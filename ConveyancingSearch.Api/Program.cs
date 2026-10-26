using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConveyancingSearch.Api.Endpoints.Locations;
using ConveyancingSearch.Api.Endpoints.Search;
using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Application.Locations;
using ConveyancingSearch.Application.Search;
using ConveyancingSearch.Data;
using ConveyancingSearch.Service.Solicitors;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("supported-locations.json", optional: false, reloadOnChange: false);
var catalogue = new LocationCatalogue(
    builder.Configuration.GetSection("Locations").Get<SupportedLocation[]>() ?? []);
builder.Services.AddSingleton(catalogue);
builder.Services.AddProblemDetails();
// Enums go on the wire as camelCase names ("cached", "new"), matching the UI's schema.
builder.Services.ConfigureHttpJsonOptions(json =>
    json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
builder.Services.AddSingleton(TimeProvider.System);
builder
    .Services.AddOptions<SourceOptions>()
    .BindConfiguration("Source")
    .Validate(
        x =>
            x.ParallelDownloads is >= 1 and <= 8
            && x.MaxPages is >= 1 and <= 1024
            && x.RequestTimeoutSeconds is >= 1 and <= 60,
        "Source limits are invalid."
    )
    .ValidateOnStart();
builder.Services.AddOptions<SearchOptions>()
    .BindConfiguration("Search")
    .Validate(
        x => x.FreshnessHours is >= 1 and <= 168 && x.RefreshTimeoutSeconds is >= 1 and <= 600,
        "Search settings are invalid."
    )
    .ValidateOnStart();
builder.Services.AddSingleton<ILocationStore>(_ => new SqlLocationStore(
    builder.Configuration.GetConnectionString("Conveyancing")
        ?? throw new InvalidOperationException(
            "Configure ConnectionStrings:Conveyancing; see SETUP.md."
        )
));
builder.Services.AddSingleton<IListingSource>(sp =>
{
    var handler = new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.All,
    };
    var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("ConveyancingSearchAssignment/1.0");
    return new SolicitorsSource(
        client,
        sp.GetRequiredService<IOptions<SourceOptions>>()
    );
});
builder.Services.AddSingleton<LocationSearch>();
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(context =>
    Results.Problem(statusCode: 500, title: "An unexpected error occurred.",
        extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier })
        .ExecuteAsync(context)));
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapSearch();
app.MapLocations();
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program { }
