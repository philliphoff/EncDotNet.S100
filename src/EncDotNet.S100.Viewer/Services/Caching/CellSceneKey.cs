using System.Globalization;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.Caching;

/// <summary>
/// The key a cell's stored scene is kept under (map rendering slice 11):
/// everything that decides the cell's rendering that the viewer knows before
/// the cell is parsed.
/// </summary>
/// <remarks>
/// <para>
/// It folds the cell's files (path, size and modification time of the base
/// cell and each update, as the portrayal warm ledger does), the product, the
/// portrayal warm signature (mariner settings, display modes, catalogue
/// overrides, the portraying assemblies and Lua engine), the rest of the
/// presentation (palette, symbol and text scale, display category, hidden
/// viewing groups and planes, layer visibility), the scene mode, and the
/// builds of the assemblies that lower and store scenes.
/// </para>
/// <para>
/// A key that matched a scene the inputs no longer produce would only draw it
/// until the parse finishes: each stored layer carries the tile disk
/// namespace it was drawn under, which hashes the portrayal itself, and the
/// render after the parse replaces any layer whose namespace differs.
/// </para>
/// </remarks>
internal static class CellSceneKey
{
    private const int Version = 1;

    /// <summary>
    /// The key for <paramref name="entry"/> under <paramref name="presentation"/>,
    /// or <see langword="null"/> when the cell can't use a stored scene: its
    /// files aren't plain files on disk, or the dataset has its own viewing
    /// groups, hidden features or portrayal time.
    /// </summary>
    /// <param name="entry">The cell.</param>
    /// <param name="spec">The cell's product specification.</param>
    /// <param name="presentation">The map's presentation.</param>
    /// <param name="settings">The viewer settings.</param>
    /// <returns>The store key, or null.</returns>
    public static string? For(DatasetEntry entry, string spec, MapPresentationState presentation, ViewerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(presentation);
        ArgumentNullException.ThrowIfNull(settings);

        if (entry.ViewingGroups is not null
            || entry.HiddenFeatures is { Count: > 0 }
            || entry.PortrayalTime is not null
            || PortrayalWarmLedger.CellKey(entry) is not { } cell)
        {
            return null;
        }

        var c = CultureInfo.InvariantCulture;
        var display = presentation.EcdisDisplay;
        var sb = new StringBuilder("scene:").Append(Version.ToString(c))
            .Append("|ops:").Append(VectorSceneSerializer.FormatVersion.ToString(c))
            .Append("|store:").Append(CellSceneStore.FormatVersion.ToString(c))
            .Append("|cell:").Append(cell)
            .Append("|spec:").Append(spec)
            .Append("|warm:").Append(PortrayalWarmSignature.Of(presentation, settings))
            .Append("|palette:").Append(presentation.Palette)
            .Append("|symbolScale:").Append(presentation.SymbolScale.ToString("R", c))
            .Append("|textScale:").Append(presentation.TextScale.ToString("R", c))
            .Append("|category:").Append(display.Category)
            .Append("|halo:").Append(presentation.SafetyContourHalo ? '1' : '0')
            .Append("|mode:").Append(ChartEngine.Options.SceneMode);

        foreach (var (product, groups) in display.HiddenViewingGroups.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("|vg:").Append(product).Append('=');
            foreach (var group in groups.Order())
                sb.Append(group.ToString(c)).Append(',');
        }

        foreach (var plane in display.HiddenDisplayPlanes.Select(p => p.ToString()).Order(StringComparer.Ordinal))
            sb.Append("|plane:").Append(plane);
        foreach (var (layer, visible) in display.LayerVisibility.OrderBy(p => p.Key, StringComparer.Ordinal))
            sb.Append("|layer:").Append(layer).Append('=').Append(visible ? '1' : '0');
        foreach (var (key, value) in display.LayerOptions.OrderBy(p => p.Key, StringComparer.Ordinal))
            sb.Append("|option:").Append(key).Append('=').Append(value);

        foreach (var assembly in new[]
                 {
                     typeof(VectorSceneSerializer).Assembly,
                     typeof(CellSceneStore).Assembly,
                     typeof(ChartEngine).Assembly,
                 })
        {
            sb.Append("|asm:").Append(assembly.ManifestModule.ModuleVersionId);
        }

        return CellSceneStore.KeyFor(sb.ToString());
    }
}
