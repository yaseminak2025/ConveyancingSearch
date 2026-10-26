using ConveyancingSearch.Application.Locations;
namespace ConveyancingSearch.Api.Endpoints.Locations;

public static class LocationEndpoints
{
    public static void MapLocations(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet("/api/locations", (LocationCatalogue catalogue) =>
            Results.Ok(catalogue.GetAll().Select(x => new { x.Key, x.DisplayName })));
}
