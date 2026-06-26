using FastDL;
using Xunit;

namespace FastDL.Tests;

public sealed class ZipServiceTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fdlzip_" + Guid.NewGuid().ToString("N"));
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

    [Fact]
    public void Pack_then_extract_roundtrips()
    {
        string a = Path.Combine(_dir, "a.txt");
        string b = Path.Combine(_dir, "b.txt");
        File.WriteAllText(a, "alpha");
        File.WriteAllText(b, "beta");
        string zip = Path.Combine(_dir, "bundle.zip");

        ZipService.PackAll(new[] { a, b }, zip, _dir, _progress);
        Assert.True(File.Exists(zip));

        int extracted = ZipService.ExtractZips(new[] { zip }, _progress);

        Assert.Equal(1, extracted);
        string outDir = Path.Combine(_dir, "bundle");
        Assert.Equal("alpha", File.ReadAllText(Path.Combine(outDir, "a.txt")));
        Assert.Equal("beta", File.ReadAllText(Path.Combine(outDir, "b.txt")));
    }

    [Fact]
    public void Extract_detects_zip_by_magic_bytes_without_extension()
    {
        string source = Path.Combine(_dir, "x.txt");
        File.WriteAllText(source, "payload");
        string zip = Path.Combine(_dir, "real.zip");
        ZipService.PackAll(new[] { source }, zip, _dir, _progress);

        string noExtension = Path.Combine(_dir, "archive.bin");
        File.Move(zip, noExtension);

        int extracted = ZipService.ExtractZips(new[] { noExtension }, _progress);

        Assert.Equal(1, extracted);
        Assert.True(File.Exists(Path.Combine(_dir, "archive.bin_extracted", "x.txt")));
    }

    [Fact]
    public void Extract_ignores_non_zip_files()
    {
        string text = Path.Combine(_dir, "plain.txt");
        File.WriteAllText(text, "definitely not a zip archive");

        Assert.Equal(0, ZipService.ExtractZips(new[] { text }, _progress));
    }
}
