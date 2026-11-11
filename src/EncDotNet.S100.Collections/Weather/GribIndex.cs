using System.Globalization;

namespace EncDotNet.S100.Collections.Weather;

/// <summary>One line of a GRIB2 <c>.idx</c> inventory, as <c>wgrib2 -s</c> writes it.</summary>
/// <param name="Number">The message number (<c>588</c>, or <c>588.2</c> for a field inside a message).</param>
/// <param name="Offset">The message's byte offset in the GRIB2 file.</param>
/// <param name="Text">The whole line, e.g. <c>588:34343549:d=2026102718:UGRD:10 m above ground:3 hour fcst:</c>.</param>
public sealed record GribIndexEntry(string Number, long Offset, string Text)
{
    /// <summary>The short name (<c>UGRD</c>).</summary>
    public string Variable => Field(3);

    /// <summary>The level (<c>10 m above ground</c>).</summary>
    public string Level => Field(4);

    /// <summary>The forecast time (<c>3 hour fcst</c>, <c>anl</c>).</summary>
    public string Forecast => Field(5);

    /// <summary>True when the line is for the parameter <paramref name="match"/> names (<c>:UGRD:10 m above ground:</c>).</summary>
    public bool Matches(string match) => (":" + Variable + ":" + Level + ":").Equals(match, StringComparison.Ordinal);

    private string Field(int index)
    {
        var parts = Text.Split(':');
        return parts.Length > index ? parts[index] : string.Empty;
    }
}

/// <summary>An inclusive byte range of a GRIB2 file; <see cref="Last"/> is null to read to the end.</summary>
/// <param name="First">The first byte.</param>
/// <param name="Last">The last byte, or <see langword="null"/> for the rest of the file.</param>
public readonly record struct GribByteRange(long First, long? Last)
{
    /// <summary>The <c>Range</c> header value: <c>bytes=100-199</c> or <c>bytes=100-</c>.</summary>
    public string HeaderValue => Last is { } last
        ? string.Create(CultureInfo.InvariantCulture, $"bytes={First}-{last}")
        : string.Create(CultureInfo.InvariantCulture, $"bytes={First}-");
}

/// <summary>
/// A GRIB2 file's <c>.idx</c> inventory (#1075): NCEP publishes one beside
/// every GFS and GFS-Wave file (<c>gfs.t00z.pgrb2.0p25.f003.idx</c>), one
/// line per message with its byte offset, so the messages for a few
/// parameters can be read from a file of hundreds with HTTP byte ranges.
/// </summary>
public sealed class GribIndex
{
    private GribIndex(IReadOnlyList<GribIndexEntry> entries) => Entries = entries;

    /// <summary>The lines, in file order.</summary>
    public IReadOnlyList<GribIndexEntry> Entries { get; }

    /// <summary>
    /// Reads an inventory: lines <c>number:offset:d=YYYYMMDDHH:VAR:level:forecast:</c>.
    /// Blank lines are skipped.
    /// </summary>
    /// <exception cref="FormatException">A line isn't an inventory line, or offsets go backwards.</exception>
    public static GribIndex Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var entries = new List<GribIndexEntry>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
                continue;
            var parts = line.Split(':');
            if (parts.Length < 6 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var offset))
                throw new FormatException($"'{line}' is not a GRIB2 .idx line.");
            if (entries.Count > 0 && offset < entries[^1].Offset)
                throw new FormatException($"The .idx offsets go backwards at '{line}'.");
            entries.Add(new GribIndexEntry(parts[0], offset, line));
        }

        return new GribIndex(entries);
    }

    /// <summary>
    /// The byte ranges holding the messages of the parameters
    /// <paramref name="matches"/> name (<c>:UGRD:10 m above ground:</c>), in
    /// file order, with neighbouring messages joined into one range. A message
    /// runs to the next line with a larger offset (fields of one message share
    /// its offset), or to the end of the file.
    /// </summary>
    public IReadOnlyList<GribByteRange> RangesFor(IReadOnlyCollection<string> matches)
    {
        ArgumentNullException.ThrowIfNull(matches);
        var ranges = new List<GribByteRange>();
        for (int i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (!matches.Any(entry.Matches))
                continue;
            long? end = null;
            for (int j = i + 1; j < Entries.Count; j++)
            {
                if (Entries[j].Offset > entry.Offset)
                {
                    end = Entries[j].Offset - 1;
                    break;
                }
            }

            if (ranges.Count > 0 && ranges[^1] is { Last: { } last } previous)
            {
                if (entry.Offset <= last)
                    continue; // Another field of a message already included.
                if (entry.Offset == last + 1)
                {
                    ranges[^1] = previous with { Last = end };
                    continue;
                }
            }

            ranges.Add(new GribByteRange(entry.Offset, end));
        }

        return ranges;
    }
}
