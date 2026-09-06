using System.Text;

namespace FastDL;

/// <summary>CLI entrypoint: parses options, builds the work list, runs downloads, then archives.</summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        DownloadOptions options;
        try
        {
            options = CommandLine.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"fdl: {ex.Message}");
            return 2;
        }

        if (options.Help || (options.Urls.Count == 0 && options.InputFile is null))
        {
            Console.WriteLine(CommandLine.Usage);
            return options.Help ? 0 : 1;
        }

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // first Ctrl+C: cancel gracefully; resume metadata is preserved
            Console.Error.WriteLine("\nfdl: cancelling… (partial progress saved for resume)");
            cancellation.Cancel();
        };

        ApplyUrlCredentials(options);
        ResolveCredentialPassword(options);
        using HttpClient http = HttpClientProvider.Create(options);
        await using var progress = new ProgressReporter(options.Quiet);
        var downloader = new SegmentedDownloader(http, options, progress);

        try
        {
            IReadOnlyList<DownloadSpec> specs = await BuildSpecsAsync(http, options, progress, cancellation.Token)
                                                      .ConfigureAwait(false);
            if (specs.Count == 0)
            {
                Console.Error.WriteLine("fdl: nothing to download (no matching files found).");
                return 1;
            }

            if (!options.Quiet)
                Console.WriteLine($"fdl: {specs.Count} file(s) | {options.Connections} segments/file | {options.Parallel} parallel | chunk {Format.Bytes(options.ChunkSize)}");

            var batch = new BatchDownloader(downloader, options, progress);
            IReadOnlyList<DownloadResult> results = await batch.RunAsync(specs, cancellation.Token).ConfigureAwait(false);

            RunPostProcessing(options, results, progress);

            return Summarize(results, progress, options);
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("fdl: cancelled.");
            return 130;
        }
        catch (Exception ex)
        {
            progress.Log($"fdl: fatal: {ex.Message}");
            return 1;
        }
    }

    private static async Task<IReadOnlyList<DownloadSpec>> BuildSpecsAsync(
        HttpClient http, DownloadOptions options, ProgressReporter progress, CancellationToken ct)
    {
        var urls = new List<string>(options.Urls);
        if (options.InputFile is not null)
            urls.AddRange(ReadUrlsFromFile(options.InputFile));

        if (urls.Count == 0)
            throw new ArgumentException("no URLs provided");

        if (options.Folder)
            return await BuildFolderSpecsAsync(http, options, progress, urls, ct).ConfigureAwait(false);

        if (options.Mirrors is { Length: > 0 })
        {
            if (urls.Count != 1)
                throw new ArgumentException("--mirror is for a single file; pass exactly one primary URL");
            var sources = new List<Uri> { ToUri(urls[0]) };
            sources.AddRange(options.Mirrors.Select(ToUri));
            return new[] { new DownloadSpec(sources, ResolveSingleOutput(options)) };
        }

        if (urls.Count == 1)
            return new[] { new DownloadSpec(new[] { ToUri(urls[0]) }, ResolveSingleOutput(options)) };

        // Multiple files -> output must be a directory (names resolved per-file at download time).
        string directory = EnsureDirectory(options.Output);
        return urls.Select(u => new DownloadSpec(new[] { ToUri(u) }, directory)).ToArray();
    }

    private static async Task<IReadOnlyList<DownloadSpec>> BuildFolderSpecsAsync(
        HttpClient http, DownloadOptions options, ProgressReporter progress, List<string> urls, CancellationToken ct)
    {
        string root = EnsureDirectory(options.Output);
        var crawler = new IndexCrawler(http, options);
        var specs = new List<DownloadSpec>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string url in urls)
        {
            if (!options.Quiet) progress.Log($"fdl: crawling {url} …");
            IReadOnlyList<CrawlItem> items = await crawler.CrawlAsync(ToUri(url), ct).ConfigureAwait(false);
            foreach (CrawlItem item in items)
            {
                string outputPath = Path.Combine(root, item.RelativePath);

                // The crawler already refuses traversing links; this is the last gate before a
                // remote-supplied name decides where bytes land, so re-check it independently.
                if (!PathGuard.IsInside(root, outputPath))
                {
                    progress.Log($"fdl: skipping '{item.Url}' — its path escapes the output directory.");
                    continue;
                }

                if (seen.Add(outputPath))
                    specs.Add(new DownloadSpec(new[] { item.Url }, outputPath));
            }
            if (!options.Quiet) progress.Log($"fdl: found {items.Count} file(s) under {url}");
        }
        return specs;
    }

    private static void RunPostProcessing(
        DownloadOptions options, IReadOnlyList<DownloadResult> results, ProgressReporter progress)
    {
        var successfulPaths = results.Where(r => r is { Success: true }).Select(r => r.OutputPath).ToList();
        if (successfulPaths.Count == 0) return;

        if (options.Extract)
        {
            int count = ZipService.ExtractZips(successfulPaths, progress);
            if (count == 0 && !options.Quiet) progress.Log("fdl: --extract set but no .zip files were downloaded.");
        }

        if (options.ZipOutput is not null)
        {
            string baseDir = options.Folder || successfulPaths.Count > 1
                ? EnsureDirectory(options.Output)
                : Path.GetDirectoryName(Path.GetFullPath(successfulPaths[0]))!;
            ZipService.PackAll(successfulPaths, options.ZipOutput, baseDir, progress);
        }
    }

    private static int Summarize(IReadOnlyList<DownloadResult> results, ProgressReporter progress, DownloadOptions options)
    {
        int ok = results.Count(r => r is { Success: true });
        int failed = results.Count - ok;
        long bytes = results.Where(r => r is { Success: true }).Sum(r => r.Bytes);

        if (!options.Quiet)
        {
            Console.WriteLine();
            Console.WriteLine($"fdl: done. {ok} ok, {failed} failed | {Format.Bytes(bytes)} in {Format.Duration(progress.Elapsed)} | avg {Format.Rate(progress.AverageSpeed)}");
        }
        return failed == 0 ? 0 : 1;
    }

    // ---- Path helpers ------------------------------------------------------

    private static string ResolveSingleOutput(DownloadOptions options)
    {
        if (string.IsNullOrEmpty(options.Output))
            return Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar;

        bool looksLikeDirectory = Directory.Exists(options.Output)
            || options.Output.EndsWith(Path.DirectorySeparatorChar)
            || options.Output.EndsWith(Path.AltDirectorySeparatorChar);
        if (looksLikeDirectory)
        {
            Directory.CreateDirectory(options.Output);
            return options.Output.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                   + Path.DirectorySeparatorChar;
        }
        return options.Output; // concrete file path
    }

    private static string EnsureDirectory(string? output)
    {
        string directory = string.IsNullOrEmpty(output) ? Directory.GetCurrentDirectory() : output;
        Directory.CreateDirectory(directory);
        return directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
               + Path.DirectorySeparatorChar;
    }

    private static IEnumerable<string> ReadUrlsFromFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"input file not found: {path}");
        return File.ReadAllLines(path)
                   .Select(line => line.Trim())
                   .Where(line => line.Length > 0 && !line.StartsWith('#'));
    }

    /// <summary>When only a username was supplied, resolve the password from FDL_PASSWORD or a hidden prompt.</summary>
    private static void ResolveCredentialPassword(DownloadOptions options)
    {
        if (!string.IsNullOrEmpty(options.Credentials) && !options.Credentials.Contains(':'))
            options.Credentials = WithPassword(options.Credentials, origin: null);

        foreach (string origin in options.OriginCredentials.Keys.ToList())
        {
            string credentials = options.OriginCredentials[origin];
            if (!credentials.Contains(':'))
                options.OriginCredentials[origin] = WithPassword(credentials, origin);
        }
    }

    private static string WithPassword(string user, string? origin)
    {
        string? password = Environment.GetEnvironmentVariable("FDL_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            if (Console.IsInputRedirected)
                throw new ArgumentException("password required: use --user user:pass or set FDL_PASSWORD");
            password = ReadHidden(origin is null ? $"Password for {user}: " : $"Password for {user} at {origin}: ");
        }
        return $"{user}:{password}";
    }

    private static string ReadHidden(string prompt)
    {
        Console.Write(prompt);
        var builder = new StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0) builder.Length--;
            }
            else if (!char.IsControl(key.KeyChar))
            {
                builder.Append(key.KeyChar);
            }
        }
        Console.WriteLine();
        return builder.ToString();
    }

    /// <summary>
    /// Extracts inline userinfo (https://user:pass@host) into Basic credentials and strips it
    /// from the URL. Each credential is filed under the origin it was typed for, so a password
    /// meant for one host is never offered to the other URLs in the same run.
    /// </summary>
    internal static void ApplyUrlCredentials(DownloadOptions options)
    {
        for (int i = 0; i < options.Urls.Count; i++)
        {
            if (!Uri.TryCreate(options.Urls[i], UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.UserInfo))
                continue;

            options.OriginCredentials[HttpClientProvider.OriginOf(uri)] = Uri.UnescapeDataString(uri.UserInfo);
            options.Urls[i] = new UriBuilder(uri) { UserName = string.Empty, Password = string.Empty }.Uri.AbsoluteUri;
        }
    }

    private static Uri ToUri(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"invalid URL: '{raw}' (only http/https supported)");
        return uri;
    }
}
