using System.Net;
using System.Security.Cryptography;
using FastDL;
using FastDL.Tests.Support;
using Xunit;

namespace FastDL.Tests;

public sealed class SegmentedDownloaderTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdldl_" + Guid.NewGuid().ToString("N"));
    private ProgressReporter _progress = null!;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_dir);
        _progress = new ProgressReporter(quiet: true);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _progress.DisposeAsync();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort cleanup */ }
    }

    // Deterministic, non-trivial payload so a wrong byte order would change the hash.
    private static byte[] DeterministicBytes(int count)
    {
        var bytes = new byte[count];
        for (int i = 0; i < count; i++) bytes[i] = (byte)((i * 31 + 7) & 0xFF);
        return bytes;
    }

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static DownloadOptions Options(long chunk = 64 * 1024, int connections = 8, int retries = 0)
        => new() { ChunkSize = chunk, Connections = connections, Parallel = 1, Retries = retries, NoResume = true, Quiet = true };

    [Fact]
    public async Task Probe_reports_size_and_range_support()
    {
        byte[] content = DeterministicBytes(10_000);
        using var http = new HttpClient(new RangeHttpHandler(content, supportsRanges: true));
        var downloader = new SegmentedDownloader(http, Options(), _progress);

        ProbeResult probe = await downloader.ProbeAsync(new Uri("https://h/f.bin"), CancellationToken.None);

        Assert.True(probe.SupportsRanges);
        Assert.Equal(10_000, probe.Size);
    }

    [Fact]
    public async Task Segmented_download_is_byte_identical()
    {
        byte[] content = DeterministicBytes(256 * 1024); // 4 chunks @ 64K
        using var http = new HttpClient(new RangeHttpHandler(content, supportsRanges: true));
        var downloader = new SegmentedDownloader(http, Options(), _progress);
        string path = Path.Combine(_dir, "seg.bin");

        DownloadResult result = await downloader.DownloadAsync(new[] { new Uri("https://h/seg.bin") }, path, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Sha256Hex(content), Sha256Hex(File.ReadAllBytes(path)));
        Assert.False(File.Exists(path + ".fdlmeta")); // resume sidecar removed on success
    }

    [Fact]
    public async Task Single_stream_fallback_is_byte_identical_when_ranges_unsupported()
    {
        byte[] content = DeterministicBytes(200 * 1024);
        using var http = new HttpClient(new RangeHttpHandler(content, supportsRanges: false));
        var downloader = new SegmentedDownloader(http, Options(), _progress);
        string path = Path.Combine(_dir, "single.bin");

        DownloadResult result = await downloader.DownloadAsync(new[] { new Uri("https://h/single.bin") }, path, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Sha256Hex(content), Sha256Hex(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Mirror_sources_produce_correct_file()
    {
        byte[] content = DeterministicBytes(256 * 1024);
        using var http = new HttpClient(new RangeHttpHandler(content, supportsRanges: true));
        var downloader = new SegmentedDownloader(http, Options(), _progress);
        string path = Path.Combine(_dir, "mirror.bin");
        var sources = new[] { new Uri("https://a/f.bin"), new Uri("https://b/f.bin") };

        DownloadResult result = await downloader.DownloadAsync(sources, path, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(Sha256Hex(content), Sha256Hex(File.ReadAllBytes(path)));
    }

    [Fact]
    public async Task Failing_source_returns_failure_rather_than_throwing()
    {
        using var http = new HttpClient(new StatusHttpHandler(HttpStatusCode.InternalServerError));
        var downloader = new SegmentedDownloader(http, Options(retries: 0), _progress);
        string path = Path.Combine(_dir, "fail.bin");

        DownloadResult result = await downloader.DownloadAsync(new[] { new Uri("https://h/fail.bin") }, path, CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Resume_skips_already_completed_chunks()
    {
        byte[] content = DeterministicBytes(256 * 1024); // 4 chunks @ 64K
        const long chunk = 64 * 1024;
        var handler = new RangeHttpHandler(content, supportsRanges: true);
        using var http = new HttpClient(handler);
        DownloadOptions options = Options(chunk: chunk);
        options.NoResume = false;
        var downloader = new SegmentedDownloader(http, options, _progress);
        string path = Path.Combine(_dir, "resume.bin");

        // Pre-seed: the full correct file on disk plus a sidecar marking chunk 0 already done.
        await File.WriteAllBytesAsync(path, content);
        ProbeResult probe = await downloader.ProbeAsync(new Uri("https://h/resume.bin"), CancellationToken.None);
        var store = new ResumeStore(path);
        ResumeStore.Meta meta = store.CreateFresh(probe, content.Length, chunk);
        meta.Completed.Add(0);
        store.Flush(meta);

        DownloadResult result = await downloader.DownloadAsync(new[] { new Uri("https://h/resume.bin") }, path, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.True(result.Resumed);
        Assert.Equal(Sha256Hex(content), Sha256Hex(File.ReadAllBytes(path)));
        Assert.Equal(3, handler.SegmentRequests); // only the 3 missing chunks were fetched
    }

    // The server names the file; it must not get to choose the directory too.
    [Theory]
    [InlineData("attachment; filename=\"../../evil.bin\"")]
    [InlineData("attachment; filename=\"..\"")]
    [InlineData("attachment; filename*=UTF-8''%2e%2e%2f%2e%2e%2fevil.bin")]
    [InlineData("attachment; filename*=UTF-8''%2e%2e")]
    public async Task Content_disposition_cannot_place_the_file_outside_the_output_directory(string disposition)
    {
        byte[] content = DeterministicBytes(4096);
        using var http = new HttpClient(new RangeHttpHandler(content, supportsRanges: false, contentDisposition: disposition));
        var downloader = new SegmentedDownloader(http, Options(), _progress);
        string outputDirectory = _dir + Path.DirectorySeparatorChar;

        DownloadResult result = await downloader.DownloadAsync(
            new[] { new Uri("https://h/f.bin") }, outputDirectory, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.True(PathGuard.IsInside(_dir, result.OutputPath), $"wrote outside the output directory: {result.OutputPath}");
        Assert.Equal(Sha256Hex(content), Sha256Hex(File.ReadAllBytes(result.OutputPath)));
    }
}
