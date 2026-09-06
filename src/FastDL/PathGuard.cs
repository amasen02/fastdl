namespace FastDL;

/// <summary>
/// Containment checks for every path FastDL derives from remote input (crawled hrefs,
/// <c>Content-Disposition</c> file names). Remote input decides file <em>names</em>; it must
/// never decide the <em>directory</em> a download lands in.
/// </summary>
public static class PathGuard
{
    /// <summary>
    /// True when <paramref name="candidate"/> resolves to a location inside
    /// <paramref name="root"/> once <c>..</c> segments, redundant separators and drive letters
    /// are normalised away. Comparison follows the platform's path semantics (case-insensitive
    /// on Windows). Symbolic links are not resolved, so this bounds the path, not the inode.
    /// </summary>
    public static bool IsInside(string root, string candidate)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullCandidate = Path.GetFullPath(candidate);

        string relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (Path.IsPathRooted(relative)) return false;              // different volume entirely
        if (relative == "..") return false;
        return !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith("../", StringComparison.Ordinal);
    }

    /// <summary>True for a path segment that navigates rather than names: "." or "..".</summary>
    public static bool IsDotSegment(string segment)
        => segment.Length is 1 or 2 && segment.All(c => c == '.');
}
