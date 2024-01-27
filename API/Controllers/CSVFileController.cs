using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using API.Models;
using API.Services;
using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Neo4jClient;

namespace API.Controllers
{
    [ApiController]
    [Route("csv-file-upload")]
    public class CSVFileController : ControllerBase
    {
        private readonly IGraphClient _client;
        private readonly ILogger<CSVFileController> _logger;
        private readonly ICacheService _cacheService;

        public CSVFileController(
            ILogger<CSVFileController> logger,
            IGraphClient client,
            ICacheService cacheService
        )
        {
            _logger = logger;
            _client = client;
            _cacheService = cacheService;
        }

        [HttpPost("upload-cities", Name = "UploadCitiesCSV")]
        public async Task<IActionResult> CreateCities(IFormFile formFile)
        {
            try
            {
                using (var reader = new StreamReader(formFile.OpenReadStream()))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    var cityRecords = csv.GetRecords<CityCSVModel>();
                    foreach (var cityRecord in cityRecords)
                    {
                        var city = new City
                        {
                            city_id = cityRecord.city_id,
                            city_name = cityRecord.city_name,
                        };

                        await _client
                            .Cypher.Create("(c:City $city)")
                            .WithParam("city", city)
                            .ExecuteWithoutResultsAsync();

                        _cacheService.PostRequest(_cacheService.city_db_name, city.city_id, city);
                    }
                }

                return Ok("CSV file for cities uploaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating cities.");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("upload-lines", Name = "UploadLinesCSV")]
        public async Task<IActionResult> CreateLines(IFormFile formFile)
        {
            try
            {
                using (var reader = new StreamReader(formFile.OpenReadStream()))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    var lineRecords = csv.GetRecords<LineCSVModel>();
                    foreach (var lineRecord in lineRecords)
                    {
                        var line = new Line
                        {
                            line_id = lineRecord.line_id,
                            line_name = lineRecord.line_name,
                            start_city_id = lineRecord.start_city_id,
                            end_city_id = lineRecord.end_city_id,
                        };

                        await _client
                            .Cypher.Match("(c1:City { city_id: " + line.start_city_id + " })")
                            .Match("(c2:City { city_id: " + line.end_city_id + " })")
                            .Merge($"(c1)-[r1:HAS_LINE]->(c2)")
                            .Set(
                                $"r1 = {{ line_id: {line.line_id}, line_name: '{line.line_name}' }}"
                            )
                            .ExecuteWithoutResultsAsync();

                        string[] parts = line.line_name.Split('-');
                        var oppositeName = $"{parts[1]}-{parts[0]}";
                        await _client
                            .Cypher.Match("(c2:City { city_id: " + line.end_city_id + " })")
                            .Match("(c1:City { city_id: " + line.start_city_id + " })")
                            .Merge($"(c2)-[r2:HAS_LINE]->(c1)")
                            .Set(
                                $"r2 = {{ line_id: {line.line_id}, line_name: '{oppositeName}' }}"
                            )
                            .ExecuteWithoutResultsAsync();

                        _cacheService.PostRequest(_cacheService.route_db_name, line.line_id, line);
                    }
                }

                return Ok("CSV file for lines uploaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating lines.");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("upload-train-routes", Name = "UploadTrainRoutesCSV")]
        public async Task<IActionResult> CreateRoutes(IFormFile formFile)
        {
            try
            {
                using (var reader = new StreamReader(formFile.OpenReadStream()))
                using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
                {
                    var trainRouteRecords = csv.GetRecords<TrainRouteCSV>();
                    foreach (var trainRouteRecord in trainRouteRecords)
                    {
                        var trainRoute = new TrainRoute
                        {
                            line_id = trainRouteRecord.line_id,
                            city_ids = ParseCityIds(trainRouteRecord.city_ids),
                            mileage = ParseMileage(trainRouteRecord.mileage),
                        };

                        await _client
                            .Cypher.Create(
                                "(t:TrainRoute {line_id: $line_id})"
                            )
                            .WithParam("line_id", trainRoute.line_id)
                            .ExecuteWithoutResultsAsync();

                        // Create relationships between cities based on the city_ids and mileage
                        for (int i = 0; i < trainRoute.city_ids.Count - 1; i++)
                        {
                            int currentCityId = trainRoute.city_ids[i];
                            int nextCityId = trainRoute.city_ids[i + 1];

                            await _client
                                .Cypher.Match(
                                    $"(c{currentCityId}:City {{ city_id: {currentCityId} }})"
                                )
                                .Match($"(c{nextCityId}:City {{ city_id: {nextCityId} }})")
                                .Merge(
                                    $"(c{currentCityId})-[r{currentCityId}{nextCityId}:HAS_ROUTE]->(c{nextCityId})"
                                )
                                .Set(
                                    $"r{currentCityId}{nextCityId} = {{ mileage: {trainRoute.mileage[i]}, line_id: {trainRoute.line_id} }}"
                                )
                                .ExecuteWithoutResultsAsync();
                        }
                    }
                }

                return Ok("CSV file for train routes uploaded successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating train routes.");
                return StatusCode(500, "Internal server error");
            }
        }

        private List<int> ParseCityIds(string cityIds)
        {
            return cityIds.Trim('[', ']').Split(',').Select(id => int.Parse(id.Trim())).ToList();
        }

        private List<int> ParseMileage(string mileage)
        {
            return mileage.Trim('[', ']').Split(',').Select(m => int.Parse(m.Trim())).ToList();
        }

        [HttpPost("delete-data", Name = "DeleteDataFromDb")]
        public async Task<IActionResult> DeleteAll()
        {
            try
            {
                await _client
                    .Cypher.OptionalMatch("(n)<-[r]-()")
                    .Delete("r, n")
                    .ExecuteWithoutResultsAsync();
                // Gornji valjda brise samo povezane nodeove
                await _client.Cypher.OptionalMatch("(n)").Delete("n").ExecuteWithoutResultsAsync();
                return Ok("All data deleted sucessfuly");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
