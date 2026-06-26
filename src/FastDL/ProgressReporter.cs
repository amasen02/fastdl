using System.Diagnostics;

namespace FastDL;

/// <summary>
/// Thread-safe, lock-free progress aggregator with a single background render loop.
/// Workers call <see cref="Add"/> / <see cref="ConnOpened"/> / <see cref="ConnClosed"/> from many threads.
/// </summary>
public sealed class ProgressReporter : IAsyncDisposable
{
    private const int RenderIntervalMs = 200;
    private const double SpeedSmoothing = 0.3; // EMA weight for the latest sample

    private long _total;             // total bytes expected (0 = unknown)
    private long _done;              // bytes completed
    private int _activeConnections;  // segments currently transferring
    private long _filesDone;
    private long _filesTotal;

    private readonly bool _quiet;
    private readonly bool _interactive;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _renderLoop;
    private readonly object _writeLock = new();

    private double _emaSpeed;
    private long _lastDone;
    private double _lastElapsedMs;
    private int _lastLineLength;
    private string _label = "";

    public ProgressReporter(bool quiet)
    {
        _quiet = quiet;
        _interactive = !quiet && !Console.IsOutputRedirected;
        _renderLoop = _quiet ? Task.CompletedTask : RenderLoopAsync(_cts.Token);
    }

    public void SetTotal(long bytes) => Interlocked.Exchange(ref _total, bytes);
    public void AddTotal(long bytes) => Interlocked.Add(ref _total, bytes);
    public void Add(long bytes) => Interlocked.Add(ref _done, bytes);
    public void ConnOpened() => Interlocked.Increment(ref _activeConnections);
    public void ConnClosed() => Interlocked.Decrement(ref _activeConnections);
    public void SetFileCounts(long done, long total)
    {
        Interlocked.Exchange(ref _filesDone, done);
        Interlocked.Exchange(ref _filesTotal, total);
    }
    public void SetLabel(string label) => _label = label;

    public long BytesDone => Interlocked.Read(ref _done);
    public TimeSpan Elapsed => _clock.Elapsed;
    public double AverageSpeed => _clock.Elapsed.TotalSeconds > 0 ? _done / _clock.Elapsed.TotalSeconds : 0;

    /// <summary>Prints a persistent log line above the live progress bar.</summary>
    public void Log(string message)
    {
        if (_quiet) return;
        lock (_writeLock)
        {
            if (_interactive && _lastLineLength > 0)
                Console.Write('\r' + new string(' ', _lastLineLength) + '\r');
            Console.WriteLine(message);
            _lastLineLength = 0;
        }
    }

    private async Task RenderLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Render();
                await Task.Delay(RenderIntervalMs, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private void Render()
    {
        if (!_interactive) return;

        long done = Interlocked.Read(ref _done);
        long total = Interlocked.Read(ref _total);
        int conns = Volatile.Read(ref _activeConnections);
        long filesDone = Interlocked.Read(ref _filesDone);
        long filesTotal = Interlocked.Read(ref _filesTotal);

        double elapsedMs = _clock.Elapsed.TotalMilliseconds;
        double dt = elapsedMs - _lastElapsedMs;
        if (dt > 0)
        {
            double instant = (done - _lastDone) / (dt / 1000.0);
            _emaSpeed = _emaSpeed <= 0 ? instant : _emaSpeed + SpeedSmoothing * (instant - _emaSpeed);
            _lastDone = done;
            _lastElapsedMs = elapsedMs;
        }

        string pct = total > 0 ? $"{100.0 * done / total,5:0.0}%" : "  --%";
        string sizePart = total > 0 ? $"{Format.Bytes(done)}/{Format.Bytes(total)}" : Format.Bytes(done);
        string eta = total > 0 && _emaSpeed > 1
            ? Format.Duration(TimeSpan.FromSeconds((total - done) / _emaSpeed))
            : "--";
        string files = filesTotal > 1 ? $" | files {filesDone}/{filesTotal}" : "";
        string label = string.IsNullOrEmpty(_label) ? "" : $" | {Truncate(_label, 28)}";

        string line = $"▶ {pct} | {sizePart} | {Format.Rate(_emaSpeed)} | ETA {eta} | conns {conns}{files}{label}";

        lock (_writeLock)
        {
            if (line.Length < _lastLineLength)
                line += new string(' ', _lastLineLength - line.Length);
            Console.Write('\r' + line);
            _lastLineLength = line.TrimEnd().Length;
        }
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : "…" + value[^(max - 1)..];

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { await _renderLoop.ConfigureAwait(false); } catch { /* ignore */ }
        if (_interactive)
        {
            lock (_writeLock)
            {
                if (_lastLineLength > 0)
                    Console.Write('\r' + new string(' ', _lastLineLength) + '\r');
            }
        }
        _cts.Dispose();
    }
}
