using System.Text.Json;
using ConveyancingSearch.Application.Locations;
namespace ConveyancingSearch.Tests;

internal static class TestLocations
{
    internal static readonly LocationCatalogue Catalogue = new(JsonSerializer.Deserialize<Configuration>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "supported-locations.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Locations);
    internal static SupportedLocation Get(string name) => Catalogue.Resolve(name) ?? throw new UnsupportedLocationException();
    private sealed record Configuration(SupportedLocation[] Locations);
}
