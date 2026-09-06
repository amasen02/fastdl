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

        // Authorization is deliberately NOT a default header: this one client serves every host
        // in the run, so a default would send one host's password to all of them. See
        // ApplyCustomHeaders, which attaches it per request against that request's origin.
        return client;
    }

    /// <summary>
    /// Applies the per-request headers for a transfer: user <c>--header</c> values, plus the
    /// Authorization header for the credential that applies to <em>this request's</em> origin.
    /// </summary>
    public static void ApplyCustomHeaders(HttpRequestMessage request, DownloadOptions options)
    {
        foreach (var (key, value) in options.Headers)
            request.Headers.TryAddWithoutValidation(key, value);

        string? credentials = CredentialsFor(request.RequestUri, options);
        if (!string.IsNullOrEmpty(credentials))
        {
            string token = Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }

    /// <summary>
    /// Picks the credential for one request: an inline credential typed for that exact origin,
    /// otherwise the run-wide <c>--user</c>/<c>FDL_PASSWORD</c> credential, otherwise none.
    /// Inline credentials never fall through to a host they were not typed for.
    /// </summary>
    public static string? CredentialsFor(Uri? url, DownloadOptions options)
    {
        if (url is not null && options.OriginCredentials.TryGetValue(OriginOf(url), out string? scoped))
            return scoped;
        return options.Credentials;
    }

    /// <summary>Scheme + host + port — the boundary a credential must not cross.</summary>
    public static string OriginOf(Uri url) => $"{url.Scheme}://{url.Host}:{url.Port}";
}
