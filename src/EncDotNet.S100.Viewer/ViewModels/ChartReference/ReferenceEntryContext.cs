using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// What an entry view (R7, #1650) reads from its host: the Reference tab, or
/// the study page's entry pane. The two differ in palette, symbol style,
/// where a link goes and whether there is a map.
/// </summary>
internal sealed class ReferenceEntryContext
{
    /// <summary>The index the entry belongs to.</summary>
    public required ChartReferenceIndex Index { get; init; }

    /// <summary>The product's artwork reader; <see langword="null"/> draws no glyphs.</summary>
    public ChartReferenceArtwork? Artwork { get; init; }

    /// <summary>The glyph cache, whose palette the large glyph is drawn in.</summary>
    public required ChartReferenceGlyphCache Glyphs { get; init; }

    /// <summary>Whether a feature type leads with its simplified symbol rather than its paper-chart one.</summary>
    public bool SimplifiedSymbols { get; init; }

    /// <summary>Opens the entry a link names: in the Reference tab, or on the page.</summary>
    public required Action<ReferenceKey> Open { get; init; }

    /// <summary>Runs an action on the UI thread; by default at once.</summary>
    public Action<Action> Post { get; init; } = static a => a();

    /// <summary>The map, for In view and Find; <see langword="null"/> where there is none (the page).</summary>
    public IChartReferenceMap? Map { get; init; }

    /// <summary>The viewer settings, for a display setting's current value; <see langword="null"/> for none.</summary>
    public ViewerSettingsService? Settings { get; init; }

    /// <summary>Opens the settings that change a display setting; <see langword="null"/> hides Change.</summary>
    public Action<string>? ChangeSetting { get; init; }
}
