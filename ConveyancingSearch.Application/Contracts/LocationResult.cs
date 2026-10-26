using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Application.Contracts;

public sealed record LocationResult(
    string Location,
    SearchStatus Status,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    string? Warning,
    IReadOnlyList<Listing> Listings,
    string? FailureCode = null
);

