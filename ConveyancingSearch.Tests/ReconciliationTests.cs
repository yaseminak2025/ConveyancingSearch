using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;
using Xunit;

namespace ConveyancingSearch.Tests;

public class ReconciliationTests
{
    [Fact]
    public void Initial_collection_is_new_and_matching_names_at_different_addresses_stay_separate()
    {
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var incoming = new[]
        {
            new SourceListing(
                "Example LLP",
                "1 High Street",
                null,
                null,
                null,
                "https://www.solicitors.com/example.html"
            ),
            new SourceListing(
                "Example LLP",
                "2 High Street",
                null,
                null,
                null,
                "https://www.solicitors.com/example.html"
            ),
        };
        var rows = Reconciliation.Apply([], incoming, now);
        Assert.Equal(2, rows.Count);
        Assert.All(
            rows,
            row =>
            {
                Assert.Equal(Discovery.New, row.Discovery);
                Assert.True(row.IsActive);
                Assert.Equal(now, row.FirstSeenAtUtc);
            }
        );
    }
}

public class LifecycleTests
{
    [Fact]
    public void Refresh_updates_matches_retains_missing_and_reactivates_returned()
    {
        var first = DateTimeOffset.Parse("2026-10-20T00:00:00Z");
        var next = first.AddDays(1);
        var a = new SourceListing(
            "Alpha",
            "1 Street",
            "111",
            null,
            null,
            "https://www.solicitors.com/a.html"
        );
        var b = a with { Name = "Beta" };
        var c = a with { Name = "Gamma" };
        var initial = Reconciliation.Apply([], [a, b], first);
        var refreshed = Reconciliation.Apply(initial, [a with { Phone = "222" }, c], next);
        Assert.Equal(3, refreshed.Count);
        var matched = Assert.Single(refreshed, x => x.Details.Name == "Alpha");
        Assert.Equal("222", matched.Details.Phone);
        Assert.Equal(first, matched.FirstSeenAtUtc);
        Assert.Equal(Discovery.Existing, matched.Discovery);
        var missing = Assert.Single(refreshed, x => x.Details.Name == "Beta");
        Assert.False(missing.IsActive);
        Assert.Equal(next, missing.InactivatedAtUtc);
        var returned = Reconciliation.Apply(refreshed, [a, b, c], next.AddDays(1));
        var restored = Assert.Single(returned, x => x.Details.Name == "Beta");
        Assert.Equal(missing.Id, restored.Id);
        Assert.Equal(Discovery.Returned, restored.Discovery);
        Assert.Null(restored.InactivatedAtUtc);
    }

    [Fact]
    public void Normalised_identity_matches_case_and_whitespace_but_not_new_addresses()
    {
        var a = new SourceListing(
            "Example LLP",
            "1 High Street",
            null,
            null,
            null,
            "https://www.solicitors.com/a.html"
        );
        var now = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var initial = Reconciliation.Apply([], [a], now);
        var same = Reconciliation.Apply(
            initial,
            [a with { Name = " EXAMPLE   llp ", Address = "1  HIGH STREET" }],
            now.AddDays(1)
        );
        Assert.Equal(initial[0].Id, Assert.Single(same).Id);
        var moved = Reconciliation.Apply(
            initial,
            [a with { Address = "2 High Street" }],
            now.AddDays(1)
        );
        Assert.Equal(2, moved.Count);
        Assert.Equal(Discovery.New, Assert.Single(moved, x => x.IsActive).Discovery);
    }
}
