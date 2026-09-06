namespace FastDL;

/// <summary>Fully parsed command-line configuration for a run.</summary>
public sealed class DownloadOptions
{
    public List<string> Urls { get; } = new();
    public string? Output { get; set; }
    public string? InputFile { get; set; }
    public bool Folder { get; set; }
    public int Depth { get; set; } = 5;
    public string[]? Extensions { get; set; }
    public int Connections { get; set; } = 16;
    public int Parallel { get; set; } = 4;
    public long ChunkSize { get; set; } = 4L * 1024 * 1024;
    public bool Extract { get; set; }
    public string? ZipOutput { get; set; }
    public bool NoResume { get; set; }
    public bool Insecure { get; set; }
    public List<(string Key, string Value)> Headers { get; } = new();
    /// <summary>HTTP Basic ("user:pass") from <c>--user</c>/<c>FDL_PASSWORD</c>: deliberately run-wide.</summary>
    public string? Credentials { get; set; }

    /// <summary>
    /// HTTP Basic credentials parsed from inline userinfo, keyed by the origin they were typed
    /// for (see <see cref="HttpClientProvider.OriginOf"/>). A credential the user scoped to one
    /// host by typing it in that host's URL is only ever sent back to that host.
    /// </summary>
    public Dictionary<string, string> OriginCredentials { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string[]? Mirrors { get; set; }
    public int Retries { get; set; } = 5;
    public bool Quiet { get; set; }
    public bool Verbose { get; set; }
    public bool Help { get; set; }
}
