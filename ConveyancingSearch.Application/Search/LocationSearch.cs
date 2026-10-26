using ConveyancingSearch.Application.Locations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Application.Search;

public sealed class LocationSearch(
    ILocationStore store,
    IListingSource source,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    IOptions<SearchOptions> options,
    ILogger<LocationSearch> logger,
    LocationCatalogue catalogue
)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<LocationResult>>> flights = new(
        StringComparer.Ordinal
    );

    public async Task<LocationResult> SearchAsync(
        string location,
        CancellationToken callerCancellation
    )
    {
        var supported = catalogue.Resolve(location) ?? throw new UnsupportedLocationException();
        var key = supported.Key;
        var lazy = flights.GetOrAdd(
            key,
            k => new Lazy<Task<LocationResult>>(
                () => RefreshAsync(supported),
                LazyThreadSafetyMode.ExecutionAndPublication
            )
        );
        var task = lazy.Value;
        _ = task.ContinueWith(
            _ => flights.TryRemove(new KeyValuePair<string, Lazy<Task<LocationResult>>>(key, lazy)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
        return await task.WaitAsync(callerCancellation);
    }

    private async Task<LocationResult> RefreshAsync(SupportedLocation supported)
    {
        var location = supported.Key;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            lifetime.ApplicationStopping
        );
        timeout.CancelAfter(TimeSpan.FromSeconds(options.Value.RefreshTimeoutSeconds));
        var ct = timeout.Token;
        SavedLocation? saved = null;
        try
        {
            saved = await store.ReadAsync(location, ct);
            if (
                saved?.LastSuccessAtUtc is { } success
                && clock.GetUtcNow() - success < TimeSpan.FromHours(options.Value.FreshnessHours)
            )
                return Outcome(saved, SearchStatus.Cached, null);
            var collection = await source.FetchAsync(supported, ct);
            var committed = await store.ApplyRefreshAsync(
                location,
                collection,
                clock.GetUtcNow(),
                ct
            );
            return Outcome(committed, SearchStatus.Refreshed, null);
        }
        catch (Exception ex) when (
            ex is SourceException or StorageException ||
            ex is OperationCanceledException && timeout.IsCancellationRequested && !lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var code =
                ex is SourceException sourceError ? sourceError.Code
                : ex is StorageException ? "database_unavailable"
                : ex is OperationCanceledException ? "refresh_timeout"
                : "refresh_failed";
            logger.LogWarning(ex, "Refresh failed for {Location}: {Code}", location, code);
            // A bounded independent cleanup token lets us record a timeout without extending it indefinitely.
            using var cleanup = CancellationTokenSource.CreateLinkedTokenSource(
                lifetime.ApplicationStopping
            );
            cleanup.CancelAfter(TimeSpan.FromSeconds(5));
            try
            {
                // Re-read after an ambiguous commit before choosing fallback; if unavailable use previously read data.
                saved = await store.ReadAsync(location, cleanup.Token) ?? saved;
                await store.RecordFailedAttemptAsync(
                    location,
                    code,
                    clock.GetUtcNow(),
                    cleanup.Token
                );
            }
            catch (Exception storageError) when (storageError is StorageException || storageError is OperationCanceledException && cleanup.IsCancellationRequested)
            {
                logger.LogWarning(
                    storageError,
                    "Could not record failed attempt for {Location}",
                    location
                );
            }
            var reason = ex switch
            {
                StorageException => "The database operation failed. ",
                OperationCanceledException => "The refresh deadline was exceeded. ",
                _ => "The source website could not be refreshed. "
            };
            var warning =
                reason
                + (
                    saved?.LastSuccessAtUtc is null
                        ? "No saved results are available."
                        : "Showing the last saved results."
                );
            return saved?.LastSuccessAtUtc is not null
                ? Outcome(saved with { LastAttemptAtUtc = clock.GetUtcNow() }, SearchStatus.Fallback, warning) with { FailureCode = code }
                : new(location, SearchStatus.Unavailable, null, clock.GetUtcNow(), warning, [], code);
        }
    }

    private static LocationResult Outcome(SavedLocation saved, SearchStatus status, string? warning) =>
        new(
            saved.Location,
            status,
            saved.LastSuccessAtUtc,
            saved.LastAttemptAtUtc,
            warning,
            [.. saved
                .Listings.Where(x => x.IsActive)
                .OrderBy(x => x.Details.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Details.Address, StringComparer.OrdinalIgnoreCase)]
        );
}
