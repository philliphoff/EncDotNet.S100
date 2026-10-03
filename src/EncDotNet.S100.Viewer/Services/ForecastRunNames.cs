using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Names the forecast run a loaded dataset belongs to from its file name
/// (#685): NOAA writes the run time into S-111 and S-104 names, e.g.
/// <c>111US00_CBOFS_20260930T18Z_US4VA1DD</c> → "cbofs 18:00Z" and
/// <c>104US004SC1BO_20251217T12Z</c> → "S-104 12:00Z".
/// </summary>
internal static partial class ForecastRunNames
{
    /// <summary>The run a dataset name carries, as "model HH:mmZ", or <see langword="null"/> when it carries none.</summary>
    public static string? Describe(string? name)
    {
        if (string.IsNullOrEmpty(name) || RunPattern().Match(name) is not { Success: true } match)
            return null;
        if (!DateTime.TryParseExact(match.Groups["run"].Value, "yyyyMMdd'T'HH", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var run))
        {
            return null;
        }

        var model = match.Groups["model"].Success
            ? match.Groups["model"].Value.ToLowerInvariant()
            : "S-" + match.Groups["product"].Value;
        return $"{model} {run.ToString("HH:mm", CultureInfo.InvariantCulture)}Z";
    }

    /// <summary>Product number, producer code, an optional <c>_MODEL</c> (or a tile code), then <c>_yyyyMMddTHHZ</c>.</summary>
    /// <summary>The run time in a forecast file's name, or null when it carries none.</summary>
    /// <param name="name">A dataset name, e.g. <c>111US00_CBOFS_20260930T18Z_US4VA1DD</c>.</param>
    public static DateTime? RunTime(string? name) =>
        !string.IsNullOrEmpty(name)
        && RunPattern().Match(name) is { Success: true } match
        && DateTime.TryParseExact(match.Groups["run"].Value, "yyyyMMdd'T'HH", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var run)
            ? run
            : null;

    /// <summary>
    /// The forecast run a loaded dataset belongs to (#720): the model from its
    /// name (a tile's, a run's, or S-111's own), and the run time from, in
    /// order, the name, the Library's run for the loaded copy, or the data's
    /// issue time (later than the run; <see cref="ForecastRun.FromIssueTime"/>).
    /// <see langword="null"/> for data that is not a forecast run.
    /// </summary>
    /// <param name="dataset">The loaded dataset.</param>
    /// <param name="library">What the Library knows (its loaded copies carry their run), if anything.</param>
    public static ForecastRun? RunOf(EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimedDataset dataset, IReadOnlyList<LibraryTimedEntry>? library = null)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        var model = ModelAndTile(dataset.Name)?.Model
            ?? (Describe(dataset.Name) is { } described ? described[..described.IndexOf(' ', StringComparison.Ordinal)] : null)
            ?? (string.Equals(dataset.ProductSpec, "S-111", StringComparison.OrdinalIgnoreCase) ? "S-111" : null);
        if (model is null)
            return null;
        if (RunTime(dataset.Name) is { } named)
            return new ForecastRun(model, named, FromIssueTime: false);
        var key = LibraryTimedEntry.KeyOf(dataset.Name);
        if (library?.FirstOrDefault(e => e.State == LibraryTimedState.Loaded && e.Run is not null
                && string.Equals(e.MatchKey, key, StringComparison.OrdinalIgnoreCase)) is { Run: { } known })
        {
            return new ForecastRun(model, known, FromIssueTime: false);
        }
        return dataset.IssueTime is { } issued ? new ForecastRun(model, issued, FromIssueTime: true) : null;
    }

    /// <summary>"cbofs 12:00Z": <see cref="RunOf"/> as the Timeline lists runs.</summary>
    public static string? Describe(EncDotNet.S100.Renderers.Mapsui.MapsuiMapTimedDataset dataset, IReadOnlyList<LibraryTimedEntry>? library = null) =>
        RunOf(dataset, library) is { } run ? $"{run.Model} {run.Time.ToString("HH:mm", CultureInfo.InvariantCulture)}Z" : null;

    /// <summary>
    /// The model and tile a tiled forecast's name carries, with or without
    /// its run (#710): <c>111US00_CBOFS_US4MD1DD</c> → ("cbofs", "US4MD1DD");
    /// <see langword="null"/> for other names.
    /// </summary>
    /// <param name="name">A dataset name.</param>
    public static (string Model, string Tile)? ModelAndTile(string? name) =>
        !string.IsNullOrEmpty(name) && TilePattern().Match(name) is { Success: true } match
            ? (match.Groups["model"].Value.ToLowerInvariant(), match.Groups["tile"].Value)
            : null;

    [GeneratedRegex(@"^\d{3}[A-Z0-9]{4}_(?<model>[A-Z][A-Z0-9]+)_(?:\d{8}T\d{2}Z_)?(?<tile>[A-Z0-9]+)$", RegexOptions.CultureInvariant)]
    private static partial Regex TilePattern();

    [GeneratedRegex(@"^(?<product>\d{3})[A-Z0-9]{4}(?:_(?<model>[A-Z][A-Z0-9_]*?)|[A-Z0-9]*?)_(?<run>\d{8}T\d{2})Z", RegexOptions.CultureInvariant)]
    private static partial Regex RunPattern();
}

/// <summary>A loaded dataset's forecast run (#720).</summary>
/// <param name="Model">The model, e.g. <c>cbofs</c> (or <c>S-104</c> / <c>S-111</c> when the name has none).</param>
/// <param name="Time">The run time (UTC).</param>
/// <param name="FromIssueTime">
/// True when only the data's issue time was known: it is later than the run
/// itself, so forecast hours are not counted from it.
/// </param>
internal readonly record struct ForecastRun(string Model, DateTime Time, bool FromIssueTime);
