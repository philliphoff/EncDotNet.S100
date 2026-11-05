using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>The kinds of NHC GIS zip an advisory is read from.</summary>
public enum NhcFileKind
{
    /// <summary>The forecast track and cone, <c>{ATCF}_5day_{adv}.zip</c>: cone polygon, track line and forecast points.</summary>
    Forecast,

    /// <summary>The forecast wind radii, <c>{ATCF}_fcst_{adv}.zip</c>: 34, 50 and 64 kt radii per forecast time.</summary>
    WindRadii,

    /// <summary>The best track, <c>{ATCF}_best_track.zip</c>: past points, line, observed radii and wind swath.</summary>
    BestTrack,
}

/// <summary>
/// The NHC GIS zips of one advisory: the forecast zip, which the advisory is
/// named after, and the wind radii and best track zips beside it when they
/// are there.
/// </summary>
/// <remarks>
/// NHC names the files after the storm's ATCF id in lower case and the
/// advisory number in three digits, with a letter for an intermediate
/// advisory: <c>al092022_5day_020.zip</c>, <c>al092022_fcst_020.zip</c>,
/// <c>al092022_best_track.zip</c>. The "latest" copies NHC also serves
/// (<c>al092022_5day_latest.zip</c>) are recognised too. Names are matched
/// ignoring case.
/// </remarks>
/// <param name="ForecastZip">The path of the <c>_5day_</c> zip.</param>
/// <param name="WindRadiiZip">The path of the matching <c>_fcst_</c> zip, or <see langword="null"/>.</param>
/// <param name="BestTrackZip">The path of the storm's <c>_best_track</c> zip, or <see langword="null"/>.</param>
public sealed partial record NhcAdvisoryFiles(string ForecastZip, string? WindRadiiZip, string? BestTrackZip)
{
    /// <summary>
    /// Finds the zips of an advisory from <paramref name="path"/>: a
    /// <c>_5day_</c> zip, whose siblings are looked for in its folder, or a
    /// folder holding one storm's zips, whose latest advisory is taken.
    /// </summary>
    /// <param name="path">A forecast zip or a folder.</param>
    /// <returns>The files, or <see langword="null"/> when <paramref name="path"/> is neither.</returns>
    public static NhcAdvisoryFiles? Find(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (Directory.Exists(path))
            return FindInFolder(path);
        if (!File.Exists(path) || !TryParseFileName(Path.GetFileName(path), out var kind, out var atcfId, out var advisory) || kind != NhcFileKind.Forecast)
            return null;

        var folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var files = SafeFiles(folder);
        return new NhcAdvisoryFiles(
            path,
            Sibling(files, WindRadiiZipName(atcfId, advisory)),
            Sibling(files, BestTrackZipName(atcfId)));
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a folder holding at least one NHC
    /// forecast zip, all for one storm.
    /// </summary>
    /// <param name="path">The folder.</param>
    public static bool IsAdvisoryFolder(string path) =>
        !string.IsNullOrEmpty(path) && Directory.Exists(path) && FindInFolder(path) is not null;

    /// <summary>
    /// Reads an NHC GIS zip's name: its kind, the storm's ATCF id (upper case)
    /// and, for the forecast and wind radii zips, the advisory as NHC writes it
    /// in the name (<c>"020"</c>, <c>"020A"</c> or <c>"latest"</c>).
    /// </summary>
    /// <param name="fileName">The file's name, without a folder.</param>
    /// <param name="kind">The kind of zip.</param>
    /// <param name="atcfId">The ATCF id, for example <c>"AL092022"</c>.</param>
    /// <param name="advisory">The advisory; empty for a best track.</param>
    /// <returns>Whether the name is an NHC advisory zip's.</returns>
    public static bool TryParseFileName(string? fileName, out NhcFileKind kind, out string atcfId, out string advisory)
    {
        kind = default;
        atcfId = string.Empty;
        advisory = string.Empty;
        if (string.IsNullOrEmpty(fileName))
            return false;

        if (AdvisoryZipRegex().Match(fileName) is { Success: true } m)
        {
            kind = string.Equals(m.Groups["kind"].Value, "5day", StringComparison.OrdinalIgnoreCase) ? NhcFileKind.Forecast : NhcFileKind.WindRadii;
            atcfId = m.Groups["id"].Value.ToUpperInvariant();
            advisory = m.Groups["adv"].Value;
            return true;
        }

        if (BestTrackZipRegex().Match(fileName) is { Success: true } b)
        {
            kind = NhcFileKind.BestTrack;
            atcfId = b.Groups["id"].Value.ToUpperInvariant();
            return true;
        }

        return false;
    }

    /// <summary>The name NHC gives an advisory's forecast zip, for example <c>al092022_5day_020.zip</c>.</summary>
    /// <param name="atcfId">The storm's ATCF id.</param>
    /// <param name="advisory">The advisory, as in the file name (<c>"020"</c>).</param>
    public static string ForecastZipName(string atcfId, string advisory) =>
        $"{atcfId.ToLowerInvariant()}_5day_{advisory}.zip";

    /// <summary>The name NHC gives an advisory's wind radii zip, for example <c>al092022_fcst_020.zip</c>.</summary>
    /// <param name="atcfId">The storm's ATCF id.</param>
    /// <param name="advisory">The advisory, as in the file name (<c>"020"</c>).</param>
    public static string WindRadiiZipName(string atcfId, string advisory) =>
        $"{atcfId.ToLowerInvariant()}_fcst_{advisory}.zip";

    /// <summary>The name NHC gives a storm's best track zip, for example <c>al092022_best_track.zip</c>.</summary>
    /// <param name="atcfId">The storm's ATCF id.</param>
    public static string BestTrackZipName(string atcfId) =>
        $"{atcfId.ToLowerInvariant()}_best_track.zip";

    /// <summary>
    /// Orders advisories as NHC numbers them: by number, an intermediate
    /// advisory (<c>"20A"</c>) after its full one, and <c>"latest"</c> last.
    /// </summary>
    /// <param name="advisory">The advisory, with or without leading zeros.</param>
    /// <returns>A key that sorts advisories in issue order.</returns>
    public static (int Number, string Suffix) AdvisoryOrder(string advisory)
    {
        if (string.Equals(advisory, "latest", StringComparison.OrdinalIgnoreCase))
            return (int.MaxValue, string.Empty);
        var digits = advisory.TakeWhile(char.IsAsciiDigit).Count();
        var number = digits > 0 && int.TryParse(advisory.AsSpan(0, digits), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
        return (number, advisory[digits..].ToUpperInvariant());
    }

    private static NhcAdvisoryFiles? FindInFolder(string folder)
    {
        var files = SafeFiles(folder);
        var forecasts = files
            .Select(f => (Path: f, Ok: TryParseFileName(Path.GetFileName(f), out var kind, out var id, out var adv), Kind: kind, Id: id, Advisory: adv))
            .Where(f => f.Ok && f.Kind == NhcFileKind.Forecast)
            .ToArray();
        if (forecasts.Length == 0 || forecasts.Select(f => f.Id).Distinct(StringComparer.Ordinal).Count() > 1)
            return null;

        var latest = forecasts
            .OrderBy(f => AdvisoryOrder(f.Advisory).Number)
            .ThenBy(f => AdvisoryOrder(f.Advisory).Suffix, StringComparer.Ordinal)
            .Last();
        return new NhcAdvisoryFiles(
            latest.Path,
            Sibling(files, WindRadiiZipName(latest.Id, latest.Advisory)),
            Sibling(files, BestTrackZipName(latest.Id)));
    }

    private static string[] SafeFiles(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFiles(folder, "*.zip", new EnumerationOptions { MatchCasing = MatchCasing.CaseInsensitive })
                .Where(f => !Path.GetFileName(f).StartsWith('.'))
                .Order(StringComparer.Ordinal)];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? Sibling(IEnumerable<string> files, string name) =>
        files.FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^(?<id>[A-Za-z]{2}[0-9]{6})_(?<kind>5day|fcst)_(?<adv>[0-9]{3}[A-Za-z]?|latest)\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex AdvisoryZipRegex();

    [GeneratedRegex(@"^(?<id>[A-Za-z]{2}[0-9]{6})_best_track\.zip$", RegexOptions.IgnoreCase)]
    private static partial Regex BestTrackZipRegex();
}
