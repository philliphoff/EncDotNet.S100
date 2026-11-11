using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The localized note for a product whose specification defines no portrayal
/// (an <see cref="PortrayalBasis.ApplicationBasis"/> on its registration), such as
/// "SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology" (issue #937).
/// </summary>
internal static class PortrayalNoteText
{
    /// <summary>
    /// Returns the note for <paramref name="productSpec"/>, or
    /// <see langword="null"/> when the product is drawn from its official
    /// portrayal catalogue.
    /// </summary>
    public static string? For(string? productSpec) =>
        ProductRegistry.Default.PortrayalOf(productSpec).AsApplication is { } portrayal
            ? string.Format(CultureInfo.CurrentCulture, Strings.Portrayal_ApplicationNote, portrayal.Basis)
            : null;
}
