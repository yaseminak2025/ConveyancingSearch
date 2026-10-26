using ConveyancingSearch.Application.Locations;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;

namespace ConveyancingSearch.Service.Solicitors;

public sealed class SolicitorsSource(HttpClient client, IOptions<SourceOptions> options)
    : IListingSource,
        IDisposable
{
    private readonly SourceOptions settings = options.Value;
    private readonly SemaphoreSlim downloads = new(options.Value.ParallelDownloads);

    public async Task<CompleteCollection> FetchAsync(SupportedLocation location, CancellationToken cancellationToken)
    {
        try { return await FetchCoreAsync(location, cancellationToken); }
        catch (HttpRequestException ex) { throw new SourceException("source_network", "The source website could not be reached.", ex); }
        catch (IOException ex) { throw new SourceException("source_network", "The source response could not be read.", ex); }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        { throw new SourceException("source_timeout", "The source request timed out.", ex); }
    }

    private async Task<CompleteCollection> FetchCoreAsync(
        SupportedLocation location,
        CancellationToken cancellationToken
    )
    {
        var initial = new Uri(new Uri("https://www.solicitors.com"), location.SourcePath);
        var pending = new Queue<Uri>();
        pending.Enqueue(initial);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pages = new SortedDictionary<string, SourcePage>(StringComparer.Ordinal);
        while (pending.Count > 0)
        {
            var batch = new List<Uri>();
            while (pending.TryDequeue(out var next) && batch.Count < settings.ParallelDownloads)
            {
                if (!visited.Add(next.AbsoluteUri))
                    continue;
                if (visited.Count > settings.MaxPages)
                    throw new SourceException(
                        "source_limit",
                        "Page limit reached before complete coverage."
                    );
                batch.Add(next);
                if (batch.Count == settings.ParallelDownloads)
                    break;
            }
            var fetched = await Task.WhenAll(
                batch.Select(uri => FetchPageAsync(uri, cancellationToken))
            );
            foreach (var (uri, html) in fetched)
            {
                if (pages.ContainsKey(uri.AbsoluteUri))
                    continue;
                var expected = Uri.UnescapeDataString(uri.AbsolutePath)
                    .Replace("/conveyancing+", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(".html", "", StringComparison.OrdinalIgnoreCase);
                var page = SourcePage.Parse(html, uri, expected);
                pages.Add(uri.AbsoluteUri, page);
                foreach (var child in page.Children)
                    if (!visited.Contains(child.AbsoluteUri))
                        pending.Enqueue(child);
            }
        }
        var items = SourcePage.Merge(pages.Values.SelectMany(x => x.Listings));
        if (items.Count == 0)
            throw new SourceException("source_incomplete", "No verified results were collected.");
        return new(items);
    }

    private async Task<(Uri, string)> FetchPageAsync(Uri uri, CancellationToken cancellationToken)
    {
        var requestedPath = Uri.UnescapeDataString(uri.AbsolutePath);
        var expectedDirectory = "/" + requestedPath.Replace("/conveyancing+", "", StringComparison.OrdinalIgnoreCase).Replace(".html", "", StringComparison.OrdinalIgnoreCase) + "-conveyancing-towns.html";
        await downloads.WaitAsync(cancellationToken);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
            for (var redirects = 0; redirects < 6; redirects++)
            {
                if (uri.Scheme != "https" || uri.Host != "www.solicitors.com" || !uri.IsDefaultPort)
                    throw new SourceException("source_redirect", "Unexpected source destination.");
                var path = Uri.UnescapeDataString(uri.AbsolutePath);
                if ((!path.Equals(requestedPath, StringComparison.OrdinalIgnoreCase) && !path.Equals(expectedDirectory, StringComparison.OrdinalIgnoreCase)) || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                    throw new SourceException("source_redirect", "Redirect changed the requested location or page shape.");
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token
                );
                if (
                    response.StatusCode
                    is HttpStatusCode.MovedPermanently
                        or HttpStatusCode.Redirect
                        or HttpStatusCode.TemporaryRedirect
                        or HttpStatusCode.PermanentRedirect
                        or HttpStatusCode.SeeOther
                )
                {
                    uri = response.Headers.Location is { } redirect
                        ? new Uri(uri, redirect)
                        : throw new SourceException(
                            "source_redirect",
                            "Missing redirect destination."
                        );
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new SourceException(
                        "source_http",
                        $"Source returned HTTP {(int)response.StatusCode} at {uri.AbsolutePath}."
                    );
                if (response.Content.Headers.ContentType?.MediaType is not "text/html")
                    throw new SourceException("source_incomplete", "Source did not return HTML.");
                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                using var body = new MemoryStream();
                var buffer = new byte[16384];
                int read;
                while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
                {
                    if (body.Length + read > 4_000_000)
                        throw new SourceException("source_limit", "Response exceeds size limit.");
                    body.Write(buffer, 0, read);
                }
                return (uri, Encoding.UTF8.GetString(body.ToArray()));
            }
            throw new SourceException("source_redirect", "Redirect limit exceeded.");
        }
        finally
        {
            downloads.Release();
        }
    }

    public void Dispose()
    {
        downloads.Dispose();
        client.Dispose();
    }
}
