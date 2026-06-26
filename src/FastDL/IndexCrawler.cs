using System.Text.RegularExpressions;

namespace FastDL;

/// <summary>
/// Recursively crawls an HTTP directory index (Apache/nginx autoindex style),
/// collecting file links and mirroring the remote tree into relative local paths.
/// </summary>
public sealed partial class IndexCrawler
{
    private readonly HttpClient _http;
    private readonly DownloadOptions _options;

    public IndexCrawler(HttpClient http, DownloadOptions options)
    {
        _http = http;
        _options = options;
    }

    [GeneratedRegex("""href\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex HrefPattern();

    public async Task<IReadOnlyList<CrawlItem>> CrawlAsync(Uri baseUrl, CancellationToken ct)
    {
        Uri root = EnsureTrailingSlash(baseUrl);
        var files = new List<CrawlItem>();
        var seenDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await CrawlDirectoryAsync(root, root, _options.Depth, files, seenDirs, seenFiles, ct).ConfigureAwait(false);
        return files;
    }

    private async Task CrawlDirectoryAsync(
        Uri current, Uri root, int depthRemaining,
        List<CrawlItem> files, HashSet<string> seenDirs, HashSet<string> seenFiles, CancellationToken ct)
    {
        if (!seenDirs.Add(current.AbsoluteUri)) return;

        string html;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            HttpClientProvider.ApplyCustomHeaders(request, _options);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                                            .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            html = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return; // unreadable directory -> skip
        }

        foreach (Match match in HrefPattern().Matches(html))
        {
            string href = match.Groups[1].Value.Trim();
            if (ShouldSkipHref(href)) continue;

            if (!Uri.TryCreate(current, href, out Uri? resolved)) continue;
            resolved = StripQueryAndFragment(resolved);

            // Stay strictly within the crawl root.
            if (!resolved.AbsoluteUri.StartsWith(root.AbsoluteUri, StringComparison.OrdinalIgnoreCase)) continue;
            if (resolved.AbsoluteUri.Equals(current.AbsoluteUri, StringComparison.OrdinalIgnoreCase)) continue;

            bool isDirectory = resolved.AbsoluteUri.EndsWith('/');
            if (isDirectory)
            {
                if (depthRemaining > 0)
                    await CrawlDirectoryAsync(resolved, root, depthRemaining - 1, files, seenDirs, seenFiles, ct)
                        .ConfigureAwait(false);
                continue;
            }

            if (!MatchesExtensionFilter(resolved)) continue;
            if (!seenFiles.Add(resolved.AbsoluteUri)) continue;

            string relative = ToRelativePath(root, resolved);
            files.Add(new CrawlItem(resolved, relative));
        }
    }

    private bool MatchesExtensionFilter(Uri url)
    {
        if (_options.Extensions is null || _options.Extensions.Length == 0) return true;
        string ext = Path.GetExtension(url.AbsolutePath).ToLowerInvariant();
        return _options.Extensions.Contains(ext);
    }

    private static bool ShouldSkipHref(string href)
        => href.Length == 0
        || href.StartsWith('?')         // Apache column-sort links (?C=N;O=D)
        || href.StartsWith('#')
        || href.StartsWith("../")
        || href.Equals("/", StringComparison.Ordinal)
        || href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
        || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase);

    private static Uri StripQueryAndFragment(Uri url)
        => new UriBuilder(url) { Query = string.Empty, Fragment = string.Empty }.Uri;

    private static Uri EnsureTrailingSlash(Uri url)
        => url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");

    private static string ToRelativePath(Uri root, Uri file)
    {
        string relative = Uri.UnescapeDataString(file.AbsoluteUri[root.AbsoluteUri.Length..]);
        string[] segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < segments.Length; i++)
            foreach (char invalid in Path.GetInvalidFileNameChars())
                segments[i] = segments[i].Replace(invalid, '_');
        return Path.Combine(segments);
    }
}
