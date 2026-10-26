using ConveyancingSearch.Application.Locations;
namespace ConveyancingSearch.Application.Contracts;

public interface IListingSource
{
    Task<CompleteCollection> FetchAsync(SupportedLocation location, CancellationToken cancellationToken);
}

