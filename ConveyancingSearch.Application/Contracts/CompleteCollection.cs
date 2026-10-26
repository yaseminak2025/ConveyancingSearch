using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Application.Contracts;

public sealed record CompleteCollection(IReadOnlyList<SourceListing> Listings);

