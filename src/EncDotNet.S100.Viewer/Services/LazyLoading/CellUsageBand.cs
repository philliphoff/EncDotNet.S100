using System;

namespace EncDotNet.S100.Viewer.Services.LazyLoading;

/// <summary>
/// Parses the ENC <em>navigational purpose</em> (usage band) from a cell name.
/// </summary>
/// <remarks>
/// <para>
/// Both S-57 (S-57 Ed 3.1 Appendix B.1 §B.1.1) and S-101 (S-100 Part 10a /
/// S-101 §5.5) name a base cell as a two-character producer/country code
/// followed by a single navigational-purpose digit and up to eight further
/// characters of cell identifier — e.g. <c>US<b>1</b>EEZ1M</c> is band&#160;1
/// (Overview). The digit therefore lives at index&#160;2 (0-based) of the
/// cell name.
/// </para>
/// <para>
/// The band is a cheap, load-free proxy for a cell's intended scale range,
/// which lets the viewer decide whether a cell is relevant at the current
/// zoom <em>before</em> parsing it (viewport-driven lazy loading). A cell
/// whose band cannot be parsed returns <see langword="null"/>; callers treat
/// an unknown band as "always eligible" so nothing is hidden by an
/// unrecognised name.
/// </para>
/// </remarks>
internal static class CellUsageBand
{
    /// <summary>Lowest valid navigational-purpose band (Overview).</summary>
    public const int MinBand = 1;

    /// <summary>Highest valid navigational-purpose band (Berthing).</summary>
    public const int MaxBand = 6;

    /// <summary>
    /// Extracts the navigational-purpose band (<see cref="MinBand"/>..
    /// <see cref="MaxBand"/>) from <paramref name="cellName"/>, or
    /// <see langword="null"/> when the name is too short or the band
    /// character is not a valid digit in range.
    /// </summary>
    /// <param name="cellName">
    /// The cell name, with or without a file extension (e.g. <c>US1EEZ1M</c>
    /// or <c>US1EEZ1M.000</c>). Leading directory separators are ignored.
    /// </param>
    public static int? TryParse(string? cellName)
    {
        if (string.IsNullOrEmpty(cellName))
            return null;

        // Strip any directory prefix and extension so a relative path such as
        // "US1EEZ1M/US1EEZ1M.000" resolves to the bare cell name.
        var name = cellName;
        var lastSep = name.LastIndexOfAny(['/', '\\']);
        if (lastSep >= 0)
            name = name[(lastSep + 1)..];

        var dot = name.IndexOf('.');
        if (dot >= 0)
            name = name[..dot];

        if (name.Length < 3)
            return null;

        var c = name[2];
        if (c < '0' || c > '9')
            return null;

        var band = c - '0';
        return band is >= MinBand and <= MaxBand ? band : null;
    }
}
