using System.IO.Compression;

namespace FastDL;

/// <summary>Post-download archive operations: extract downloaded zips, or bundle outputs into one zip.</summary>
public static class ZipService
{
    // ZIP local-file-header / end-of-central-directory / data-descriptor signatures.
    private static readonly byte[][] ZipSignatures =
    {
        new byte[] { 0x50, 0x4B, 0x03, 0x04 },
        new byte[] { 0x50, 0x4B, 0x05, 0x06 },
        new byte[] { 0x50, 0x4B, 0x07, 0x08 },
    };

    /// <summary>Extracts every zip among <paramref name="paths"/> into a sibling folder. Returns the count extracted.</summary>
    /// <remarks>Detection is by file content (PK signature), so files saved without a .zip extension still extract.</remarks>
    public static int ExtractZips(IEnumerable<string> paths, ProgressReporter progress)
    {
        int extracted = 0;
        foreach (string path in paths)
        {
            if (!File.Exists(path) || !IsZip(path)) continue;

            string destination = ResolveExtractDirectory(path);
            try
            {
                Directory.CreateDirectory(destination);
                ZipFile.ExtractToDirectory(path, destination, overwriteFiles: true);
                progress.Log($"  ⤷ extracted {Path.GetFileName(path)} -> {Path.GetFileName(destination)}\\");
                extracted++;
            }
            catch (Exception ex)
            {
                progress.Log($"  ! extract failed for {Path.GetFileName(path)}: {ex.Message}");
            }
        }
        return extracted;
    }

    private static bool IsZip(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[4];
            using var stream = File.OpenRead(path);
            if (stream.Read(header) < 4) return false;
            foreach (byte[] signature in ZipSignatures)
                if (header.SequenceEqual(signature)) return true;
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string ResolveExtractDirectory(string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        bool hasZipExtension = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        string name = hasZipExtension ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path) + "_extracted";
        string destination = Path.Combine(directory, name);
        // Never collide with the archive file itself.
        return string.Equals(destination, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)
            ? destination + "_extracted"
            : destination;
    }

    /// <summary>Packs the given files into a single zip, keeping their paths relative to <paramref name="baseDir"/>.</summary>
    public static void PackAll(IReadOnlyList<string> files, string zipPath, string baseDir, ProgressReporter progress)
    {
        string fullBase = Path.GetFullPath(baseDir);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath))!);
        if (File.Exists(zipPath)) File.Delete(zipPath);

        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (string file in files)
        {
            if (!File.Exists(file)) continue;
            string full = Path.GetFullPath(file);
            string entryName = full.StartsWith(fullBase, StringComparison.OrdinalIgnoreCase)
                ? Path.GetRelativePath(fullBase, full)
                : Path.GetFileName(full);
            archive.CreateEntryFromFile(full, entryName.Replace('\\', '/'), CompressionLevel.Optimal);
        }
        progress.Log($"  ⤷ packed {files.Count} file(s) -> {Path.GetFileName(zipPath)} ({Format.Bytes(new FileInfo(zipPath).Length)})");
    }
}
