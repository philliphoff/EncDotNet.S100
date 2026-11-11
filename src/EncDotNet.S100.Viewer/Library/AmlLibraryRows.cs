using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// How the Library lists NATO Additional Military Layers cells (#1370,
/// decisions 4–6): the AML filter's products, whether a cell is drawn, and
/// the note at the top of its details.
/// </summary>
internal static class AmlLibraryRows
{
    /// <summary>The AML filter's entry for every AML 3.0 cell, whatever its product.</summary>
    public const string Aml30 = "3.0";

    /// <summary>The AML filter menu's products, after "All products" (decision 5).</summary>
    public static IReadOnlyList<string> FilterProducts { get; } = ["CLB", "ESB", "LBO", "SBO", "MFF", "RAL", Aml30];

    /// <summary>
    /// Whether SoundCharts draws <paramref name="info"/>'s cells. This is the
    /// one place the Library asks; it follows the AML portrayal the pipelines
    /// use (#1373), so the details never say "doesn't draw it" for a drawn cell.
    /// </summary>
    /// <param name="info">The cell's AML facts.</param>
    public static bool IsDrawn(AmlDatasetInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return S100Products.AmlPortrayal.Draws;
    }

    /// <summary>The note at the top of an AML item's details: "Military overlay data, not a chart." and, while not drawn, that SoundCharts doesn't draw it.</summary>
    /// <param name="info">The cell's AML facts.</param>
    /// <param name="drawn">Whether SoundCharts draws it.</param>
    public static string ItemNote(AmlDatasetInfo info, bool drawn)
    {
        ArgumentNullException.ThrowIfNull(info);
        return drawn ? Strings.Aml_ItemNoteDrawn : Strings.Aml_ItemNote;
    }

    /// <summary>
    /// Whether a cell passes the AML filter's product: <see langword="null"/>
    /// for every product, <see cref="Aml30"/> for every AML 3.0 cell, else a
    /// product code (a 3.0 cell named for that product passes too).
    /// </summary>
    /// <param name="info">The cell's AML facts.</param>
    /// <param name="product">The chosen product.</param>
    public static bool MatchesProduct(AmlDatasetInfo info, string? product)
    {
        ArgumentNullException.ThrowIfNull(info);
        return product is null
            || (product == Aml30
                ? info.IsAml30
                : string.Equals(info.ProductCode, product, StringComparison.OrdinalIgnoreCase));
    }
}
