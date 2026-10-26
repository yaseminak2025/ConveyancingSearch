using System.Text;
using System.Text.RegularExpressions;

namespace ConveyancingSearch.Domain;

public sealed record SourceListing(
    string Name,
    string Address,
    string? Phone,
    string? Website,
    string? ContactUrl,
    string SourceUrl
)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Identity => Normalise(Name) + "\n" + Normalise(Address);

    public static string Normalise(string value) =>
        Regex
            .Replace(value.Normalize(NormalizationForm.FormC).Trim(), @"\s+", " ")
            .ToUpperInvariant();
}

