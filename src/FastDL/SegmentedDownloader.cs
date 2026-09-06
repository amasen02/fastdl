using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using Microsoft.Win32.SafeHandles;

namespace FastDL;

/// <summary>
/// Downloads one logical file using many parallel HTTP byte-range segments pulled from a shared
/// work queue. Fast connections naturally claim more chunks, eliminating the slow-segment long tail.
/// Multiple mirror sources are striped across segments for additional throughput.
/// </summary>
public sealed class SegmentedDownloader
{
    private const int ReadBufferSize = 1 << 20;          // 1 MiB network read buffer
    private static readonly TimeSpan ReadStallTimeout = TimeSpan.FromSeconds(45);

    private readonly HttpClient _http;
    private readonly DownloadOptions _options;
    private readonly ProgressReporter _progress;

    public SegmentedDownloader(HttpClient http, DownloadOptions options, ProgressReporter progress)
    {
        _http = http;
        _options = options;
        _progress = progress;
    }

    public async Task<DownloadResult> DownloadAsync(IReadOnlyList<Uri> sources, string outputPath, CancellationToken ct)
    {
        // Best-effort path for error reporting until the probe resolves the real file name.
        string finalPath = outputPath;
        try
        {
            ProbeResult probe = await ProbeAsync(sources[0], ct).ConfigureAwait(false);
            finalPath = ResolveOutputPath(outputPath, probe.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(finalPath))!);

            bool canSegment = probe.SupportsRanges && probe.Size is > 0 && probe.Size > _options.ChunkSize;
            return canSegment
                ? await SegmentedAsync(sources, probe, finalPath, ct).ConfigureAwait(false)
                : await SingleStreamAsync(sources[0], probe, finalPath, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A failed probe or transfer for one file must not abort the rest of the batch.
            return new DownloadResult(finalPath, _progress.BytesDone, Success: false, Resumed: false, ex.Message);
        }
    }

    // ---- Probe -------------------------------------------------------------

    public async Task<ProbeResult> ProbeAsync(Uri url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Range = new RangeHeaderValue(0, 0); // tiny range probe; reveals range support + size
        HttpClientProvider.ApplyCustomHeaders(request, _options);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                                        .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            string realm = response.Headers.WwwAuthenticate.FirstOrDefault()?.ToString() ?? "";
            throw new HttpRequestException(
                $"{(int)response.StatusCode} {response.ReasonPhrase} — authentication required. " +
                $"Pass credentials with --user user:pass {(realm.Length > 0 ? $"[{realm}]" : "")}".TrimEnd());
        }

        Uri finalUrl = response.RequestMessage?.RequestUri ?? url;

        // Only treat ranges as usable if the server returned EXACTLY the byte we asked for (0-0)
        // with a well-formed Content-Range. Some servers (e.g. buggy PHP file managers) reply 206
        // but ignore the range or emit a malformed header — segmenting those corrupts the file.
        var contentRange = response.Content.Headers.ContentRange;
        bool supportsRanges = response.StatusCode == HttpStatusCode.PartialContent
            && contentRange is { HasRange: true, From: 0, To: 0 };

        // Recover the total size even from a malformed Content-Range, so single-stream can verify completion.
        long? size = contentRange?.Length ?? TryParseContentRangeTotal(response);
        if (size is null && response.StatusCode == HttpStatusCode.OK)
            size = response.Content.Headers.ContentLength;

        string fileName = ResolveFileName(finalUrl, response.Content.Headers.ContentDisposition?.FileNameStar
                                                   ?? response.Content.Headers.ContentDisposition?.FileName);

