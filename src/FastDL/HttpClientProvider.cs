using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace FastDL;

/// <summary>Builds a single throughput-tuned <see cref="HttpClient"/> shared by all workers.</summary>
public static class HttpClientProvider
{
    public const string UserAgent = "FastDL/1.0 (+segmented-downloader)";

    public static HttpClient Create(DownloadOptions options)
    {
        var handler = new SocketsHttpHandler
        {
            // One pool, many sockets per host: this is what lets segments run truly in parallel.
            MaxConnectionsPerServer = Math.Min(256, Math.Max(options.Connections * options.Parallel, options.Connections)),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            EnableMultipleHttp2Connections = true,
            AutomaticDecompression = DecompressionMethods.None, // never auto-decompress: corrupts byte-range math
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ResponseDrainTimeout = TimeSpan.FromSeconds(5),
        };

        if (options.Insecure)
            handler.SslOptions.RemoteCertificateValidationCallback = static (_, _, _, _) => true;

        var client = new HttpClient(handler)
        {
            // Big downloads must not hit an overall timeout; per-read cancellation handles stalls.
            Timeout = Timeout.InfiniteTimeSpan,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("identity"); // ask servers not to compress

        if (!string.IsNullOrEmpty(options.Credentials))
        {
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(options.Credentials));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
        return client;
    }

    public static void ApplyCustomHeaders(HttpRequestMessage request, DownloadOptions options)
    {
        foreach (var (key, value) in options.Headers)
            request.Headers.TryAddWithoutValidation(key, value);
    }
}
