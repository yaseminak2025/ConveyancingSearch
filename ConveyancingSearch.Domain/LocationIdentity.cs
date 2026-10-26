using System.Text.RegularExpressions;

namespace ConveyancingSearch.Domain;

/// <summary>Stable key for saved locations and shared searches; preserves existing stored keys.</summary>
public static class LocationIdentity
{
    public static string Normalize(string value) =>
        Regex.Replace(value.Trim().ToLowerInvariant(), @"[\s-]+", "-");
}
