using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace FastDL.Tests.Support;

/// <summary>
/// Serves a fixed in-memory payload, honouring HTTP Range requests (206 + Content-Range) when
/// ranges are enabled, or returning the full body (200) when they are not. Lets the segmented
/// engine be tested deterministically with no network.
/// </summary>
internal sealed class RangeHttpHandler : HttpMessageHandler
{
    private readonly byte[] _content;
    private readonly bool _supportsRanges;
    private readonly string? _contentDisposition;
    private int _requestCount;
    private int _segmentRequests;

    public RangeHttpHandler(byte[] content, bool supportsRanges = true, string? contentDisposition = null)
    {
        _content = content;
        _supportsRanges = supportsRanges;
        _contentDisposition = contentDisposition;
    }

    public int RequestCount => Volatile.Read(ref _requestCount);

    /// <summary>Count of real data-range requests (excludes the 0-0 probe).</summary>
    public int SegmentRequests => Volatile.Read(ref _segmentRequests);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requestCount);
        long total = _content.Length;
        RangeItemHeaderValue? range = _supportsRanges ? request.Headers.Range?.Ranges.FirstOrDefault() : null;

        if (range is not null)
        {
            long from = range.From ?? 0;
            long to = Math.Min(range.To ?? total - 1, total - 1);
            int length = (int)(to - from + 1);
            if (!(from == 0 && to == 0)) Interlocked.Increment(ref _segmentRequests);

            var slice = new byte[length];
            Array.Copy(_content, from, slice, 0, length);
            var partial = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(slice) };
            partial.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, total);
            partial.Content.Headers.ContentLength = length;
            AddContentDisposition(partial);
            return Task.FromResult(partial);
        }

        var full = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_content) };
        full.Content.Headers.ContentLength = total;
        AddContentDisposition(full);
        return Task.FromResult(full);
    }

    /// <summary>Added unvalidated so hostile file names (traversal attempts) reach the parser intact.</summary>
    private void AddContentDisposition(HttpResponseMessage response)
    {
        if (_contentDisposition is not null)
            response.Content.Headers.TryAddWithoutValidation("Content-Disposition", _contentDisposition);
    }
}

/// <summary>Always responds with a fixed status code — used to assert graceful failure handling.</summary>
internal sealed class StatusHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;

    public StatusHttpHandler(HttpStatusCode status) => _status = status;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_status) { Content = new ByteArrayContent(Array.Empty<byte>()) });
}

/// <summary>Maps absolute request URLs to canned HTML bodies for directory-index crawl tests; 404 otherwise.</summary>
internal sealed class HtmlMapHttpHandler : HttpMessageHandler
{
    private readonly IReadOnlyDictionary<string, string> _pages;

    public HtmlMapHttpHandler(IReadOnlyDictionary<string, string> pages) => _pages = pages;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.AbsoluteUri;
        return _pages.TryGetValue(url, out string? html)
            ? Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") })
            : Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new ByteArrayContent(Array.Empty<byte>()) });
    }
}
