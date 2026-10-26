namespace ConveyancingSearch.Domain;

public sealed record Listing(
    Guid Id,
    SourceListing Details,
    bool IsActive,
    Discovery Discovery,
    DateTimeOffset FirstSeenAtUtc,
    DateTimeOffset LastSeenAtUtc,
    DateTimeOffset? InactivatedAtUtc
);

