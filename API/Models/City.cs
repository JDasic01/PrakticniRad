namespace API.Models;

public class City
{
    public int id { get; private set; }
    public string? name { get; set; }
    public string? seeSection { get; set; }
    public string? doSection { get; set; }
    public string? guide { get; set; }
    public List<AvailableRoute> availableRoutes { get; set; }
}