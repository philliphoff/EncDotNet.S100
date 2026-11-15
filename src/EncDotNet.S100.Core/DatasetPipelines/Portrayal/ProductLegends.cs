using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>The rows of one kind of sub-layer in a product's legend.</summary>
/// <param name="Key">The sub-layer key (<c>"weather.tc.track"</c>).</param>
/// <param name="Title">The sub-layer's name, "Tropical cyclone track", "AML seabed".</param>
/// <param name="Rows">The rows, in the palette the sub-layer was drawn in.</param>
public sealed record LegendGroup(string Key, string Title, IReadOnlyList<LegendEntry> Rows);

/// <summary>
/// The legend one drawn sub-layer carries (<see cref="CoverageSubLayerBase.Legend"/>,
/// <see cref="VectorSubLayer.Legend"/>), with what places it in a product's legend.
/// </summary>
/// <param name="Spec">The spec of the portrayal it came from, or <see langword="null"/> when unknown.</param>
/// <param name="Key">The sub-layer key.</param>
/// <param name="Name">The sub-layer's display name, "Wave height: gfswave.grib2".</param>
/// <param name="Rows">The legend rows.</param>
/// <param name="Caption">The caption shown with the rows, or <see langword="null"/>.</param>
/// <param name="Title">The group title the sub-layer asks for, or <see langword="null"/> for its name without the dataset.</param>
public sealed record SubLayerLegend(string? Spec, string Key, string Name, IReadOnlyList<LegendEntry> Rows, string? Caption, string? Title)
{
    /// <summary>The legend of a coverage sub-layer, or <see langword="null"/> when it carries no rows.</summary>
    /// <param name="subLayer">The sub-layer.</param>
    /// <param name="spec">The spec of its portrayal result.</param>
    public static SubLayerLegend? Of(CoverageSubLayerBase subLayer, string? spec)
    {
        ArgumentNullException.ThrowIfNull(subLayer);
        return subLayer.Legend is { Count: > 0 } rows
            ? new SubLayerLegend(spec, subLayer.LayerKey, subLayer.LayerName, rows, subLayer.LegendCaption, Title: null)
            : null;
    }

    /// <summary>The legend of a vector sub-layer, or <see langword="null"/> when it carries no rows.</summary>
    /// <param name="subLayer">The sub-layer.</param>
    /// <param name="spec">The spec of its portrayal result.</param>
    public static SubLayerLegend? Of(VectorSubLayer subLayer, string? spec)
    {
        ArgumentNullException.ThrowIfNull(subLayer);
        return subLayer.Legend is { Count: > 0 } rows
            ? new SubLayerLegend(spec, subLayer.LayerKey, subLayer.LayerName, rows, subLayer.LegendCaption, subLayer.LegendTitle)
            : null;
    }
}

/// <summary>
/// The legend of one product or overlay: seven S-102 tiles make one
/// product legend, with one group per kind of sub-layer, top of the stack first.
/// </summary>
/// <param name="Id">The legend's stable id (<see cref="ProductLegends.SectionId"/>).</param>
/// <param name="Spec">The spec the datasets load under (<c>"S-111"</c>, <c>"GRIB2"</c>).</param>
/// <param name="Title">The product's name: "Surface currents", "Wave height".</param>
/// <param name="Caption">The topmost group's caption, or <see langword="null"/>.</param>
/// <param name="Groups">The groups, top of the stack first.</param>
public sealed record ProductLegend(string Id, string Spec, string Title, string? Caption, IReadOnlyList<LegendGroup> Groups)
{
    /// <summary>Every row of every group, in order.</summary>
    public IEnumerable<LegendEntry> Rows => Groups.SelectMany(g => g.Rows);
}

