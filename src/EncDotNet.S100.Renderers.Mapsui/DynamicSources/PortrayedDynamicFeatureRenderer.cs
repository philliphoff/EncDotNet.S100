using EncDotNet.S100.DynamicSources;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;
using Mapsui;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Draws dynamic features with a code-defined
/// <see cref="IDynamicFeaturePortrayal"/>: the portrayal's S-100 Part 9
/// drawing instructions are lowered by the same scene builder a vector
/// product uses, into self-styled Mapsui features (symbol images and labels).
/// </summary>
/// <remarks>
/// <para>
/// This is the bridge from the portrayal toolkit
/// (<c>EncDotNet.S100.Portrayals.Application</c>) to the dynamic-source
/// overlay host. Symbols take their colours from the portrayal's
/// <see cref="ApplicationPalette"/> in the palette <see cref="PaletteAccessor"/>
/// returns when the feature is drawn; the host redraws a source's layer on its
/// next change, so a palette switch shows on the next update.
/// </para>
/// <para>
/// Like every <see cref="IDynamicFeatureRenderer"/> it is a pure function of
/// the feature and safe to call from any thread; processed symbols are cached
/// per palette.
/// </para>
/// </remarks>
public sealed class PortrayedDynamicFeatureRenderer : IDynamicFeatureRenderer
{
    private readonly IDynamicFeaturePortrayal _portrayal;
    private readonly MapsuiRenderAssetCache _assets = new();

    /// <summary>Creates a renderer for <paramref name="portrayal"/>.</summary>
    /// <param name="portrayal">The portrayal.</param>
    /// <param name="paletteAccessor">The palette to draw in; <see langword="null"/> for Day.</param>
    public PortrayedDynamicFeatureRenderer(IDynamicFeaturePortrayal portrayal, Func<PaletteType>? paletteAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(portrayal);
        _portrayal = portrayal;
        PaletteAccessor = paletteAccessor ?? (static () => PaletteType.Day);
    }

    /// <summary>The palette features are drawn in.</summary>
    public Func<PaletteType> PaletteAccessor { get; }

    /// <summary>The mariner's symbol scale. Defaults to 1.</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>The mariner's text scale. Defaults to 1.</summary>
    public double TextScale { get; init; } = 1.0;

    /// <inheritdoc />
    public bool CanRender(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return _portrayal.CanPortray(feature);
    }

    /// <inheritdoc />
    public IEnumerable<IFeature> Render(DynamicFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var instructions = _portrayal.Portray(feature);
        if (instructions.Count == 0)
            return [];

        var renderer = new MapsuiDisplayListRenderer
        {
            LayerName = feature.Id,
            Palette = _portrayal.Palette.GetColorPalette(PaletteAccessor()),
            SymbolProvider = _portrayal.SymbolProvider,
            AssetCache = _assets,
            SymbolScale = SymbolScale,
            TextScale = TextScale,
        };
        return renderer.RenderFeatures(instructions, new DynamicFeatureGeometryProvider([feature]));
    }
}
