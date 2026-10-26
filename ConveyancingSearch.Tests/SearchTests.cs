using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;
using ConveyancingSearch.Application.Search;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using ConveyancingSearch.Application.Locations;

namespace ConveyancingSearch.Tests;

public class SearchTests
{
    [Fact]
    public async Task Fresh_data_is_reused_but_exactly_24_hours_requires_refresh()
    {
        var clock = new Clock();
        var store = new MemoryStore();
        var source = new ControlledSource();
        var search = Create(store, source, clock);
        var first = await search.SearchAsync("Leeds", default);
        Assert.Equal(SearchStatus.Refreshed, first.Status);
        clock.Now = clock.Now.AddHours(23);
        var cached = await search.SearchAsync("  LEEDS  ", default);
        Assert.Equal(SearchStatus.Cached, cached.Status);
        Assert.Equal(1, source.Calls);
        Assert.Equal(Discovery.New, cached.Listings[0].Discovery);
        clock.Now = clock.Now.AddHours(1);
        await search.SearchAsync("Leeds", default);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Freshness_window_comes_from_configuration()
    {
        var clock = new Clock();
        var source = new ControlledSource();
        var search = Create(new MemoryStore(), source, clock, new SearchOptions { FreshnessHours = 1 });
        await search.SearchAsync("Leeds", default);
        clock.Now = clock.Now.AddHours(2);
        var result = await search.SearchAsync("Leeds", default);
        Assert.Equal(SearchStatus.Refreshed, result.Status);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Failed_source_preserves_saved_data_and_success_timestamp()
    {
        var clock = new Clock();
        var store = new MemoryStore();
        var source = new ControlledSource();
        var search = Create(store, source, clock);
        var first = await search.SearchAsync("Leeds", default);
        clock.Now = clock.Now.AddDays(1);
        source.Fail = true;
        var result = await search.SearchAsync("Leeds", default);
        Assert.Equal(SearchStatus.Fallback, result.Status);
        Assert.Equal(first.LastSuccessAtUtc, result.LastSuccessAtUtc);
        Assert.Equal(first.Listings[0].Id, result.Listings[0].Id);
        Assert.NotNull(result.Warning);
    }

    [Fact]
    public async Task Unsaved_discoveries_are_not_published_when_store_write_fails()
    {
        var clock = new Clock();
        var store = new MemoryStore { FailWrites = true };
        var source = new ControlledSource();
        var result = await Create(store, source, clock).SearchAsync("Leeds", default);
        Assert.Equal(SearchStatus.Unavailable, result.Status);
        Assert.Equal("database_unavailable", result.FailureCode);
        Assert.Contains("database", result.Warning);
        Assert.Empty(result.Listings);
    }

    [Fact]
    public async Task Concurrent_waiters_share_failure_and_one_cancellation_does_not_cancel_the_other()
    {
        var clock = new Clock();
        var store = new MemoryStore();
        var source = new ControlledSource
        {
            Gate = new(TaskCreationOptions.RunContinuationsAsynchronously),
            Fail = true,
        };
        var search = Create(store, source, clock);
        using var cancellation = new CancellationTokenSource();
        var first = search.SearchAsync("Leeds", cancellation.Token);
        var second = search.SearchAsync("Leeds", default);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        source.Gate.SetResult();
        var result = await second;
        Assert.Equal(SearchStatus.Unavailable, result.Status);
        Assert.Equal(1, source.Calls);
        await search.SearchAsync("Leeds", default);
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Unsupported_city_does_not_access_store_or_source()
    {
        var store = new MemoryStore();
        var source = new ControlledSource();
        await Assert.ThrowsAsync<UnsupportedLocationException>(() => Create(store, source, new Clock()).SearchAsync("Banana", default));
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, source.Calls);
    }

    private static LocationSearch Create(
        MemoryStore store,
        ControlledSource source,
        Clock clock,
        SearchOptions? options = null
    ) =>
        new(
            store,
            source,
            clock,
            new Lifetime(),
            Options.Create(options ?? new SearchOptions()),
            NullLogger<LocationSearch>.Instance,
            TestLocations.Catalogue
        );

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Lifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => default;
        public CancellationToken ApplicationStopping => default;
        public CancellationToken ApplicationStopped => default;

        public void StopApplication() { }
    }

    private sealed class ControlledSource : IListingSource
    {
        public int Calls;
        public bool Fail;
        public TaskCompletionSource? Gate;

        public async Task<CompleteCollection> FetchAsync(SupportedLocation location, CancellationToken ct)
        {
            Calls++;
            if (Gate is not null)
                await Gate.Task.WaitAsync(ct);
            if (Fail)
                throw new SourceException("source_incomplete", "Broken page");
            return new([
                new("Firm", "1 Street", null, null, null, "https://www.solicitors.com/firm.html"),
            ]);
        }
    }

    private sealed class MemoryStore : ILocationStore
    {
        private SavedLocation? saved;
        public bool FailWrites;

        public int Reads;
        public Task<SavedLocation?> ReadAsync(string location, CancellationToken ct)
        {
            Reads++;
            return Task.FromResult(saved);
        }

        public Task<SavedLocation> ApplyRefreshAsync(
            string location,
            CompleteCollection collection,
            DateTimeOffset now,
            CancellationToken ct
        )
        {
            if (FailWrites)
                throw new StorageException(new Exception("Write failed"));
            saved = new(
                location,
                now,
                now,
                null,
                Reconciliation.Apply(saved?.Listings ?? [], collection.Listings, now)
            );
            return Task.FromResult(saved);
        }

        public Task RecordFailedAttemptAsync(
            string location,
            string code,
            DateTimeOffset now,
            CancellationToken ct
        )
        {
            if (saved is not null)
                saved = saved with { LastAttemptAtUtc = now, FailureCode = code };
            return Task.CompletedTask;
        }
    }
}
