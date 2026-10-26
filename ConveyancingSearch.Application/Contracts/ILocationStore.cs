namespace ConveyancingSearch.Application.Contracts;

public interface ILocationStore
{
    Task<SavedLocation?> ReadAsync(string location, CancellationToken cancellationToken);
    Task<SavedLocation> ApplyRefreshAsync(
        string location,
        CompleteCollection collection,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken
    );
    Task RecordFailedAttemptAsync(
        string location,
        string code,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken
    );
}
