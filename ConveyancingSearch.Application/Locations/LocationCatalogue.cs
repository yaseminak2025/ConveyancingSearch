using System.Text.RegularExpressions;
using ConveyancingSearch.Domain;
namespace ConveyancingSearch.Application.Locations;

public sealed class LocationCatalogue
{
    private readonly IReadOnlyList<SupportedLocation> locations;
    private readonly Dictionary<string, SupportedLocation> lookup = new(StringComparer.Ordinal);

    public LocationCatalogue(IEnumerable<SupportedLocation> entries)
    {
        var items = entries.ToArray();
        if (items.Length == 0) throw new ArgumentException("Configure at least one supported location.");
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || string.IsNullOrWhiteSpace(item.DisplayName)
                || item.Key.Length > 80 || item.DisplayName.Length > 80
                || !Regex.IsMatch(item.Key, @"^[a-z][a-z-]*$")
                || !Regex.IsMatch(item.DisplayName, @"^[\p{L}][\p{L}\s.'-]*$")
                || item.Key != LocationIdentity.Normalize(item.Key) || !keys.Add(item.Key)
                || !Regex.IsMatch(item.SourcePath ?? "", @"^/conveyancing\+[a-z0-9-]+\.html$"))
                throw new ArgumentException("Invalid or duplicate supported location configuration.");
            foreach (var name in new[] { item.Key, item.DisplayName })
            {
                var normalized = LocationIdentity.Normalize(name);
                if (lookup.TryGetValue(normalized, out var existing) && existing.Key != item.Key)
                    throw new ArgumentException("Ambiguous supported location name.");
                lookup[normalized] = item;
            }
        }
        locations = Array.AsReadOnly(items);
    }

    public SupportedLocation? Resolve(string? input) => string.IsNullOrWhiteSpace(input)
        ? null : lookup.GetValueOrDefault(LocationIdentity.Normalize(input));
    public IReadOnlyList<SupportedLocation> GetAll() => locations;
}