/// <summary>
/// Gathers the legends of drawn sub-layers into one <see cref="ProductLegend"/>
/// per product or overlay (legends design note D1, D4). Shared by the
/// viewer's Legend tab and the legend graphics of <c>s100 tiles serve</c>, so
/// both show the same keys.
/// </summary>
public static class ProductLegends
{
    /// <summary>
    /// The legend a sub-layer belongs to: its product, or for a weather grid
    /// (one spec for every field) the field's sub-layer key.
    /// </summary>
    /// <param name="spec">The spec the dataset loads under.</param>
    /// <param name="layerKey">The sub-layer's key.</param>
    public static string SectionId(string spec, string layerKey)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(layerKey);
        return IsWeatherGrid(spec, layerKey) ? layerKey : spec;
    }

    private static bool IsWeatherGrid(string spec, string layerKey) =>
        layerKey.StartsWith("weather.", StringComparison.Ordinal)
        && !string.Equals(spec, SpecName.TropicalCyclone, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(spec, SpecName.Codsus, StringComparison.OrdinalIgnoreCase);

    /// <summary>The legend's name: the product's ("Surface currents"), or a weather field's ("Wave height").</summary>
    /// <param name="spec">The spec the dataset loads under.</param>
    /// <param name="layerKey">The sub-layer's key.</param>
    /// <param name="layerName">The sub-layer's name, "Wave height: gfswave.grib2".</param>
    /// <param name="productTitle">Names a product by its spec, or <see langword="null"/> for <see cref="ProductTitle"/>.</param>
    public static string SectionTitle(string spec, string layerKey, string layerName, Func<string, string?>? productTitle = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(layerKey);
        ArgumentNullException.ThrowIfNull(layerName);
        if (!IsWeatherGrid(spec, layerKey))
            return (productTitle ?? ProductTitle)(spec) ?? spec;
        var name = GroupTitle(layerName);
        return name.Length > 0 ? name : spec;
    }

    /// <summary>A product's name in English ("Bathymetric surface"), or <see langword="null"/> for a spec without one.</summary>
    /// <param name="spec">The spec.</param>
    public static string? ProductTitle(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        return spec.ToUpperInvariant() switch
        {
            "S-101" or "S-57" => "Electronic navigational chart",
            "S-102" => "Bathymetric surface",
            "S-104" => "Water level",
            "S-111" => "Surface currents",
            "S-122" => "Marine protected areas",
            "S-124" => "Navigational warnings",
            "S-129" => "Under keel clearance management",
            "S-411" => "Sea ice",
            "S-412" => "Weather warnings",
            "S-413" => "Weather and wave",
            "TC" => "Tropical cyclones",
            "CODSUS" => "Surface analysis",
            "AML" => "Additional Military Layers",
            _ => null,
        };
    }

    /// <summary>A sub-layer's name without its dataset: "Wave height: gfswave.grib2" → "Wave height".</summary>
    /// <param name="layerName">The sub-layer's name.</param>
    public static string GroupTitle(string layerName)
    {
        ArgumentNullException.ThrowIfNull(layerName);
        var colon = layerName.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 ? layerName[..colon] : layerName;
    }

    /// <summary>
    /// Gathers sub-layer legends, top of the stack first, into one legend per
    /// product, in the order each product first appears. A group's rows come
    /// from its topmost sub-layer, and a legend's caption from its topmost group.
    /// </summary>
    /// <param name="subLayers">The sub-layer legends, top of the stack first.</param>
    /// <param name="productTitle">Names a product by its spec, or <see langword="null"/> for <see cref="ProductTitle"/>.</param>
    public static IReadOnlyList<ProductLegend> Build(IEnumerable<SubLayerLegend> subLayers, Func<string, string?>? productTitle = null)
    {
        ArgumentNullException.ThrowIfNull(subLayers);
        var byId = new Dictionary<string, Builder>(StringComparer.Ordinal);
        var order = new List<Builder>();
        foreach (var sub in subLayers)
        {
            var spec = sub.Spec ?? string.Empty;
            var id = SectionId(spec, sub.Key);
            if (!byId.TryGetValue(id, out var builder))
            {
                builder = new Builder(id, spec, SectionTitle(spec, sub.Key, sub.Name, productTitle));
                byId.Add(id, builder);
                order.Add(builder);
            }

            builder.Groups.Add(sub);
        }

        return [.. order.Select(b => new ProductLegend(b.Id, b.Spec, b.Title, b.Groups.Caption, [.. b.Groups.Groups]))];
    }

    private sealed class Builder(string id, string spec, string title)
    {
        public string Id { get; } = id;

        public string Spec { get; } = spec;

        public string Title { get; } = title;

        public LegendGroupSet Groups { get; } = new();
    }
}

/// <summary>
/// The groups of one product's legend as sub-layers are added top of the
/// stack first: one group per sub-layer key, from its topmost sub-layer, and
/// the caption of the topmost group.
/// </summary>
public sealed class LegendGroupSet
{
    private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
    private readonly List<LegendGroup> _groups = [];

    /// <summary>The groups, top of the stack first.</summary>
    public IReadOnlyList<LegendGroup> Groups => _groups;

    /// <summary>The topmost group's caption, or <see langword="null"/>.</summary>
    public string? Caption { get; private set; }

    /// <summary>Adds a sub-layer's legend; one with a key already added is ignored.</summary>
    /// <param name="sub">The sub-layer legend.</param>
    /// <returns>Whether it made a new group.</returns>
    public bool Add(SubLayerLegend sub)
    {
        ArgumentNullException.ThrowIfNull(sub);
        if (!_keys.Add(sub.Key))
            return false;
        // The caption is the topmost group's (D4): its first sub-layer is the top one.
        if (_groups.Count == 0)
            Caption = sub.Caption;
        _groups.Add(new LegendGroup(sub.Key, sub.Title ?? ProductLegends.GroupTitle(sub.Name), sub.Rows));
        return true;
    }
}
