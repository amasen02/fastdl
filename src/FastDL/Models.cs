namespace FastDL;

/// <summary>Result of probing a remote resource before download.</summary>
public sealed record ProbeResult(
    Uri FinalUrl,
    long? Size,
    bool SupportsRanges,
    string FileName,
    string? ETag,
    string? LastModified);

/// <summary>A single logical download: one target file fed by one or more mirror sources.</summary>
public sealed record DownloadSpec(IReadOnlyList<Uri> Sources, string OutputPath)
{
    public Uri Primary => Sources[0];
}

/// <summary>Outcome of a completed (or failed) download.</summary>
public sealed record DownloadResult(
    string OutputPath,
    long Bytes,
    bool Success,
    bool Resumed,
    string? Error = null);

/// <summary>An item discovered while crawling a directory index.</summary>
public sealed record CrawlItem(Uri Url, string RelativePath);
