using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Domain;

public static class Reconciliation
{
    public static IReadOnlyList<Listing> Apply(
        IReadOnlyList<Listing> existing,
        IReadOnlyList<SourceListing> incoming,
        DateTimeOffset now
    )
    {
        var remaining = existing.ToDictionary(x => x.Details.Identity, StringComparer.Ordinal);
        var result = new List<Listing>();
        foreach (var item in incoming)
        {
            if (remaining.Remove(item.Identity, out var previous))
                result.Add(
                    previous with
                    {
                        Details = item,
                        IsActive = true,
                        Discovery = previous.IsActive ? Discovery.Existing : Discovery.Returned,
                        LastSeenAtUtc = now,
                        InactivatedAtUtc = null,
                    }
                );
            else
                result.Add(new(Guid.NewGuid(), item, true, Discovery.New, now, now, null));
        }
        result.AddRange(
            remaining.Values.Select(x =>
                x with
                {
                    IsActive = false,
                    Discovery = Discovery.Existing,
                    InactivatedAtUtc = x.IsActive ? now : x.InactivatedAtUtc,
                }
            )
        );
        return result;
    }
}
