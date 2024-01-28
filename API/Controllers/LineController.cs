using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Services;
using API.Models;
using Microsoft.AspNetCore.Mvc;
using Neo4jClient;

namespace API.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class LineController : ControllerBase
    {
        private readonly IGraphClient _client;
        private readonly ICacheService _cacheService;
        private readonly IMessageService<Line> _messageService;

        public LineController(
            IGraphClient client,
            ICacheService cacheService,
            IMessageService<Line> messageService
        )
        {
            _client = client;
            _cacheService = cacheService;
            _messageService = messageService;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var lines = await _client
                .Cypher.Match("(n:Line)")
                .Return(n => n.As<Line>())
                .ResultsAsync;

            return Ok(lines);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(int id)
        {
            var line = await _client
                .Cypher.Match("(l:Line)")
                .Where((Line l) => l.line_id == id)
                .Return(l => l.As<Line>())
                .ResultsAsync;

            return Ok(line.LastOrDefault());
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] Line line)
        {
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

            return Ok();
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] Line line)
        {
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

            return Ok();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            await _client
                .Cypher
                .Match("(c1:City)-[r1:HAS_LINE]->(c2:City)")
                .Where((Line r1) => r1.line_id == id)
                .Delete("r1")
                .ExecuteWithoutResultsAsync();

            await _client
                .Cypher
                .Match("(c2:City)-[r2:HAS_LINE]->(c1:City)")
                .Where((Line r2) => r2.line_id == id)
                .Delete("r2")
                .ExecuteWithoutResultsAsync();

                return Ok();
        }
    }
}