        return new ProbeResult(
            finalUrl,
            size,
            supportsRanges,
            fileName,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified?.ToString("R"));
    }

    // ---- Segmented (parallel range) path -----------------------------------

    private async Task<DownloadResult> SegmentedAsync(
        IReadOnlyList<Uri> sources, ProbeResult probe, string path, CancellationToken ct)
    {
        long size = probe.Size!.Value;
        long chunkSize = _options.ChunkSize;
        int chunkCount = (int)((size + chunkSize - 1) / chunkSize);

        _progress.AddTotal(size);
        _progress.SetLabel(Path.GetFileName(path));

        var resume = new ResumeStore(path);
        ResumeStore.Meta? existing = _options.NoResume ? null : resume.TryLoad(probe, chunkSize);
        bool resumed = existing is not null && existing.Completed.Count > 0;
        ResumeStore.Meta meta = existing ?? resume.CreateFresh(probe, size, chunkSize);

        if (_options.NoResume) resume.Delete();

        EnsureFileLength(path, size, fresh: existing is null);

        // Account for chunks already on disk from a previous run.
        long alreadyDone = 0;
        var pending = new ConcurrentQueue<int>();
        for (int i = 0; i < chunkCount; i++)
        {
            if (meta.Completed.Contains(i))
                alreadyDone += ChunkLength(i, chunkSize, size);
            else
                pending.Enqueue(i);
        }
        if (alreadyDone > 0) _progress.Add(alreadyDone);

        using SafeFileHandle handle = File.OpenHandle(
            path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, FileOptions.Asynchronous);

        int workerCount = Math.Min(_options.Connections, pending.Count);
        var workers = new Task[workerCount];
        for (int w = 0; w < workerCount; w++)
        {
            int workerId = w;
            workers[w] = Task.Run(() => WorkerAsync(workerId, sources, pending, handle, meta, resume, chunkSize, size, ct), ct);
        }

        await Task.WhenAll(workers).ConfigureAwait(false);

        resume.Flush(meta);
        if (meta.Completed.Count != chunkCount)
            throw new IOException($"incomplete: {meta.Completed.Count}/{chunkCount} segments written");

        resume.Delete();
        return new DownloadResult(path, size, Success: true, resumed);
    }

    private async Task WorkerAsync(
        int workerId, IReadOnlyList<Uri> sources, ConcurrentQueue<int> pending,
        SafeFileHandle handle, ResumeStore.Meta meta, ResumeStore resume,
        long chunkSize, long size, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && pending.TryDequeue(out int index))
        {
            await DownloadChunkAsync(workerId, index, sources, handle, chunkSize, size, ct).ConfigureAwait(false);
            resume.MarkCompleted(meta, index);
        }
    }

    private async Task DownloadChunkAsync(
        int workerId, int index, IReadOnlyList<Uri> sources, SafeFileHandle handle,
        long chunkSize, long size, CancellationToken ct)
    {
        long start = (long)index * chunkSize;
        long end = Math.Min(start + chunkSize, size) - 1;
        long expected = end - start + 1;

        Exception? last = null;
        for (int attempt = 0; attempt <= _options.Retries; attempt++)
        {
            // Stripe across mirrors; on retry, deliberately move to a different source.
            Uri source = sources[(index + workerId + attempt) % sources.Count];
            long writtenThisAttempt = 0;
            _progress.ConnOpened();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source);
                request.Headers.Range = new RangeHeaderValue(start, end);
                HttpClientProvider.ApplyCustomHeaders(request, _options);

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                                                .ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    throw new HttpRequestException($"expected 206, got {(int)response.StatusCode} {response.StatusCode}");

                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
                try
                {
                    long position = start;
                    while (true)
                    {
                        int read = await ReadWithStallTimeoutAsync(stream, buffer, ct).ConfigureAwait(false);
                        if (read == 0) break;
                        await RandomAccess.WriteAsync(handle, buffer.AsMemory(0, read), position, ct).ConfigureAwait(false);
                        position += read;
                        writtenThisAttempt += read;
                        _progress.Add(read);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                if (writtenThisAttempt == expected)
                    return; // chunk complete

                throw new IOException($"short read: {writtenThisAttempt}/{expected} bytes for segment {index}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (writtenThisAttempt > 0) _progress.Add(-writtenThisAttempt); // roll back so totals stay honest
                if (attempt < _options.Retries)
                    await Task.Delay(BackoffDelay(attempt), ct).ConfigureAwait(false);
            }
            finally
            {
                _progress.ConnClosed();
            }
        }

        throw new IOException($"segment {index} failed after {_options.Retries + 1} attempts: {last?.Message}", last);
    }

    private async ValueTask<int> ReadWithStallTimeoutAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReadStallTimeout);
        try
        {
            return await stream.ReadAsync(buffer, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"no data for {ReadStallTimeout.TotalSeconds:0}s");
        }
    }

    // ---- Single-stream fallback (no range support / unknown size) -----------

    private async Task<DownloadResult> SingleStreamAsync(Uri source, ProbeResult probe, string path, CancellationToken ct)
    {
        if (probe.Size is > 0) _progress.AddTotal(probe.Size.Value);
        _progress.SetLabel(Path.GetFileName(path));

        // Bytes of THIS file currently reflected in the shared progress counter.
        // Tracking it lets retries stay byte-accurate whether they resume or restart.
        long fileProgress = 0;
        bool resumed = false;
        Exception? last = null;

        for (int attempt = 0; attempt <= _options.Retries; attempt++)
        {
            bool canResume = !_options.NoResume && probe.SupportsRanges && File.Exists(path);
            long startOffset;
            if (canResume)
            {
                startOffset = new FileInfo(path).Length;
            }
            else
            {
                if (File.Exists(path)) File.Delete(path); // not resumable -> always restart clean
                startOffset = 0;
            }

            // Re-baseline the progress counter to the bytes actually on disk for this attempt.
            _progress.Add(startOffset - fileProgress);
            fileProgress = startOffset;
            resumed = startOffset > 0;

            _progress.ConnOpened();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, source);
                if (startOffset > 0) request.Headers.Range = new RangeHeaderValue(startOffset, null);
                HttpClientProvider.ApplyCustomHeaders(request, _options);

                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                                                .ConfigureAwait(false);

                // Server ignored our resume request -> the body is the whole file; restart from zero.
                if (startOffset > 0 && response.StatusCode != HttpStatusCode.PartialContent)
                {
                    if (File.Exists(path)) File.Delete(path);
                    _progress.Add(-fileProgress);
                    fileProgress = 0;
                    startOffset = 0;
                    resumed = false;
                }
                response.EnsureSuccessStatusCode();

                var fileMode = startOffset > 0 ? FileMode.Append : FileMode.Create;
                await using (var file = new FileStream(path, fileMode, FileAccess.Write, FileShare.Read, ReadBufferSize, useAsync: true))
                await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                {
                    byte[] buffer = ArrayPool<byte>.Shared.Rent(ReadBufferSize);
                    try
                    {
                        int read;
                        while ((read = await ReadWithStallTimeoutAsync(stream, buffer, ct).ConfigureAwait(false)) > 0)
                        {
                            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                            fileProgress += read;
                            _progress.Add(read);
                        }
                        await file.FlushAsync(ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }

                // Reject a truncated stream (e.g. a server that closes early or mishandles resume ranges).
                if (probe.Size is > 0 && fileProgress != probe.Size)
                    throw new IOException($"incomplete stream: {fileProgress}/{probe.Size} bytes");

                // Count bytes from the write loop, not FileInfo.Length, which can be stale before flush.
                return new DownloadResult(path, fileProgress, Success: true, resumed);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < _options.Retries)
                    await Task.Delay(BackoffDelay(attempt), ct).ConfigureAwait(false);
            }
            finally
            {
                _progress.ConnClosed();
            }
        }

        throw new IOException($"download failed after {_options.Retries + 1} attempts: {last?.Message}", last);
    }

    // ---- Helpers -----------------------------------------------------------

    /// <summary>Extracts the total size from a raw (possibly malformed) Content-Range header: the number after the last '/'.</summary>
    private static long? TryParseContentRangeTotal(HttpResponseMessage response)
    {
        if (!response.Content.Headers.TryGetValues("Content-Range", out var values)) return null;
        string raw = values.FirstOrDefault() ?? "";
        int slash = raw.LastIndexOf('/');
        if (slash < 0 || slash + 1 >= raw.Length) return null;
        return long.TryParse(raw[(slash + 1)..].Trim(), out long total) && total > 0 ? total : null;
    }

    private static long ChunkLength(int index, long chunkSize, long size)
        => Math.Min((long)(index + 1) * chunkSize, size) - (long)index * chunkSize;

    private static TimeSpan BackoffDelay(int attempt)
        => TimeSpan.FromMilliseconds(Math.Min(8000, 250 * Math.Pow(2, attempt)));

    private static void EnsureFileLength(string path, long size, bool fresh)
    {
        using var fs = new FileStream(path, fresh ? FileMode.Create : FileMode.OpenOrCreate,
                                      FileAccess.Write, FileShare.ReadWrite);
        if (fs.Length != size) fs.SetLength(size);
    }

    private static string ResolveOutputPath(string outputPath, string fileName)
    {
        bool isDirectory = Directory.Exists(outputPath)
            || outputPath.EndsWith(Path.DirectorySeparatorChar)
            || outputPath.EndsWith(Path.AltDirectorySeparatorChar);
        if (!isDirectory) return outputPath; // user named the exact file; nothing remote to trust

        string combined = Path.Combine(outputPath, fileName);
        if (!PathGuard.IsInside(outputPath, combined))
            throw new IOException($"refusing to write outside '{outputPath}': server-supplied name '{fileName}'");
        return combined;
    }

    private static string ResolveFileName(Uri url, string? contentDisposition)
    {
        if (!string.IsNullOrWhiteSpace(contentDisposition))
        {
            string cleaned = contentDisposition.Trim('"');
            if (cleaned.StartsWith("UTF-8''", StringComparison.OrdinalIgnoreCase))
                cleaned = Uri.UnescapeDataString(cleaned["UTF-8''".Length..]);
            cleaned = Path.GetFileName(cleaned);
            if (!string.IsNullOrWhiteSpace(cleaned)) return Sanitize(cleaned);
        }

        string fromPath = Path.GetFileName(Uri.UnescapeDataString(url.AbsolutePath.TrimEnd('/')));
        return string.IsNullOrWhiteSpace(fromPath) ? "download" : Sanitize(fromPath);
    }

    private static string Sanitize(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        // "." and ".." pass the invalid-character filter untouched but name a directory, not a
        // file: combining either with the output directory walks out of it.
        return PathGuard.IsDotSegment(name) ? "download" : name;
    }
}
