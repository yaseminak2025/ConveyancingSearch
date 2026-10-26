using System.Net;
using System.Text.RegularExpressions;
using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Service.Solicitors;

// A bounded source-specific HTML tokenizer. It does not execute scripts or repair arbitrary HTML.
internal sealed class HtmlNode(string tag, Dictionary<string, string>? attributes = null)
{
    public string Tag { get; } = tag;
    private readonly Dictionary<string, string> attributes =
        attributes ?? new(StringComparer.OrdinalIgnoreCase);
    private readonly List<HtmlNode> children = [];
    private sealed record Content(string? Value, HtmlNode? Child);
    private readonly List<Content> contents = [];

    public string Attr(string name) => attributes.GetValueOrDefault(name, "");

    public bool HasClass(string name) =>
        Attr("class").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name);

    public string OwnText() => Clean(string.Concat(contents.Where(x => x.Value is not null).Select(x => WebUtility.HtmlDecode(x.Value))));

    private string DecodedText() => string.Concat(contents.Select(x => x.Child is { } child ? child.DecodedText() : WebUtility.HtmlDecode(x.Value)));
    public string Text() => Clean(DecodedText());

    public IEnumerable<HtmlNode> All()
    {
        yield return this;
        foreach (var child in children)
            foreach (var node in child.All())
                yield return node;
    }

    private static string Clean(string value) =>
        Regex.Replace(value, @"\s+", " ").Trim();

    public static HtmlNode Read(string html)
    {
        if (html.Length > 4_000_000)
            throw new SourceException("source_limit", "Source response too large.");
        var root = new HtmlNode("root");
        var stack = new List<HtmlNode> { root };
        var index = 0;
        while (index < html.Length)
        {
            if (html[index] != '<')
            {
                var end = html.IndexOf('<', index);
                if (end < 0)
                    end = html.Length;
                stack[^1].contents.Add(new(html[index..end], null));
                index = end;
                continue;
            }
            if (html.AsSpan(index).StartsWith("<!--"))
            {
                var end = html.IndexOf("-->", index, StringComparison.Ordinal);
                if (end < 0)
                    throw new SourceException("source_incomplete", "Truncated comment.");
                index = end + 3;
                continue;
            }
            var close = index + 1;
            char quote = '\0';
            for (; close < html.Length; close++)
            {
                var c = html[close];
                if (quote != '\0')
                {
                    if (c == quote)
                        quote = '\0';
                }
                else if (c is '\'' or '"')
                    quote = c;
                else if (c == '>')
                    break;
            }
            if (close == html.Length)
                throw new SourceException("source_incomplete", "Truncated HTML tag.");
            var token = html[(index + 1)..close];
            index = close + 1;
            var m = Regex.Match(
                token,
                @"^\s*(/?)\s*([a-zA-Z][a-zA-Z0-9-]*)",
                RegexOptions.None,
                TimeSpan.FromSeconds(1)
            );
            if (!m.Success)
                continue;
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (m.Groups[1].Value == "/")
            {
                var at = stack.FindLastIndex(x => x.Tag == tag);
                if (at > 0)
                    stack.RemoveRange(at, stack.Count - at);
                continue;
            }
            if (tag is "script" or "style")
            {
                var end = html.IndexOf("</" + tag, index, StringComparison.OrdinalIgnoreCase);
                if (end < 0)
                    throw new SourceException("source_incomplete", "Unclosed script or style.");
                index = end;
                continue;
            }
            var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (
                Match a in Regex.Matches(
                    token[m.Length..],
                    "([a-zA-Z_:][-a-zA-Z0-9_:.]*)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s/>]+))",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(1)
                )
            )
                attrs[a.Groups[1].Value] = WebUtility.HtmlDecode(
                    a.Groups[2].Success ? a.Groups[2].Value
                    : a.Groups[3].Success ? a.Groups[3].Value
                    : a.Groups[4].Value
                );
            var node = new HtmlNode(tag, attrs);
            if (tag == "br") node.contents.Add(new(" ", null));
            stack[^1].children.Add(node);
            stack[^1].contents.Add(new(null, node));
            if (
                !token.EndsWith('/')
                && tag
                    is not (
                        "area"
                        or "base"
                        or "br"
                        or "col"
                        or "embed"
                        or "hr"
                        or "img"
                        or "input"
                        or "link"
                        or "meta"
                        or "param"
                        or "source"
                        or "track"
                        or "wbr"
                    )
            )
                stack.Add(node);
            if (stack.Count > 128)
                throw new SourceException("source_limit", "HTML nesting limit exceeded.");
        }
        if (!html.TrimEnd().EndsWith("</html>", StringComparison.OrdinalIgnoreCase))
            throw new SourceException("source_incomplete", "Missing document terminator.");
        return root;
    }
}
