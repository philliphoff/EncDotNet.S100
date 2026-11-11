using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>
/// A tropical cyclone's identity: its ATCF id, name and basin.
/// </summary>
/// <param name="AtcfId">
/// The ATCF storm id in upper case: the basin, the storm's number in the
/// season and the year, for example <c>"AL092022"</c>.
/// </param>
/// <param name="Name">The storm's name as NHC writes it in the advisory, for example <c>"Ian"</c> or <c>"Nine"</c>.</param>
public sealed partial record TropicalCycloneStorm(string AtcfId, string Name)
{
    /// <summary>
    /// The ATCF basin code: <c>"AL"</c> (North Atlantic), <c>"EP"</c> (East
    /// Pacific), <c>"CP"</c> (Central Pacific), <c>"WP"</c> (West Pacific),
    /// <c>"IO"</c> (North Indian Ocean) or <c>"SH"</c> (Southern Hemisphere).
    /// </summary>
    public string Basin => AtcfId.Length >= 2 ? AtcfId[..2] : string.Empty;

    /// <summary>The storm's number in the basin's season, or 0 when the id has none.</summary>
    public int Number => AtcfId.Length >= 4 && int.TryParse(AtcfId.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;

    /// <summary>The season's year, or 0 when the id has none.</summary>
    public int Year => AtcfId.Length >= 8 && int.TryParse(AtcfId.AsSpan(4, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) ? y : 0;

    /// <summary>The basin's name in words, for example <c>"North Atlantic"</c>, or the code when it isn't known.</summary>
    public string BasinName => Basin switch
    {
        "AL" => "North Atlantic",
        "EP" => "East Pacific",
        "CP" => "Central Pacific",
        "WP" => "West Pacific",
        "IO" => "North Indian Ocean",
        "SH" => "Southern Hemisphere",
        "SL" => "South Atlantic",
        var code => code,
    };

    /// <summary>Whether <paramref name="id"/> is an ATCF storm id: two letters, two digits for the number, four for the year.</summary>
    /// <param name="id">The candidate id, in either case.</param>
    public static bool IsAtcfId(string? id) => id is not null && AtcfIdRegex().IsMatch(id);

    [GeneratedRegex("^[A-Za-z]{2}[0-9]{2}[0-9]{4}$")]
    private static partial Regex AtcfIdRegex();
}
