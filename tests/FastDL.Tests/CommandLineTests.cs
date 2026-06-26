using FastDL;
using Xunit;

namespace FastDL.Tests;

public class CommandLineTests
{
    [Fact]
    public void Defaults_are_sensible()
    {
        var options = CommandLine.Parse(new[] { "https://h/f" });
        Assert.Equal(16, options.Connections);
        Assert.Equal(4, options.Parallel);
        Assert.Equal(4L * 1024 * 1024, options.ChunkSize);
        Assert.Equal(5, options.Retries);
        Assert.Equal(5, options.Depth);
        Assert.Equal(new[] { "https://h/f" }, options.Urls);
    }

    [Fact]
    public void Connections_clamped_to_max() => Assert.Equal(256, CommandLine.Parse(new[] { "-c", "9999", "u" }).Connections);

    [Fact]
    public void Connections_clamped_to_min() => Assert.Equal(1, CommandLine.Parse(new[] { "-c", "0", "u" }).Connections);

    [Fact]
    public void Parallel_clamped_to_max() => Assert.Equal(64, CommandLine.Parse(new[] { "-p", "1000", "u" }).Parallel);

    [Fact]
    public void Chunk_has_64k_floor() => Assert.Equal(64L * 1024, CommandLine.Parse(new[] { "--chunk", "1K", "u" }).ChunkSize);

    [Fact]
    public void Retries_clamped() => Assert.Equal(50, CommandLine.Parse(new[] { "--retries", "999", "u" }).Retries);

    [Fact]
    public void Mirrors_are_collected()
    {
        var options = CommandLine.Parse(new[] { "u", "--mirror", "m1", "--mirror", "m2" });
        Assert.Equal(new[] { "m1", "m2" }, options.Mirrors);
    }

    [Fact]
    public void Header_parsed_into_key_value()
    {
        var options = CommandLine.Parse(new[] { "u", "--header", "Authorization: Bearer abc" });
        Assert.Equal(("Authorization", "Bearer abc"), options.Headers[0]);
    }

    [Fact]
    public void Header_without_colon_throws()
        => Assert.Throws<ArgumentException>(() => CommandLine.Parse(new[] { "u", "--header", "nope" }));

    [Fact]
    public void Extensions_normalised_with_dot_and_lowercase()
    {
        var options = CommandLine.Parse(new[] { "u", "--ext", "ISO,.Zip,bin" });
        Assert.Equal(new[] { ".iso", ".zip", ".bin" }, options.Extensions);
    }

    [Fact]
    public void Unknown_option_throws() => Assert.Throws<ArgumentException>(() => CommandLine.Parse(new[] { "--nope" }));

    [Fact]
    public void Missing_value_throws() => Assert.Throws<ArgumentException>(() => CommandLine.Parse(new[] { "-o" }));

    [Fact]
    public void Boolean_flags_set()
    {
        var options = CommandLine.Parse(new[] { "u", "--folder", "--extract", "--no-resume", "--insecure", "-q", "-v" });
        Assert.True(options.Folder);
        Assert.True(options.Extract);
        Assert.True(options.NoResume);
        Assert.True(options.Insecure);
        Assert.True(options.Quiet);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Positional_arguments_become_urls()
    {
        var options = CommandLine.Parse(new[] { "a", "b", "-c", "8", "c" });
        Assert.Equal(new[] { "a", "b", "c" }, options.Urls);
        Assert.Equal(8, options.Connections);
    }
}
