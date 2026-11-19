namespace EncDotNet.S100.Cli.Web;

/// <summary>The basemap under the chart (handoff D17, Q4).</summary>
public enum Basemap
{
    /// <summary>OpenStreetMap, greyed and softened so the chart reads first (the default).</summary>
    Muted,

    /// <summary>OpenStreetMap in its own colours.</summary>
    OpenStreetMap,

    /// <summary>No basemap: the chart only.</summary>
    None,
}

/// <summary>A choice in the Display panel's Layers list: a layer, or every dataset (handoff D17).</summary>
/// <param name="Id">The layer's id; <see langword="null"/> for all layers.</param>
/// <param name="Label">What the list shows: the id, or "All layers".</param>
/// <param name="Count">The datasets in it.</param>
/// <param name="Products">Its products, in order, for "3 · S-101".</param>
public sealed record ServeLayerChoice(string? Id, string Label, int Count, IReadOnlyList<string> Products);

/// <summary>What the Display panel works out (handoff D17, D18, D36).</summary>
internal static class ServeDisplay
{
    /// <summary>The hash parameter of the chart palette, left out for the default palette.</summary>
    public const string PaletteParameter = "palette";

    /// <summary>The hash parameter of the layer, left out for all layers.</summary>
    public const string LayerParameter = "layer";

    /// <summary>
    /// "All layers" and each layer with the datasets in it, or nothing when
    /// the datasets aren't split into layers.
    /// </summary>
    public static IReadOnlyList<ServeLayerChoice> LayerChoices(IReadOnlyList<ServedDataset> datasets, IReadOnlyList<string> layers)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
            return [];
        var choices = new List<ServeLayerChoice> { new(null, "All layers", datasets.Count, Products(datasets)) };
        foreach (var layer in layers)
        {
            var inLayer = datasets.Where(d => string.Equals(d.Layer, layer, StringComparison.Ordinal)).ToList();
            choices.Add(new ServeLayerChoice(layer, layer, inLayer.Count, Products(inLayer)));
        }

        return choices;
    }

    /// <summary>
    /// The TileJSON of a layer, palette and time, relative to the page:
    /// <c>layers/{layer}/{palette}/tiles.json?t=…</c>, with the default
    /// palette and all layers at the root.
    /// </summary>
    public static string TileJsonUrl(string? layer, string? palette, string? defaultPalette, string? time)
    {
        var url = layer is null ? "" : TileServerLayersPath + "/" + Uri.EscapeDataString(layer) + "/";
        if (palette is not null && !string.Equals(palette, defaultPalette, StringComparison.Ordinal))
            url += Uri.EscapeDataString(palette) + "/";
        url += "tiles.json";
        return time is null ? url : url + "?t=" + Uri.EscapeDataString(time);
    }

    /// <summary>The palette a hash asks for, when it's one served; otherwise <see langword="null"/>.</summary>
    public static string? PaletteFromHash(IReadOnlyDictionary<string, string> parameters, IReadOnlyList<string> palettes)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(palettes);
        return parameters.TryGetValue(PaletteParameter, out var value)
            ? palettes.FirstOrDefault(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    /// <summary>The layer a hash asks for, when it's one served; otherwise <see langword="null"/> (all layers).</summary>
    public static string? LayerFromHash(IReadOnlyDictionary<string, string> parameters, IReadOnlyList<string> layers)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(layers);
        return parameters.TryGetValue(LayerParameter, out var value)
            ? layers.FirstOrDefault(l => string.Equals(l, value, StringComparison.Ordinal))
            : null;
    }

    /// <summary>Writes the palette and layer into the hash's parameters, leaving defaults out (D36).</summary>
    public static void WriteHash(IDictionary<string, string> parameters, string? palette, string? defaultPalette, string? layer)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (palette is null || string.Equals(palette, defaultPalette, StringComparison.Ordinal))
            parameters.Remove(PaletteParameter);
        else
            parameters[PaletteParameter] = palette;
        if (layer is null)
            parameters.Remove(LayerParameter);
        else
            parameters[LayerParameter] = layer;
    }

    /// <summary>The note under the palette: what it does to the page's chrome (D18).</summary>
    public static string PaletteNote(string? palette, PageAppearance page) => ServeTheme.Resolve(palette, page) switch
    {
        "night" => "Night also dims this page for the bridge.",
        "dark" when IsDusk(palette) => "Dusk also darkens this page.",
        "light" when IsDusk(palette) => "Page: Light keeps this page light.",
        "auto" => "Chrome follows your browser.",
        _ => "Chrome follows the Page setting.",
    };

    /// <summary>The note under Page (D18): Night wins over the setting.</summary>
    public static string PageNote(string? palette) =>
        string.Equals(palette, "night", StringComparison.OrdinalIgnoreCase)
            ? "The Night palette keeps night chrome, whatever this says."
            : "Auto follows your browser and the chart palette.";

    /// <summary>The value the JS module takes for a basemap.</summary>
    public static string ScriptName(Basemap basemap) => basemap switch
    {
        Basemap.OpenStreetMap => "osm",
        Basemap.None => "none",
        _ => "muted",
    };

    private const string TileServerLayersPath = EncDotNet.S100.Cli.Infrastructure.Tiles.TileServer.LayersPath;

    private static bool IsDusk(string? palette) => string.Equals(palette, "dusk", StringComparison.OrdinalIgnoreCase);

    private static string[] Products(IEnumerable<ServedDataset> datasets) =>
        [.. datasets.Select(d => d.Product).Where(p => p.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
}
