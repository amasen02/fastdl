using System.Text;
using FastDL;
using Xunit;

namespace FastDL.Tests;

/// <summary>
/// A credential typed into one URL must reach that URL's host and no other. The shared
/// <see cref="HttpClient"/> serves every host in a run, so scoping has to happen per request.
/// </summary>
public class CredentialScopeTests
{
    private static string? BasicHeaderFor(string url, DownloadOptions options)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        HttpClientProvider.ApplyCustomHeaders(request, options);
        var auth = request.Headers.Authorization;
        return auth is null ? null : Encoding.UTF8.GetString(Convert.FromBase64String(auth.Parameter!));
    }

    private static DownloadOptions WithUrls(params string[] urls)
    {
        var options = new DownloadOptions();
        options.Urls.AddRange(urls);
        Program.ApplyUrlCredentials(options);
        return options;
    }

    [Fact]
    public void Inline_userinfo_is_stripped_from_the_url()
    {
        var options = WithUrls("https://alice:PLACEHOLDER-A@private.example/a.iso");

        Assert.Equal("https://private.example/a.iso", options.Urls[0]);
        Assert.DoesNotContain("alice", options.Urls[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Inline_userinfo_is_sent_back_to_its_own_origin()
        => Assert.Equal("alice:PLACEHOLDER-A",
                        BasicHeaderFor("https://private.example/a.iso",
                                       WithUrls("https://alice:PLACEHOLDER-A@private.example/a.iso")));

    [Fact]
    public void Inline_userinfo_is_not_sent_to_any_other_host()
    {
        var options = WithUrls("https://alice:PLACEHOLDER-A@private.example/a.iso", "https://cdn.attacker.example/b.iso");

        Assert.Null(BasicHeaderFor("https://cdn.attacker.example/b.iso", options));
    }

    [Fact]
    public void Inline_userinfo_does_not_cross_scheme_or_port()
    {
        var options = WithUrls("https://alice:PLACEHOLDER-A@private.example/a.iso");

        Assert.Null(BasicHeaderFor("http://private.example/a.iso", options));
        Assert.Null(BasicHeaderFor("https://private.example:8443/a.iso", options));
    }

    [Fact]
    public void Each_url_keeps_its_own_credential()
    {
        var options = WithUrls("https://alice:PLACEHOLDER-1@a.example/x.iso", "https://bob:PLACEHOLDER-2@b.example/y.iso");

        Assert.Equal("alice:PLACEHOLDER-1", BasicHeaderFor("https://a.example/x.iso", options));
        Assert.Equal("bob:PLACEHOLDER-2", BasicHeaderFor("https://b.example/y.iso", options));
        Assert.Null(BasicHeaderFor("https://c.example/z.iso", options));
    }

    [Fact]
    public void Explicit_user_flag_stays_run_wide_as_documented()
    {
        var options = new DownloadOptions { Credentials = "flag:PLACEHOLDER-RUNWIDE" };

        Assert.Equal("flag:PLACEHOLDER-RUNWIDE", BasicHeaderFor("https://a.example/x.iso", options));
        Assert.Equal("flag:PLACEHOLDER-RUNWIDE", BasicHeaderFor("https://b.example/y.iso", options));
    }

    [Fact]
    public void Origin_credential_wins_over_the_run_wide_one_for_its_own_host()
    {
        var options = WithUrls("https://alice:PLACEHOLDER-A@private.example/a.iso");
        options.Credentials = "flag:PLACEHOLDER-RUNWIDE";

        Assert.Equal("alice:PLACEHOLDER-A", BasicHeaderFor("https://private.example/a.iso", options));
        Assert.Equal("flag:PLACEHOLDER-RUNWIDE", BasicHeaderFor("https://elsewhere.example/b.iso", options));
    }

    [Fact]
    public void Shared_client_carries_no_default_authorization_header()
    {
        var options = new DownloadOptions { Credentials = "flag:PLACEHOLDER-RUNWIDE" };
        using HttpClient client = HttpClientProvider.Create(options);

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }
}
