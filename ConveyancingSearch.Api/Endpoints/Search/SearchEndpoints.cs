using System.Text.RegularExpressions;
using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Application.Locations;
using ConveyancingSearch.Application.Search;
using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Api.Endpoints.Search;

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearch(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/api/search",
            async (SearchRequest request, LocationSearch search, CancellationToken ct) =>
            {
                var location = request.Location?.Trim();
                if (
                    string.IsNullOrWhiteSpace(location)
                    || location.Length > 80
                    || !Regex.IsMatch(
                        location,
                        @"^[\p{L}][\p{L}\s.'-]*$",
                        RegexOptions.None,
                        TimeSpan.FromSeconds(1)
                    )
                )
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]>
                        {
                            ["location"] =
                            [
                                "Enter a city name of 1 to 80 characters, using letters, spaces, apostrophes, periods or hyphens.",
                            ],
                        }
                    );
                try
                {
                    var result = await search.SearchAsync(location, ct);
                    return Results.Json(
                        result,
                        statusCode: result.Status == SearchStatus.Unavailable ? 503 : 200
                    );
                }
                catch (UnsupportedLocationException ex)
                {
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["location"] = [ex.Message] });
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // Client disconnected; the shared refresh continues and is saved for the next search.
                    return Results.Empty;
                }
            }
        );
        return endpoints;
    }
}
