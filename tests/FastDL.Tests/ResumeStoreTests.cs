using FastDL;
using Xunit;

namespace FastDL.Tests;

public sealed class ResumeStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdlresume_" + Guid.NewGuid().ToString("N"));

    public ResumeStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private static ProbeResult Probe(long size, string? etag = null, string? lastModified = null)
        => new(new Uri("https://h/f"), size, SupportsRanges: true, "f", etag, lastModified);

    [Fact]
    public void Roundtrip_preserves_completed_chunks()
    {
        string path = Path_("a.bin");
        var store = new ResumeStore(path);
        var meta = store.CreateFresh(Probe(1000), 1000, 256);
        meta.Completed.Add(0);
        meta.Completed.Add(2);
        store.Flush(meta);

        var loaded = store.TryLoad(Probe(1000), 256);

        Assert.NotNull(loaded);
        Assert.Equal(new HashSet<int> { 0, 2 }, loaded!.Completed);
    }

    [Fact]
    public void TryLoad_returns_null_when_size_differs()
    {
        string path = Path_("b.bin");
        var store = new ResumeStore(path);
        store.Flush(store.CreateFresh(Probe(1000), 1000, 256));

        Assert.Null(store.TryLoad(Probe(2000), 256));
    }

    [Fact]
    public void TryLoad_returns_null_when_chunk_size_differs()
    {
        string path = Path_("c.bin");
        var store = new ResumeStore(path);
        store.Flush(store.CreateFresh(Probe(1000), 1000, 256));

        Assert.Null(store.TryLoad(Probe(1000), 512));
    }

    [Fact]
    public void TryLoad_returns_null_when_etag_differs()
    {
        string path = Path_("d.bin");
        var store = new ResumeStore(path);
        store.Flush(store.CreateFresh(Probe(1000, etag: "\"v1\""), 1000, 256));

        Assert.Null(store.TryLoad(Probe(1000, etag: "\"v2\""), 256));
    }

    [Fact]
    public void Delete_removes_the_sidecar()
    {
        string path = Path_("e.bin");
        var store = new ResumeStore(path);
        store.Flush(store.CreateFresh(Probe(1000), 1000, 256));
        Assert.True(File.Exists(path + ".fdlmeta"));

        store.Delete();

        Assert.False(File.Exists(path + ".fdlmeta"));
    }

    [Fact]
    public void Corrupt_sidecar_loads_as_null()
    {
        string path = Path_("f.bin");
        File.WriteAllText(path + ".fdlmeta", "{ this is not valid json");

        Assert.Null(new ResumeStore(path).TryLoad(Probe(1000), 256));
    }
}
