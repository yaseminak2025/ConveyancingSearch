using ConveyancingSearch.Application.Locations;
using Xunit;
namespace ConveyancingSearch.Tests;

public class LocationCatalogueTests
{
    [Fact]
    public void Configuration_can_expand_beyond_eight_and_resolve_canonical_names()
    {
        var catalogue = new LocationCatalogue(TestLocations.Catalogue.GetAll().Append(new("york", "York", "/conveyancing+york.html")));
        Assert.Equal(9, catalogue.GetAll().Count);
        Assert.Equal("york", catalogue.Resolve("  YORK  ")!.Key);
        Assert.Null(catalogue.Resolve("Banana"));
    }
    [Fact]
    public void Duplicate_keys_and_external_source_paths_are_rejected()
    {
        var leeds = TestLocations.Get("Leeds");
        Assert.Throws<ArgumentException>(() => new LocationCatalogue([leeds, leeds]));
        Assert.Throws<ArgumentException>(() => new LocationCatalogue([leeds with { SourcePath = "https://other.example/" }]));
    }
}
