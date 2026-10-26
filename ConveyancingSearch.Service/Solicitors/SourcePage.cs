using System.Net;
using System.Text.RegularExpressions;

using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Service.Solicitors;

public sealed record SourcePage(IReadOnlyList<SourceListing> Listings, IReadOnlyList<Uri> Children)
{
    public static SourcePage Parse(string html, Uri page, string location)
    {
        var root = HtmlNode.Read(html);
        var directory = root.All().FirstOrDefault(x => x.HasClass("alphabet-list"));
        if (
            directory is not null
            && page.AbsolutePath.EndsWith(
                "-conveyancing-towns.html",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            var children = directory.All().Where(x => x.Tag == "a").Select(x =>
            {
                var link = SafeLink(page, x.Attr("href"));
                if (link is null || link.Scheme != "https" || link.Host != "www.solicitors.com" || !link.IsDefaultPort || link.Query.Length != 0 || link.Fragment.Length != 0)
                    throw Invalid("Unrecognised required directory link.");
                var path = Uri.UnescapeDataString(link.AbsolutePath);
                if (!path.StartsWith("/conveyancing+", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                    throw Invalid("Unrecognised required directory route.");
                return new Uri("https://www.solicitors.com" + path);
            }).Distinct().ToArray();
            if (children.Length == 0)
                throw Invalid("Directory has no recognised locations.");
            return new([], children);
        }
        var heading =
            root.All().FirstOrDefault(x => x.HasClass("h4") && x.HasClass("text-center"))?.Text()
            ?? "";
        var match = Regex.Match(
            heading,
            @"^(\d+) Conveyancing Solicitors found in (.+)$",
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        );
        if (!match.Success || LocationIdentity.Normalize(match.Groups[2].Value) != LocationIdentity.Normalize(location))
            throw Invalid("Results heading does not identify the requested location.");
        var section = root.All().SingleOrDefault(x => x.HasClass("result-section"));
        if (section is null)
            throw Invalid("Results container missing.");
        var blocks = section.All().Where(x => x.HasClass("result-item")).ToArray();
        if (blocks.Length == 0 || blocks.Length != int.Parse(match.Groups[1].Value))
            throw Invalid("Incomplete or unverified empty results.");
        if (
            section
                .All()
                .Any(x => x.HasClass("pagination") || x.Attr("rel").Split(' ').Contains("next"))
        )
            throw Invalid("Unverified pagination.");
        var listings = new List<SourceListing>();
        foreach (var block in blocks)
        {
            var name = block.All().FirstOrDefault(x => x.HasClass("h2"))?.OwnText();
            var address = block.All().FirstOrDefault(x => x.Tag == "address")?.Text();
            var profile = block.All().FirstOrDefault(x => x.HasClass("link-map"));
            var source = SafeLink(page, profile?.Attr("href"));
            if (
                string.IsNullOrWhiteSpace(name)
                || string.IsNullOrWhiteSpace(address)
                || source is null
            )
                throw Invalid("Listing identity or source link missing.");
            var anchors = block.All().Where(x => x.Tag == "a").ToArray();
            var phone = anchors
                .FirstOrDefault(x =>
                    x.Attr("href").StartsWith("tel:", StringComparison.OrdinalIgnoreCase)
                )
                ?.Text();
            var website = anchors.FirstOrDefault(x => x.Text() == "Website");
            var contact = anchors.FirstOrDefault(x => x.Text() == "Email");
            listings.Add(
                new(
                    name,
                    address,
                    phone,
                    SafeLink(page, website?.Attr("href"))?.AbsoluteUri,
                    SafeLink(page, contact?.Attr("href"))?.AbsoluteUri,
                    source.AbsoluteUri
                )
            );
        }
        return new(Merge(listings), []);
    }

    public static IReadOnlyList<SourceListing> Merge(IEnumerable<SourceListing> items) =>
        items
            .GroupBy(x => x.Identity, StringComparer.Ordinal)
            .Select(g =>
            {
                var first = g.First();
                return first with
                {
                    Phone = g.Select(x => x.Phone)
                        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)),
                    Website = g.Select(x => x.Website).FirstOrDefault(x => x is not null),
                    ContactUrl = g.Select(x => x.ContactUrl).FirstOrDefault(x => x is not null),
                };
            })
            .ToArray();

    private static Uri? SafeLink(Uri page, string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(page, value, out var uri)
        && uri.Scheme is "http" or "https"
            ? uri
            : null;

    private static SourceException Invalid(string message) => new("source_incomplete", message);
}

