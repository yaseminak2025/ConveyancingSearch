using System.Net;
using ConveyancingSearch.Application.Contracts;
using ConveyancingSearch.Domain;
using ConveyancingSearch.Service.Solicitors;
using Microsoft.Extensions.Options;
using Xunit;
namespace ConveyancingSearch.Tests;

public class SourceNavigationTests
{
    [Fact]
    public async Task Redirect_to_a_different_city_cannot_satisfy_requested_city()
    {
        using var source = Source(uri => uri.AbsolutePath.Contains("leeds") ? Redirect("/conveyancing+bristol.html") : Html(Capture("bristol")));
        await Assert.ThrowsAsync<SourceException>(() => source.FetchAsync(TestLocations.Get("Leeds"), default));
    }
    [Fact]
    public async Task Directory_backlink_and_repeated_encoded_links_are_downloaded_once()
    {
        var calls = 0;
        using var source = Source(uri =>
        {
            if (uri.AbsolutePath == "/conveyancing+london.html") return Redirect("/london-conveyancing-towns.html");
            if (uri.AbsolutePath == "/london-conveyancing-towns.html") return Html(Directory("/conveyancing+london.html", "/conveyancing+acton.html", "/conveyancing%2Bacton.html"));
            calls++; return Html(Capture("acton"));
        });
        var collection = await source.FetchAsync(TestLocations.Get("London"), default); Assert.Equal(4, collection.Listings.Count); Assert.Equal(1, calls);
    }
    [Theory]
    [InlineData("/unknown-route.html")]
    [InlineData("https://other.example/conveyancing+acton.html")]
    public async Task Unrecognised_required_directory_link_fails_entire_collection(string unknown)
    {
        using var source = Source(uri => uri.AbsolutePath == "/conveyancing+london.html" ? Redirect("/london-conveyancing-towns.html") : Html(Directory("/conveyancing+acton.html", unknown)));
        await Assert.ThrowsAsync<SourceException>(() => source.FetchAsync(TestLocations.Get("London"), default));
    }
    [Fact]
    public async Task Failed_child_and_page_limit_do_not_publish_partial_collection()
    {
        HttpResponseMessage Handle(Uri uri) => uri.AbsolutePath switch
        {
            "/conveyancing+london.html" => Redirect("/london-conveyancing-towns.html"),
            "/london-conveyancing-towns.html" => Html(Directory("/conveyancing+acton.html", "/conveyancing+leeds.html")),
            "/conveyancing+acton.html" => Html(Capture("acton")),
            _ => new(HttpStatusCode.ServiceUnavailable)
        };
        using var failing = Source(Handle); await Assert.ThrowsAsync<SourceException>(() => failing.FetchAsync(TestLocations.Get("London"), default));
        using var limited = Source(Handle, new SourceOptions { MaxPages = 1 }); await Assert.ThrowsAsync<SourceException>(() => limited.FetchAsync(TestLocations.Get("London"), default));
    }
    [Fact]
    public void Nested_address_preserves_order_and_entities_decode_once()
    {
        // Synthetic, deliberately minimal complete HTML for the documented source shape.
        var html = "<html><span class='h4 text-center'>1 Conveyancing Solicitors found in Leeds</span><div class='result-section'><div class='result-item'><span class='h2'>A &amp;lt; Co</span><a class='link-map' href='/a.html'><address>1 <span>High</span> Street &amp;lt;</address></a></div></div></html>";
        var item = Assert.Single(SourcePage.Parse(html, new Uri("https://www.solicitors.com/conveyancing+leeds.html"), "Leeds").Listings);
        Assert.Equal("1 High Street &lt;", item.Address); Assert.Equal("A &lt; Co", item.Name);
    }
    [Fact]
    public async Task Network_failure_is_classified_without_losing_inner_exception()
    {
        var network = new HttpRequestException("Connection failed");
        using var source = Source(_ => throw network);
        var error = await Assert.ThrowsAsync<SourceException>(() => source.FetchAsync(TestLocations.Get("Leeds"), default));
        Assert.Equal("source_network", error.Code);
        Assert.Same(network, error.InnerException);
    }

    [Fact]
    public async Task Request_timeout_is_distinct_from_caller_cancellation()
    {
        using var source = Source(_ => throw new TaskCanceledException("HTTP timeout"));
        var error = await Assert.ThrowsAsync<SourceException>(() => source.FetchAsync(TestLocations.Get("Leeds"), default));
        Assert.Equal("source_timeout", error.Code);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.FetchAsync(TestLocations.Get("Leeds"), cancelled.Token));
    }

    private static string Directory(params string[] links) => "<html><ul class='alphabet-list'>" + string.Join("", links.Select(x => $"<li><a href='{x}'>Area</a></li>")) + "</ul></html>";
    private static string Capture(string city) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", city + ".html"));
    private static HttpResponseMessage Html(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "text/html") };
    private static HttpResponseMessage Redirect(string target) { var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri(target, UriKind.RelativeOrAbsolute); return response; }
    private static SolicitorsSource Source(Func<Uri, HttpResponseMessage> handler, SourceOptions? options = null) => new(new HttpClient(new Handler(handler)), Options.Create(options ?? new()));
    private sealed class Handler(Func<Uri, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handler(request.RequestUri!));
    }
}
