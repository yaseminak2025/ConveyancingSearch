using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Application.Locations;
using ConveyancingSearch.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ConveyancingSearch.Tests;

public sealed class ApiTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [Theory]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("Banana")]
    public async Task Invalid_city_returns_field_error_without_dependency_calls(string city)
    {
        await using var app = new Factory();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/search", new { location = city });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("errors").TryGetProperty("location", out _));
        Assert.Equal(0, app.Store.Reads);
        Assert.Equal(0, app.Source.Calls);
    }

    [Fact]
    public async Task Catalogue_exposes_public_contract()
    {
        await using var app = new Factory();
        using var client = app.CreateClient();
        var response = await client.GetAsync("/api/locations");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(8, rows.GetArrayLength());
        Assert.All(rows.EnumerateArray(), x => { Assert.True(x.TryGetProperty("key", out _)); Assert.True(x.TryGetProperty("displayName", out _)); Assert.False(x.TryGetProperty("sourcePath", out _)); });
    }

    [Fact]
    public async Task Successful_refresh_then_cache_preserve_response_contract()
    {
        await using var app = new Factory();
        using var client = app.CreateClient();
        var first = await Search(client);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var raw = await first.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"refreshed\"", raw);
        Assert.Contains("\"discovery\":\"new\"", raw);
        var refreshed = await first.Content.ReadFromJsonAsync<LocationResult>(Json);
        Assert.Equal(SearchStatus.Refreshed, refreshed!.Status);
        Assert.Single(refreshed.Listings);
        var second = await Search(client);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var cached = await second.Content.ReadFromJsonAsync<LocationResult>(Json);
        Assert.Equal(SearchStatus.Cached, cached!.Status);
        Assert.Equal(refreshed.LastSuccessAtUtc, cached.LastSuccessAtUtc);
        Assert.Equal(1, app.Source.Calls);
    }

    [Theory]
    [InlineData(false, "source_incomplete", "source website")]
    [InlineData(true, "database_unavailable", "database")]
    public async Task Expected_failure_without_saved_data_returns_503(bool database, string code, string warning)
    {
        await using var app = new Factory();
        if (database) app.Store.Failure = new StorageException(new Exception("private SQL detail"));
        else app.Source.Failure = new SourceException("source_incomplete", "private source detail");
        using var client = app.CreateClient();
        var response = await Search(client);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LocationResult>(Json);
        Assert.Equal(SearchStatus.Unavailable, body!.Status);
        Assert.Equal(code, body.FailureCode);
        Assert.Contains(warning, body.Warning);
        Assert.Empty(body.Listings);
        Assert.DoesNotContain("private", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Source_failure_with_saved_data_returns_200_fallback()
    {
        await using var app = new Factory();
        app.Store.Saved = new("leeds", DateTimeOffset.Parse("2020-01-01T00:00:00Z"), null, null,
            Reconciliation.Apply([], Factory.Collection.Listings, DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        app.Source.Failure = new SourceException("source_timeout", "timeout");
        using var client = app.CreateClient();
        var response = await Search(client);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LocationResult>(Json);
        Assert.Equal(SearchStatus.Fallback, body!.Status);
        Assert.Equal("source_timeout", body.FailureCode);
        Assert.Single(body.Listings);
    }

    [Fact]
    public async Task Programming_error_returns_safe_500_even_with_saved_data()
    {
        await using var app = new Factory();
        app.Store.Saved = new("leeds", DateTimeOffset.Parse("2020-01-01T00:00:00Z"), null, null, []);
        app.Source.Failure = new InvalidOperationException("private programming detail");
        using var client = app.CreateClient();
        var response = await Search(client);
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(500, body.GetProperty("status").GetInt32());
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.DoesNotContain("private programming detail", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, app.Store.FailedAttempts);
    }

    [Fact]
    public async Task Cancelling_one_http_request_does_not_cancel_shared_refresh()
    {
        await using var app = new Factory();
        app.Source.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = app.CreateClient();
        using var cancellation = new CancellationTokenSource();
        var first = client.PostAsJsonAsync("/api/search", new { location = "Leeds" }, cancellation.Token);
        await app.Source.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Search(client);
        cancellation.Cancel();
        app.Source.Gate.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        var response = await second;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, app.Source.Calls);
        Assert.NotNull(app.Store.Saved);
    }

    private static Task<HttpResponseMessage> Search(HttpClient client) => client.PostAsJsonAsync("/api/search", new { location = "Leeds" });

    private sealed class Factory : WebApplicationFactory<Program>
    {
        public readonly StoreFake Store = new();
        public readonly SourceFake Source = new();
        internal static readonly CompleteCollection Collection = new([new("Firm", "1 Street", null, null, null, "https://www.solicitors.com/firm.html")]);
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production").ConfigureServices(services =>
        {
            services.RemoveAll<ILocationStore>(); services.AddSingleton<ILocationStore>(Store);
            services.RemoveAll<IListingSource>(); services.AddSingleton<IListingSource>(Source);
        });
    }
    private sealed class SourceFake : IListingSource
    {
        public int Calls;
        public Exception? Failure;
        public TaskCompletionSource? Gate;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CompleteCollection> FetchAsync(SupportedLocation location, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); Started.TrySetResult();
            if (Gate is not null) await Gate.Task.WaitAsync(ct);
            if (Failure is not null) throw Failure;
            return Factory.Collection;
        }
    }
    private sealed class StoreFake : ILocationStore
    {
        public int Reads;
        public int FailedAttempts;
        public Exception? Failure;
        public SavedLocation? Saved;
        public Task<SavedLocation?> ReadAsync(string location, CancellationToken ct)
        {
            Interlocked.Increment(ref Reads);
            if (Failure is not null) throw Failure;
            return Task.FromResult(Saved);
        }
        public Task<SavedLocation> ApplyRefreshAsync(string location, CompleteCollection collection, DateTimeOffset now, CancellationToken ct)
        {
            Saved = new(location, now, now, null, Reconciliation.Apply(Saved?.Listings ?? [], collection.Listings, now));
            return Task.FromResult(Saved);
        }
        public Task RecordFailedAttemptAsync(string location, string code, DateTimeOffset attemptedAt, CancellationToken ct)
        { FailedAttempts++; return Task.CompletedTask; }
    }
}
