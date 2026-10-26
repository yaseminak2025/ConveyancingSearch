using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;
using ConveyancingSearch.Service.Solicitors;
using Xunit;

namespace ConveyancingSearch.Tests;

public class SourceTests
{
    [Theory]
    [InlineData("leeds", 68)]
    [InlineData("bristol", 58)]
    [InlineData("birmingham", 74)]
    public void Captured_results_preserve_branches_and_remove_duplicate_identities(
        string city,
        int count
    )
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", city + ".html")
        );
        var result = SourcePage.Parse(
            html,
            new Uri("https://www.solicitors.com/conveyancing+" + city + ".html"),
            city
        );
        Assert.Equal(count, result.Listings.Count);
        Assert.All(result.Listings, x => Assert.DoesNotContain("(2864)", x.Name));
    }

    [Fact]
    public void Unrecognised_empty_page_is_not_a_complete_collection() =>
        Assert.Throws<SourceException>(() =>
            SourcePage.Parse(
                "<html><body>Blocked</body></html>",
                new Uri("https://www.solicitors.com/conveyancing+leeds.html"),
                "leeds"
            )
        );
}

public class SourceCompletenessTests
{
    [Theory]
    [InlineData("central-london", 27)]
    [InlineData("abbey-wood", 10)]
    [InlineData("acton", 4)]
    [InlineData("greater-london", 3)]
    [InlineData("manchester", 72)]
    [InlineData("sheffield", 37)]
    [InlineData("bradford", 25)]
    [InlineData("liverpool", 60)]
    public void Other_captured_shapes_have_expected_unique_counts(string city, int count)
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", city + ".html")
        );
        Assert.Equal(
            count,
            SourcePage
                .Parse(
                    html,
                    new Uri("https://www.solicitors.com/conveyancing+" + city + ".html"),
                    city
                )
                .Listings.Count
        );
    }

    [Fact]
    public void London_directory_is_navigation_not_empty_results()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "london-directory.html")
        );
        var page = SourcePage.Parse(
            html,
            new Uri("https://www.solicitors.com/london-conveyancing-towns.html"),
            "London"
        );
        Assert.Empty(page.Listings);
        Assert.Equal(252, page.Children.Count);
    }

    [Fact]
    public void Broken_required_field_or_truncated_response_fails_completeness()
    {
        var html = File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "leeds.html")
        );
        var uri = new Uri("https://www.solicitors.com/conveyancing+leeds.html");
        // Synthetic corruption of a real capture, not an invented successful fixture.
        Assert.Throws<SourceException>(() =>
            SourcePage.Parse(html.Replace("<address>", "<missing>"), uri, "Leeds")
        );
        Assert.Throws<SourceException>(() =>
            SourcePage.Parse(html[..(html.Length / 2)], uri, "Leeds")
        );
    }
}
