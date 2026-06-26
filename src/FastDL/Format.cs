using System.Globalization;

namespace FastDL;

/// <summary>Human-readable formatting helpers for sizes, rates and durations.</summary>
public static class Format
{
    private static readonly string[] ByteUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    public static string Bytes(long value)
    {
        if (value < 0) return "0 B";
        double size = value;
        int unit = 0;
        while (size >= 1024 && unit < ByteUnits.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0
            ? $"{(long)size} {ByteUnits[unit]}"
            : string.Create(CultureInfo.InvariantCulture, $"{size:0.00} {ByteUnits[unit]}");
    }

    public static string Rate(double bytesPerSecond)
        => $"{Bytes((long)bytesPerSecond)}/s";

    public static string Duration(TimeSpan span)
    {
        if (span.TotalSeconds < 0 || double.IsInfinity(span.TotalSeconds) || double.IsNaN(span.TotalSeconds))
            return "--";
        if (span.TotalHours >= 1)
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h{span.Minutes:00}m{span.Seconds:00}s");
        if (span.TotalMinutes >= 1)
            return string.Create(CultureInfo.InvariantCulture, $"{span.Minutes}m{span.Seconds:00}s");
        return string.Create(CultureInfo.InvariantCulture, $"{span.Seconds}s");
    }

    /// <summary>Parses a size string such as "4M", "512K", "1G" or a raw byte count.</summary>
    public static long ParseSize(string text)
    {
        text = text.Trim();
        if (text.Length == 0) throw new FormatException("empty size");
        long multiplier = 1;
        char suffix = char.ToUpperInvariant(text[^1]);
        if (!char.IsDigit(suffix))
        {
            multiplier = suffix switch
            {
                'K' => 1024L,
                'M' => 1024L * 1024,
                'G' => 1024L * 1024 * 1024,
                'T' => 1024L * 1024 * 1024 * 1024,
                _ => throw new FormatException($"unknown size suffix '{suffix}'")
            };
            text = text[..^1];
        }
        return (long)(double.Parse(text, CultureInfo.InvariantCulture) * multiplier);
    }
}
