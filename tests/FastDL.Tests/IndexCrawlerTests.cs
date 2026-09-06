using FastDL;
using FastDL.Tests.Support;
using Xunit;

namespace FastDL.Tests;

public class IndexCrawlerTests
{
    private static string Page(params string[] hrefs)
        => "<html><body>" + string.Concat(hrefs.Select(h => $"<a href=\"{h}\">{h}</a>")) + "</body></html>";

    private static string[] RelativePaths(IEnumerable<CrawlItem> items)
        => items.Select(i => i.RelativePath.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();

    [Fact]
    public async Task Crawls_files_and_recurses_into_subdirectories()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://h/dir/"] = Page("a.iso", "b.zip", "sub/", "?C=N;O=D", "../"),
            ["https://h/dir/sub/"] = Page("c.iso"),
        };
        using var http = new HttpClient(new HtmlMapHttpHandler(pages));
        var crawler = new IndexCrawler(http, new DownloadOptions { Depth = 5 });

        var items = await crawler.CrawlAsync(new Uri("https://h/dir/"), CancellationToken.None);

        Assert.Equal(new[] { "a.iso", "b.zip", "sub/c.iso" }, RelativePaths(items));
    }

    [Fact]
    public async Task Extension_filter_excludes_other_files()
    {
        var pages = new Dictionary<string, string> { ["https://h/d/"] = Page("a.iso", "b.zip", "c.txt") };
        using var http = new HttpClient(new HtmlMapHttpHandler(pages));
        var crawler = new IndexCrawler(http, new DownloadOptions { Extensions = new[] { ".iso" } });

        var items = await crawler.CrawlAsync(new Uri("https://h/d/"), CancellationToken.None);

        Assert.Equal(new[] { "a.iso" }, RelativePaths(items));
    }

    [Fact]
    public async Task Depth_limit_stops_recursion()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://h/d/"] = Page("deep/"),
            ["https://h/d/deep/"] = Page("x.iso"),
        };
        using var http = new HttpClient(new HtmlMapHttpHandler(pages));
        var crawler = new IndexCrawler(http, new DownloadOptions { Depth = 0 });

        var items = await crawler.CrawlAsync(new Uri("https://h/d/"), CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task Stays_within_the_crawl_root()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://h/d/"] = Page("https://other/evil.iso", "/outside.iso", "ok.iso"),
        };
        using var http = new HttpClient(new HtmlMapHttpHandler(pages));
        var crawler = new IndexCrawler(http, new DownloadOptions());

        var items = await crawler.CrawlAsync(new Uri("https://h/d/"), CancellationToken.None);

        Assert.Equal(new[] { "ok.iso" }, RelativePaths(items));
    }

    // Uri never unescapes %2f and never collapses dot segments hidden behind it, so these links
    // sit under the crawl root as URIs while decoding to paths outside the output directory.
    [Theory]
    [InlineData("..%2f..%2fevil.iso")]
    [InlineData("%2e%2e%2f%2e%2e%2fevil.iso")]
    [InlineData("x%2f..%2f..%2fevil.iso")]
    [InlineData("..%2F..%2Fevil.iso")]
    [InlineData("sub%2f..%2f..%2f..%2fevil.iso")]
    public async Task Rejects_percent_encoded_traversal_out_of_the_crawl_root(string href)
    {
        var pages = new Dictionary<string, string> { ["https://h/d/"] = Page(href, "ok.iso") };
        using var http = new HttpClient(new HtmlMapHttpHandler(pages));
        var crawler = new IndexCrawler(http, new DownloadOptions());

        var items = await crawler.CrawlAsync(new Uri("https://h/d/"), CancellationToken.None);

        Assert.Equal(new[] { "ok.iso" }, RelativePaths(items));
    }

    [Fact]
    public async Task Percent_encoded_names_that_do_not_traverse_are_still_crawled()
    {
        var pages = new Dictionary<string, string> { ["https://h/d/"] = Page("a%20b.iso", "sub/c%2Bd.iso") };
        using var http = new HttpClient(new HtmlMapHttpHandler(pages));
        var crawler = new IndexCrawler(http, new DownloadOptions());

        var items = await crawler.CrawlAsync(new Uri("https://h/d/"), CancellationToken.None);

        Assert.Equal(new[] { "a b.iso", "sub/c+d.iso" }, RelativePaths(items));
    }

    // Every crawled path must land under the output root once combined with it.
    [Fact]
    public async Task Crawled_paths_resolve_inside_the_output_root()
    {
        var pages = new Dictionary<string, string>
        {
            ["https://h/d/"] = Page("..%2f..%2fevil.iso", "%2e%2e%2fsibling.iso", "sub/", "ok.iso"),
            ["https://h/d/sub/"] = Page("..%2f..%2f..%2fdeep-evil.iso", "nested.iso"),
        };
        using var http = new HttpClient(new HtmlMapHttpHandler(pages));
        var crawler = new IndexCrawler(http, new DownloadOptions { Depth = 5 });

        var items = await crawler.CrawlAsync(new Uri("https://h/d/"), CancellationToken.None);

        string root = Path.Combine(Path.GetTempPath(), "fdlroot_" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        foreach (var item in items)
            Assert.True(PathGuard.IsInside(root, Path.Combine(root, item.RelativePath)),
                        $"'{item.RelativePath}' escapes the output root");

        Assert.Equal(new[] { "ok.iso", "sub/nested.iso" }, RelativePaths(items));
    }
}
