using FastDL;
using Xunit;

namespace FastDL.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(512, "512 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    public void Bytes_formats_human_readable(long value, string expected)
        => Assert.Equal(expected, Format.Bytes(value));

    [Fact]
    public void Bytes_negative_is_zero() => Assert.Equal("0 B", Format.Bytes(-5));

    [Fact]
    public void Rate_appends_per_second() => Assert.Equal("1.00 KB/s", Format.Rate(1024));

    [Theory]
    [InlineData("4M", 4L * 1024 * 1024)]
    [InlineData("512K", 512L * 1024)]
    [InlineData("1G", 1024L * 1024 * 1024)]
    [InlineData("2048", 2048)]
    public void ParseSize_parses_suffixes(string text, long expected)
        => Assert.Equal(expected, Format.ParseSize(text));

    [Fact]
    public void ParseSize_accepts_fractional() => Assert.Equal((long)(1.5 * 1024 * 1024), Format.ParseSize("1.5M"));

    [Fact]
    public void ParseSize_empty_throws() => Assert.Throws<FormatException>(() => Format.ParseSize("   "));

    [Fact]
    public void ParseSize_unknown_suffix_throws() => Assert.Throws<FormatException>(() => Format.ParseSize("10X"));

    [Fact]
    public void Duration_negative_is_placeholder() => Assert.Equal("--", Format.Duration(TimeSpan.FromSeconds(-1)));

    [Fact]
    public void Duration_seconds_only() => Assert.Equal("9s", Format.Duration(TimeSpan.FromSeconds(9)));

    [Fact]
    public void Duration_minutes_and_seconds() => Assert.Equal("2m05s", Format.Duration(TimeSpan.FromSeconds(125)));

    [Fact]
    public void Duration_hours_minutes_seconds() => Assert.Equal("1h01m05s", Format.Duration(TimeSpan.FromSeconds(3665)));
}
