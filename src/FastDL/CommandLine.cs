namespace FastDL;

/// <summary>Parses argv into <see cref="DownloadOptions"/> and renders usage.</summary>
public static class CommandLine
{
    public static DownloadOptions Parse(string[] args)
    {
        var options = new DownloadOptions();
        var mirrors = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h":
                case "--help":
                    options.Help = true;
                    break;
                case "-o":
                case "--out":
                    options.Output = RequireValue(args, ref i, arg);
                    break;
                case "-i":
                case "--input":
                    options.InputFile = RequireValue(args, ref i, arg);
                    break;
                case "--folder":
                    options.Folder = true;
                    break;
                case "--depth":
                    options.Depth = int.Parse(RequireValue(args, ref i, arg));
                    break;
                case "--ext":
                    options.Extensions = NormalizeExtensions(RequireValue(args, ref i, arg));
                    break;
                case "-c":
                case "--connections":
                    options.Connections = Math.Clamp(int.Parse(RequireValue(args, ref i, arg)), 1, 256);
                    break;
                case "-p":
                case "--parallel":
                    options.Parallel = Math.Clamp(int.Parse(RequireValue(args, ref i, arg)), 1, 64);
                    break;
                case "--chunk":
                    options.ChunkSize = Math.Max(64 * 1024, Format.ParseSize(RequireValue(args, ref i, arg)));
                    break;
                case "--extract":
                    options.Extract = true;
                    break;
                case "--zip":
                    options.ZipOutput = RequireValue(args, ref i, arg);
                    break;
                case "--mirror":
                    mirrors.Add(RequireValue(args, ref i, arg));
                    break;
                case "--no-resume":
                    options.NoResume = true;
                    break;
                case "--insecure":
                    options.Insecure = true;
                    break;
                case "--retries":
                    options.Retries = Math.Clamp(int.Parse(RequireValue(args, ref i, arg)), 0, 50);
                    break;
                case "--header":
                    options.Headers.Add(ParseHeader(RequireValue(args, ref i, arg)));
                    break;
                case "--user":
                    options.Credentials = RequireValue(args, ref i, arg);
                    break;
                case "-q":
                case "--quiet":
                    options.Quiet = true;
                    break;
                case "-v":
                case "--verbose":
                    options.Verbose = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                        throw new ArgumentException($"unknown option '{arg}' (try --help)");
                    options.Urls.Add(arg);
                    break;
            }
        }

        if (mirrors.Count > 0)
            options.Mirrors = mirrors.ToArray();

        return options;
    }

    private static string RequireValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"option '{flag}' requires a value");
        return args[++i];
    }

    private static (string, string) ParseHeader(string raw)
    {
        int colon = raw.IndexOf(':');
        if (colon <= 0)
            throw new ArgumentException($"invalid header '{raw}' (expected 'Key: Value')");
        return (raw[..colon].Trim(), raw[(colon + 1)..].Trim());
    }

    private static string[] NormalizeExtensions(string raw)
        => raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Select(e => e.StartsWith('.') ? e.ToLowerInvariant() : "." + e.ToLowerInvariant())
              .ToArray();

    public static string Usage =>
        """
        fdl - FastDL: a super-speed segmented downloader (faster than IDM on throttled & multi-source links)

        USAGE:
          fdl <url> [url2 ...] [options]
          fdl -i links.txt [options]
          fdl --folder <directory-index-url> [options]

        MODES:
          single        fdl https://host/file.iso
          multi-file    fdl url1 url2 url3            (or  -i links.txt)
          folder        fdl --folder https://host/dir/   (recursive autoindex crawl)
          multi-source  fdl <url> --mirror <url2> --mirror <url3>   (stripe chunks across mirrors)

        OPTIONS:
          -o, --out <path>        Output file (single) or directory (multi/folder). Default: current dir.
          -i, --input <file>      Read newline-separated URLs from a file.
          -c, --connections <n>   Parallel segments per file (default 16, max 256).
          -p, --parallel <n>      Files downloaded concurrently (default 4).
              --chunk <size>      Segment size, e.g. 1M, 4M, 512K (default 4M).
              --mirror <url>      Add an alternate source for the SAME file (repeatable).
              --folder            Treat each URL as a directory index and crawl it recursively.
              --depth <n>         Crawl recursion depth (default 5).
              --ext <list>        Only download these extensions when crawling, e.g. .iso,.zip
              --extract           Extract any downloaded .zip files after download.
              --zip <name.zip>    Pack all downloaded files into a single zip archive.
              --no-resume         Ignore existing partial data and restart.
              --header "K: V"     Add a request header (repeatable). Useful for auth/cookies.
              --user <user:pass>  HTTP Basic auth (e.g. password-protected seedboxes). Also via https://user:pass@host/…
              --insecure          Skip TLS certificate validation.
              --retries <n>       Per-segment retry attempts (default 5).
          -q, --quiet             Minimal output.
          -v, --verbose           Verbose logging.
          -h, --help              Show this help.

        EXAMPLES:
          fdl https://proof.ovh.net/files/100Mb.dat -c 32
          fdl https://host/big.zip --extract
          fdl --folder https://proof.ovh.net/files/ --ext .dat -o .\dump
          fdl https://a/file.iso --mirror https://b/file.iso --mirror https://c/file.iso
          fdl -i links.txt -o .\out --zip bundle.zip
        """;
}
