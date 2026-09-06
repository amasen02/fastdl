using FastDL;
using Xunit;

namespace FastDL.Tests;

public class PathGuardTests
{
    private static readonly string Root =
        Path.Combine(Path.GetTempPath(), "fdlguard") + Path.DirectorySeparatorChar;

    [Theory]
    [InlineData("a.iso")]
    [InlineData("sub/a.iso")]
    [InlineData("sub/deeper/a.iso")]
    [InlineData("sub/../a.iso")]
    public void Accepts_paths_that_land_inside_the_root(string relative)
        => Assert.True(PathGuard.IsInside(Root, Path.Combine(Root, relative)));

    [Theory]
    [InlineData("../evil.iso")]
    [InlineData("../../evil.iso")]
    [InlineData("sub/../../evil.iso")]
    [InlineData("..")]
    public void Rejects_paths_that_walk_out_of_the_root(string relative)
        => Assert.False(PathGuard.IsInside(Root, Path.Combine(Root, relative)));

    [Fact]
    public void Rejects_a_sibling_directory_that_shares_the_root_prefix()
        => Assert.False(PathGuard.IsInside(Root, Root.TrimEnd(Path.DirectorySeparatorChar) + "-evil" + Path.DirectorySeparatorChar + "x.iso"));

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public void Dot_segments_are_recognised(string segment) => Assert.True(PathGuard.IsDotSegment(segment));

    [Theory]
    [InlineData("...")]
    [InlineData(".iso")]
    [InlineData("..a")]
    [InlineData("a.iso")]
    [InlineData("")]
    public void Ordinary_names_are_not_dot_segments(string segment) => Assert.False(PathGuard.IsDotSegment(segment));
}
