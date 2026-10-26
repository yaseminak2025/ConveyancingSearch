using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Application.Contracts;

public sealed record SavedLocation(
    string Location,
    DateTimeOffset? LastSuccessAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    string? FailureCode,
    IReadOnlyList<Listing> Listings
);

