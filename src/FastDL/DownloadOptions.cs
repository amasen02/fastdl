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
    public string? Credentials { get; set; } // HTTP Basic, "user:pass"
    public string[]? Mirrors { get; set; }
    public int Retries { get; set; } = 5;
    public bool Quiet { get; set; }
    public bool Verbose { get; set; }
    public bool Help { get; set; }
}
