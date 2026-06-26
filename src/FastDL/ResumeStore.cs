using System.Text.Json;
using System.Text.Json.Serialization;

namespace FastDL;

/// <summary>
/// Source-generated JSON context for <see cref="ResumeStore.Meta"/>. Reflection-based
/// serialization is disabled in single-file/AOT publishes, so the resume sidecar must use
/// source generation to be written at all.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(ResumeStore.Meta))]
internal sealed partial class ResumeJsonContext : JsonSerializerContext;

/// <summary>Sidecar persistence of which chunks of a file are already on disk, enabling resume.</summary>
public sealed class ResumeStore
{
    private const string MetaSuffix = ".fdlmeta";

    private readonly string _metaPath;
    private readonly object _gate = new();
    private DateTime _lastFlush = DateTime.MinValue;

    public ResumeStore(string outputPath) => _metaPath = outputPath + MetaSuffix;

    public sealed class Meta
    {
        public long Size { get; set; }
        public long ChunkSize { get; set; }
        public string? ETag { get; set; }
        public string? LastModified { get; set; }
        public HashSet<int> Completed { get; set; } = new();
    }

    /// <summary>Loads existing metadata if it is compatible with the current remote resource.</summary>
    public Meta? TryLoad(ProbeResult probe, long chunkSize)
    {
        if (!File.Exists(_metaPath)) return null;
        try
        {
            var meta = JsonSerializer.Deserialize(File.ReadAllText(_metaPath), ResumeJsonContext.Default.Meta);
            if (meta is null) return null;

            bool sameSize = probe.Size is null || meta.Size == probe.Size;
            bool sameChunk = meta.ChunkSize == chunkSize;
            bool sameIdentity =
                (probe.ETag is null || meta.ETag is null || meta.ETag == probe.ETag) &&
                (probe.LastModified is null || meta.LastModified is null || meta.LastModified == probe.LastModified);

            return sameSize && sameChunk && sameIdentity ? meta : null;
        }
        catch
        {
            return null; // corrupt meta -> start fresh
        }
    }

    public Meta CreateFresh(ProbeResult probe, long size, long chunkSize) => new()
    {
        Size = size,
        ChunkSize = chunkSize,
        ETag = probe.ETag,
        LastModified = probe.LastModified,
        Completed = new HashSet<int>(),
    };

    /// <summary>Records a completed chunk and flushes to disk at most every second.</summary>
    public void MarkCompleted(Meta meta, int chunkIndex)
    {
        bool flush;
        lock (_gate)
        {
            meta.Completed.Add(chunkIndex);
            flush = (DateTime.UtcNow - _lastFlush).TotalSeconds >= 1;
            if (flush) _lastFlush = DateTime.UtcNow;
        }
        if (flush) Flush(meta);
    }

    public void Flush(Meta meta)
    {
        lock (_gate)
        {
            try
            {
                // Serialize a snapshot, then atomically replace, so a reader never sees a half-written file.
                var snapshot = new Meta
                {
                    Size = meta.Size,
                    ChunkSize = meta.ChunkSize,
                    ETag = meta.ETag,
                    LastModified = meta.LastModified,
                    Completed = new HashSet<int>(meta.Completed),
                };
                string json = JsonSerializer.Serialize(snapshot, ResumeJsonContext.Default.Meta);
                string temp = _metaPath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, _metaPath, overwrite: true);
            }
            catch { /* best-effort; resume is an optimization, not a guarantee */ }
        }
    }

    public void Delete()
    {
        try { if (File.Exists(_metaPath)) File.Delete(_metaPath); }
        catch { /* ignore */ }
    }
}
