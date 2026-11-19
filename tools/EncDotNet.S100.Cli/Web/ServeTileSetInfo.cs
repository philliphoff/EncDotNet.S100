using System.Globalization;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// What the Tile set panel (issue #1584, D35) says about a built tile set:
/// its tags and the text of each field.
/// </summary>
internal static class ServeTileSetInfo
{
    /// <summary>The toast a click on the map shows, since a tile set has nothing to pick (D35).</summary>
    public const string NoPickTitle = "No feature information in a tile set";

    /// <summary>The toast's second line.</summary>
    public const string NoPickDetail = "Serve the datasets to pick features.";

    /// <summary>The tags under the name: the container, the format and, when served, "@2x available".</summary>
    public static IReadOnlyList<string> Tags(ServedTileSet tileSet, BuiltTileSet built)
    {
        ArgumentNullException.ThrowIfNull(tileSet);
        ArgumentNullException.ThrowIfNull(built);
        List<string> tags = [built.Container, FormatName(tileSet.Format)];
        if (tileSet.HighDpi)
            tags.Add("@2x available");
        return tags;
    }

    /// <summary>"PNG, 256 px tiles".</summary>
    public static string Format(ServedTileSet tileSet, BuiltTileSet built) =>
        FormatName(tileSet.Format) + ", " + built.TileSize.ToString(CultureInfo.InvariantCulture) + " px tiles";

    /// <summary>"512 px @2x tiles", or "No".</summary>
    public static string HighDpi(ServedTileSet tileSet) => tileSet.HighDpi ? "512 px @2x tiles" : "No";

    /// <summary>"13 – 16", or "13" for one zoom.</summary>
    public static string Zoom(ServedTileSet tileSet) =>
        tileSet.MinZoom == tileSet.MaxZoom
            ? tileSet.MinZoom.ToString(CultureInfo.InvariantCulture)
            : tileSet.MinZoom.ToString(CultureInfo.InvariantCulture) + " – " + tileSet.MaxZoom.ToString(CultureInfo.InvariantCulture);

    /// <summary>The bounds in degrees and minutes, latitudes then longitudes; <see langword="null"/> when unknown.</summary>
    public static (string Latitudes, string Longitudes)? Bounds(ServedTileSet tileSet) =>
        tileSet.Bounds is [var west, var south, var east, var north]
            ? (ServeDatasetInfo.DegreesMinutes(south, 'N', 'S', 2) + " – " + ServeDatasetInfo.DegreesMinutes(north, 'N', 'S', 2),
               ServeDatasetInfo.DegreesMinutes(west, 'E', 'W', 3) + " – " + ServeDatasetInfo.DegreesMinutes(east, 'E', 'W', 3))
            : null;

    /// <summary>"50°43.5′N 001°10.2′W"; <see langword="null"/> when unknown.</summary>
    public static string? Centre(ServedTileSet tileSet) =>
        tileSet.Center is [var longitude, var latitude, ..]
            ? ServeDatasetInfo.DegreesMinutes(latitude, 'N', 'S', 2) + " " + ServeDatasetInfo.DegreesMinutes(longitude, 'E', 'W', 3)
            : null;

    /// <summary>
    /// "s100 1.2.0 · day palette · 2021-04-01 10:00 UTC", from the TileJSON's
    /// <c>s100</c> block; <see langword="null"/> when it has none.
    /// </summary>
    public static string? MadeBy(BuiltTileSet built)
    {
        ArgumentNullException.ThrowIfNull(built);
        if (built.Generator is null)
            return null;

        // The informational version's build metadata (+commit) says nothing to a reader.
        var plus = built.Generator.IndexOf('+', StringComparison.Ordinal);
        var parts = new List<string> { plus > 0 ? built.Generator[..plus] : built.Generator };
        if (built.Palette is { } palette)
            parts.Add(palette + " palette");
        if (built.Time is { } time)
            parts.Add(time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC");
        return string.Join(" · ", parts);
    }

    private static string FormatName(string format) => format.ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "JPEG",
        "webp" => "WebP",
        var other => other.ToUpperInvariant(),
    };
}

/// <summary>What the Datasets panel says when no datasets are left (issue #1584, D32).</summary>
internal static class ServeEmptyState
{
    /// <summary>
    /// The command that adds a dataset to the server at <paramref name="baseUri"/>
    /// (with its token, when it has one), with a placeholder path.
    /// </summary>
    public static string AddCommand(string baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        return "curl -X POST " + baseUri + Infrastructure.Tiles.ServedDatasets.Path
            + " -H \"Content-Type: application/json\" -d '{\"path\": \"/path/to/dataset\"}'";
    }
}
