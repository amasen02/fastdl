namespace FastDL;

/// <summary>Runs many <see cref="DownloadSpec"/> concurrently under a global file-parallelism cap.</summary>
public sealed class BatchDownloader
{
    private readonly SegmentedDownloader _downloader;
    private readonly DownloadOptions _options;
    private readonly ProgressReporter _progress;

    public BatchDownloader(SegmentedDownloader downloader, DownloadOptions options, ProgressReporter progress)
    {
        _downloader = downloader;
        _options = options;
        _progress = progress;
    }

    public async Task<IReadOnlyList<DownloadResult>> RunAsync(IReadOnlyList<DownloadSpec> specs, CancellationToken ct)
    {
        _progress.SetFileCounts(0, specs.Count);
        var results = new DownloadResult[specs.Count];
        using var gate = new SemaphoreSlim(_options.Parallel);
        long completed = 0;

        var tasks = specs.Select(async (spec, index) =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                DownloadResult result = await _downloader.DownloadAsync(spec.Sources, spec.OutputPath, ct)
                                                         .ConfigureAwait(false);
                results[index] = result;

                long done = Interlocked.Increment(ref completed);
                _progress.SetFileCounts(done, specs.Count);
                _progress.Log(result.Success
                    ? $"  ✓ {Path.GetFileName(result.OutputPath)}  ({Format.Bytes(result.Bytes)}{(result.Resumed ? ", resumed" : "")})"
                    : $"  ✗ {Path.GetFileName(spec.OutputPath)}  -> {result.Error}");
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results;
    }
}
