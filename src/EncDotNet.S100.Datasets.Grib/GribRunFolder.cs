namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// A folder holding one model run as several GRIB2 files (#1075; design
/// note weather-sources.md §3.4): NOMADS and AWS publish one file per
/// forecast hour (<c>gfswave.t00z.global.0p25.f000.grib2</c>, <c>…f003…</c>),
/// and a folder of them is read as one dataset, the files appended in name
/// order. <see cref="GribWeatherDataset"/> then merges the fields by
/// parameter, orders each parameter's steps by valid time, and keeps the
/// latest run when the folder mixes runs (listing the older runs' fields).
/// </summary>
public static class GribRunFolder
{
    /// <summary>
    /// The GRIB2 files directly in <paramref name="folder"/>, in ordinal name
    /// order: files that start with a GRIB edition 2 indicator, whatever their
    /// extension. <c>.idx</c> inventories, hidden files and other files are
    /// skipped; sub-folders aren't read.
    /// </summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The files' full paths; empty when there are none or the folder can't be read.</returns>
    public static IReadOnlyList<string> Files(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        try
        {
            return [.. Directory.EnumerateFiles(folder)
                .Where(f => !Path.GetFileName(f).StartsWith('.')
                    && !f.EndsWith(".idx", StringComparison.OrdinalIgnoreCase)
                    && IsGrib2(f))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>True when <paramref name="path"/> is a folder holding at least one GRIB2 file (see <see cref="Files"/>).</summary>
    /// <param name="path">A path.</param>
    public static bool IsRunFolder(string path) =>
        !string.IsNullOrEmpty(path) && Directory.Exists(path) && Files(path).Count > 0;

    /// <summary>True when the file at <paramref name="path"/> starts with a GRIB edition 2 indicator.</summary>
    /// <param name="path">A file path.</param>
    public static bool IsGrib2(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[8];
            using var stream = File.OpenRead(path);
            return stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8
                && header[..4].SequenceEqual("GRIB"u8)
                && header[7] == 2;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
