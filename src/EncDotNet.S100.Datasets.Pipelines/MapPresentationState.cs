using System.Collections.Frozen;

using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Immutable, renderer-neutral snapshot of the presentation choices shared by
/// every dataset on an S-100 map.
/// </summary>
/// <remarks>
/// <para>
/// The ECDIS snapshot includes the cross-product category, viewing-group and
/// display-plane overrides, and product-specific S-100 Part 9 §11.7 display
/// modes. Collection values are defensively copied so later mutations of the
/// source settings cannot affect an in-flight render.
/// </para>
/// <para>
/// Viewport, time-step, basemap, and output-filter choices remain on
/// <see cref="RenderContext"/> because they vary by dataset or render request.
/// </para>
/// </remarks>
public sealed class MapPresentationState
{
    /// <summary>
    /// The standard Day-palette presentation with default scales, ECDIS
    /// settings, and mariner selections.
    /// </summary>
    public static MapPresentationState Default { get; } = new(
        PaletteType.Day,
        1.0,
        1.0,
        new EcdisDisplaySettings(),
        MarinerSettings.Default);

    /// <summary>
    /// Creates an immutable map presentation snapshot.
    /// </summary>
    /// <param name="palette">The S-100 colour palette.</param>
    /// <param name="symbolScale">Global symbol scale factor.</param>
    /// <param name="textScale">Global text scale factor.</param>
    /// <param name="ecdisDisplay">
    /// Cross-product ECDIS settings, including product-specific display modes.
    /// </param>
    /// <param name="mariner">Mariner-configurable portrayal settings.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="ecdisDisplay"/> or <paramref name="mariner"/> is
    /// <c>null</c>.
    /// </exception>
    public MapPresentationState(
        PaletteType palette,
        double symbolScale,
        double textScale,
        EcdisDisplaySettings ecdisDisplay,
        MarinerSettings mariner)
    {
        ArgumentNullException.ThrowIfNull(ecdisDisplay);
        ArgumentNullException.ThrowIfNull(mariner);

        Palette = palette;
        SymbolScale = symbolScale;
        TextScale = textScale;
        EcdisDisplay = Snapshot(ecdisDisplay);
        Mariner = mariner;
    }

    /// <summary>The S-100 colour palette applied across the map.</summary>
    public PaletteType Palette { get; }

    /// <summary>Global symbol scale factor, where <c>1.0</c> is the default.</summary>
    public double SymbolScale { get; }

    /// <summary>Global text scale factor, where <c>1.0</c> is the default.</summary>
    public double TextScale { get; }

    /// <summary>
    /// Cross-product ECDIS settings, including product-specific S-100 Part 9
    /// §11.7 display-mode selections.
    /// </summary>
    public EcdisDisplaySettings EcdisDisplay { get; }

    /// <summary>Mariner-configurable portrayal settings.</summary>
    public MarinerSettings Mariner { get; }

    /// <summary>
    /// Applies this map-wide snapshot to a dataset-specific render context.
    /// </summary>
    /// <param name="context">
    /// The context carrying dataset- or request-specific choices to preserve.
    /// </param>
    /// <param name="portrayalSpec">
    /// The specification whose portrayal catalogue processes the dataset.
    /// </param>
    /// <returns>
    /// A copy of <paramref name="context"/> containing this presentation and the
    /// product-specific display mode selected for
    /// <paramref name="portrayalSpec"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> is <c>null</c>.
    /// </exception>
    public RenderContext ApplyTo(RenderContext context, SpecRef portrayalSpec)
    {
        ArgumentNullException.ThrowIfNull(context);

        var applied = context with
        {
            Palette = Palette,
            SymbolScale = SymbolScale,
            TextScale = TextScale,
            EcdisDisplay = EcdisDisplay,
            Mariner = Mariner,
        };

        var displayModeId = EcdisDisplay.ActiveDisplayModes.GetValueOrDefault(
            portrayalSpec.Name);
        return string.IsNullOrEmpty(displayModeId)
            ? applied
            : applied with { DisplayModeId = displayModeId };
    }

    private static EcdisDisplaySettings Snapshot(EcdisDisplaySettings settings)
    {
        var hiddenViewingGroups = settings.HiddenViewingGroups.ToFrozenDictionary(
            pair => pair.Key,
            pair => (IReadOnlySet<int>)pair.Value.ToFrozenSet(),
            StringComparer.OrdinalIgnoreCase);

        return settings with
        {
            HiddenViewingGroups = hiddenViewingGroups,
            HiddenDisplayPlanes = settings.HiddenDisplayPlanes.ToFrozenSet(),
            ActiveDisplayModes = settings.ActiveDisplayModes.ToFrozenDictionary(
                StringComparer.OrdinalIgnoreCase),
        };
    }
}
