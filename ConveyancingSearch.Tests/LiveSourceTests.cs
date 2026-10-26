using System.Diagnostics;
using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;
using ConveyancingSearch.Service.Solicitors;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace ConveyancingSearch.Tests;

public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("CONVEYANCING_LIVE_TEST") != "1")
            Skip =
                "Opt-in live source check; set CONVEYANCING_LIVE_TEST=1. Makes real upstream requests.";
    }
}

public class LiveSourceTests(ITestOutputHelper output)
{
    [LiveFact]
    public async Task Live_source_returns_complete_collection()
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ConveyancingSearchAssignment/1.0");
        using var source = new SolicitorsSource(client, Options.Create(new SourceOptions()));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var city = Environment.GetEnvironmentVariable("CONVEYANCING_LIVE_CITY") ?? "Leeds";
        var watch = Stopwatch.StartNew();
        var result = await source.FetchAsync(TestLocations.Get(city), timeout.Token);
        Assert.NotEmpty(result.Listings);
        output.WriteLine(
            $"{city}: {result.Listings.Count} unique listings in {watch.Elapsed.TotalSeconds:F1}s"
        );
    }
}
