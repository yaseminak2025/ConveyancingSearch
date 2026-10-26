using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;
using ConveyancingSearch.Data;
using Microsoft.Data.SqlClient;
using Xunit;

namespace ConveyancingSearch.Tests;

public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CONVEYANCING_TEST_SQL")))
            Skip =
                "Set CONVEYANCING_TEST_SQL to a disposable database with database/001-schema.sql applied. Real SQL verification has not run.";
    }
}

public class SqlStoreTests
{
    [SqlFact]
    public async Task Real_SQL_preserves_atomic_state_under_concurrency_and_failed_writes()
    {
        var connection = Environment.GetEnvironmentVariable("CONVEYANCING_TEST_SQL")!;
        var store = new SqlLocationStore(connection);
        var city = "test-" + Guid.NewGuid().ToString("N");
        var first = DateTimeOffset.Parse("2026-10-25T12:00:00Z");
        var a = new SourceListing(
            "Alpha",
            "1 Street",
            null,
            null,
            null,
            "https://www.solicitors.com/a.html"
        );
        try
        {
            await store.ApplyRefreshAsync(city, new([a]), first, default);
            // Duplicate identities deliberately violate the unique constraint after the first new insert.
            var b = a with
            {
                Name = "Beta",
            };
            await Assert.ThrowsAsync<StorageException>(() =>
                store.ApplyRefreshAsync(city, new([b, b]), first.AddDays(1), default)
            );
            var unchanged = await store.ReadAsync(city, default);
            Assert.NotNull(unchanged);
            Assert.Equal(first, unchanged.LastSuccessAtUtc);
            Assert.Equal("Alpha", Assert.Single(unchanged.Listings).Details.Name);
            await Task.WhenAll(
                Enumerable
                    .Range(0, 4)
                    .Select(_ =>
                        store.ApplyRefreshAsync(city, new([a, b]), first.AddDays(1), default)
                    )
            );
            var after = await store.ReadAsync(city, default);
            Assert.NotNull(after);
            Assert.Equal(2, after.Listings.Count);
            Assert.All(after.Listings, x => Assert.True(x.IsActive));
            await store.RecordFailedAttemptAsync(
                city,
                "source_incomplete",
                first.AddDays(2),
                default
            );
            var failed = await store.ReadAsync(city, default);
            Assert.NotNull(failed);
            Assert.Equal(first.AddDays(1), failed.LastSuccessAtUtc);
            Assert.Equal("source_incomplete", failed.FailureCode);
            Assert.Equal(2, failed.Listings.Count);
        }
        finally
        {
            await using var db = new SqlConnection(connection);
            await db.OpenAsync();
            await using var cmd = new SqlCommand(
                "DELETE FROM dbo.Listings WHERE Location=@city; DELETE FROM dbo.Locations WHERE Location=@city;",
                db
            );
            cmd.Parameters.AddWithValue("@city", city);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
