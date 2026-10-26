using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.SqlClient;

using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Data;

public sealed class SqlLocationStore(string connectionString) : ILocationStore
{
    public Task<SavedLocation?> ReadAsync(string location, CancellationToken ct) =>
        TranslateSqlFailure(() => ReadCoreAsync(location, ct));

    public Task<SavedLocation> ApplyRefreshAsync(string location, CompleteCollection collection, DateTimeOffset observedAt, CancellationToken ct) =>
        TranslateSqlFailure(() => ApplyRefreshCoreAsync(location, collection, observedAt, ct));

    public async Task RecordFailedAttemptAsync(string location, string code, DateTimeOffset attemptedAt, CancellationToken ct) =>
        await TranslateSqlFailure(async () => { await RecordFailedAttemptCoreAsync(location, code, attemptedAt, ct); return true; });

    private static async Task<T> TranslateSqlFailure<T>(Func<Task<T>> operation)
    {
        try { return await operation(); }
        catch (SqlException ex) { throw new StorageException(ex); }
    }

    private async Task<SavedLocation?> ReadCoreAsync(string location, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, tx, location, "Shared", ct);
        var result = await ReadInsideAsync(connection, tx, location, ct);
        await tx.CommitAsync(ct);
        return result;
    }

    private async Task<SavedLocation> ApplyRefreshCoreAsync(
        string location,
        CompleteCollection collection,
        DateTimeOffset observedAt,
        CancellationToken ct
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, tx, location, "Exclusive", ct);
        var existing = await ReadInsideAsync(connection, tx, location, ct);
        var rows = Reconciliation.Apply(existing?.Listings ?? [], collection.Listings, observedAt);
        // Never let a hash collision merge different identities, including retained inactive records.
        foreach (var group in rows.GroupBy(x => Convert.ToHexString(Hash(x.Details.Identity))))
            if (group.Select(x => x.Details.Identity).Distinct(StringComparer.Ordinal).Count() != 1)
                throw new InvalidOperationException("Identity hash collision.");
        await UpsertLocationAsync(connection, tx, location, observedAt, observedAt, null, ct);
        foreach (var row in rows)
        {
            await using var cmd = new SqlCommand(
                """
                UPDATE dbo.Listings SET DetailsJson=@details, IsActive=@active, Discovery=@discovery, LastSeenAtUtc=@last, InactivatedAtUtc=@inactive WHERE Id=@id;
                IF @@ROWCOUNT=0 INSERT dbo.Listings(Id,Location,IdentityHash,IdentityText,DetailsJson,IsActive,Discovery,FirstSeenAtUtc,LastSeenAtUtc,InactivatedAtUtc)
                VALUES(@id,@location,@hash,@identity,@details,@active,@discovery,@first,@last,@inactive);
                """,
                connection,
                tx
            );
            cmd.Parameters.AddWithValue("@id", row.Id);
            cmd.Parameters.AddWithValue("@location", location);
            cmd.Parameters.AddWithValue("@hash", Hash(row.Details.Identity));
            cmd.Parameters.AddWithValue("@identity", row.Details.Identity);
            cmd.Parameters.AddWithValue("@details", JsonSerializer.Serialize(row.Details));
            cmd.Parameters.AddWithValue("@active", row.IsActive);
            // The column stores lowercase names, constrained by the schema CHECK.
            cmd.Parameters.AddWithValue("@discovery", row.Discovery.ToString().ToLowerInvariant());
            cmd.Parameters.AddWithValue("@first", row.FirstSeenAtUtc);
            cmd.Parameters.AddWithValue("@last", row.LastSeenAtUtc);
            cmd.Parameters.Add("@inactive", SqlDbType.DateTimeOffset).Value =
                (object?)row.InactivatedAtUtc ?? DBNull.Value;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return new(location, observedAt, observedAt, null, rows);
    }

    private async Task RecordFailedAttemptCoreAsync(
        string location,
        string code,
        DateTimeOffset attemptedAt,
        CancellationToken ct
    )
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await LockAsync(connection, tx, location, "Exclusive", ct);
        var existing = await ReadInsideAsync(connection, tx, location, ct);
        await UpsertLocationAsync(
            connection,
            tx,
            location,
            existing?.LastSuccessAtUtc,
            attemptedAt,
            code,
            ct
        );
        await tx.CommitAsync(ct);
    }

    private static byte[] Hash(string identity) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(identity));

    private static async Task LockAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string location,
        string mode,
        CancellationToken ct
    )
    {
        await using var cmd = new SqlCommand(
            "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode=@mode,@LockOwner='Transaction',@LockTimeout=10000; IF @result<0 THROW 51000,'Location lock unavailable',1;",
            connection,
            tx
        );
        cmd.Parameters.AddWithValue("@resource", "conveyancing:" + location);
        cmd.Parameters.AddWithValue("@mode", mode);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task UpsertLocationAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string location,
        DateTimeOffset? success,
        DateTimeOffset attempt,
        string? failure,
        CancellationToken ct
    )
    {
        await using var cmd = new SqlCommand(
            "UPDATE dbo.Locations SET LastSuccessAtUtc=@success,LastAttemptAtUtc=@attempt,FailureCode=@failure WHERE Location=@location; IF @@ROWCOUNT=0 INSERT dbo.Locations VALUES(@location,@success,@attempt,@failure);",
            connection,
            tx
        );
        cmd.Parameters.AddWithValue("@location", location);
        cmd.Parameters.Add("@success", SqlDbType.DateTimeOffset).Value =
            (object?)success ?? DBNull.Value;
        cmd.Parameters.AddWithValue("@attempt", attempt);
        cmd.Parameters.Add("@failure", SqlDbType.NVarChar, 80).Value =
            (object?)failure ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<SavedLocation?> ReadInsideAsync(
        SqlConnection connection,
        SqlTransaction tx,
        string location,
        CancellationToken ct
    )
    {
        await using var cmd = new SqlCommand(
            "SELECT LastSuccessAtUtc,LastAttemptAtUtc,FailureCode FROM dbo.Locations WHERE Location=@location; SELECT Id,DetailsJson,IsActive,Discovery,FirstSeenAtUtc,LastSeenAtUtc,InactivatedAtUtc,IdentityText FROM dbo.Listings WHERE Location=@location;",
            connection,
            tx
        );
        cmd.Parameters.AddWithValue("@location", location);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;
        DateTimeOffset? success = reader.IsDBNull(0)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(0);
        DateTimeOffset? attempt = reader.IsDBNull(1)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(1);
        var failure = reader.IsDBNull(2) ? null : reader.GetString(2);
        await reader.NextResultAsync(ct);
        var rows = new List<Listing>();
        while (await reader.ReadAsync(ct))
        {
            var details =
                JsonSerializer.Deserialize<SourceListing>(reader.GetString(1))
                ?? throw new InvalidOperationException("Invalid stored listing.");
            if (details.Identity != reader.GetString(7))
                throw new InvalidOperationException("Stored identity mismatch.");
            rows.Add(
                new(
                    reader.GetGuid(0),
                    details,
                    reader.GetBoolean(2),
                    Enum.Parse<Discovery>(reader.GetString(3), ignoreCase: true),
                    reader.GetFieldValue<DateTimeOffset>(4),
                    reader.GetFieldValue<DateTimeOffset>(5),
                    reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6)
                )
            );
        }
        return new(location, success, attempt, failure, rows);
    }
}
