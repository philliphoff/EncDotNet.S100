using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Text for the Catalogues entry points outside the activity (catalogue-panels
/// D10, §6.4–6.5, CR3): the dataset inspector's <b>Drawn with</b> line and
/// Help › About's table. Read from <see cref="CatalogueRegistry"/>.
/// </summary>
internal static class CatalogueEntryPointText
{
    /// <summary>
    /// The product whose Catalogues row describes <paramref name="productSpec"/>'s
    /// datasets: the product itself when the registry knows it, otherwise the
    /// product that portrays it (S-101 for an S-57 cell).
    /// </summary>
    /// <param name="registry">The catalogues in effect.</param>
    /// <param name="productSpec">The dataset's product, e.g. <c>"S-57"</c>.</param>
    /// <param name="portrayalSpec">The product whose portrayal draws it, e.g. <c>"S-101"</c>.</param>
    public static string CatalogueProduct(CatalogueRegistry? registry, string productSpec, string portrayalSpec) =>
        registry is null || registry.Find(productSpec) is not null || registry.Find(portrayalSpec) is null
            ? productSpec
            : portrayalSpec;

    /// <summary>
    /// "Feature 2.0.0 · Portrayal 2.1.0-draft (custom)", "Feature 1.1.0 · Drawn
    /// by SoundCharts", or "S-101 Portrayal 2.0.0" for an S-57 cell; null when
    /// the registry knows neither product.
    /// </summary>
    /// <param name="registry">The catalogues in effect.</param>
    /// <param name="productSpec">The dataset's product.</param>
    /// <param name="portrayalSpec">The product whose portrayal catalogue draws it.</param>
    public static string? DrawnWith(CatalogueRegistry registry, string productSpec, string portrayalSpec)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var product = registry.Find(productSpec);
        var portrayalProduct = Same(productSpec, portrayalSpec) ? product : registry.Find(portrayalSpec);

        var parts = new List<string>(2);
        if (product is not null)
            parts.Add(Describe(product.Feature));
        if (portrayalProduct is not null)
        {
            var portrayal = Describe(portrayalProduct.Portrayal);
            // An S-57 cell is drawn with S-101's portrayal: name it.
            parts.Add(ReferenceEquals(portrayalProduct, product)
                ? portrayal
                : string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_DrawnWith_SpecFormat, portrayalProduct.Spec, portrayal));
        }
        else if (product is not null)
        {
            parts.Add(Describe(product.Portrayal));
        }

        return parts.Count == 0 ? null : string.Join(Strings.Catalogues_DrawnWith_Separator, parts);
    }

    /// <summary>
    /// One catalogue for the Drawn with line: "Feature 2.0.0", "Portrayal
    /// 2.1.0-draft (custom)", "Portrayal 2.0.0 (custom didn't load)", "No
    /// portrayal catalogue" or "Drawn by SoundCharts". Built-in isn't named.
    /// </summary>
    /// <param name="state">The catalogue as in effect.</param>
    public static string Describe(CatalogueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Status switch
        {
            CatalogueStatus.NotRequired => Strings.Catalogues_Row_DrawnBySoundCharts,
            CatalogueStatus.Missing => Missing(state.Kind),
            CatalogueStatus.Error => string.Format(
                CultureInfo.CurrentCulture,
                Strings.Catalogues_DrawnWith_FailedFormat,
                state.Drawing is { } drawing ? KindVersionOrigin(state.Kind, drawing) : Missing(state.Kind),
                CatalogueText.Lower(state.Source?.Origin ?? CatalogueOrigin.Custom)),
            _ => state.Source is { } source ? KindVersionOrigin(state.Kind, source) : Missing(state.Kind),
        };
    }

    /// <summary>
    /// A cell of Help › About's table: "2.0.0 · built-in", "2.1.0-draft ·
    /// custom", "2.1.0 · custom, didn't load", "missing" or "drawn by SoundCharts".
    /// </summary>
    /// <param name="state">The catalogue as in effect.</param>
    public static string AboutCell(CatalogueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Status switch
        {
            CatalogueStatus.NotRequired => Strings.About_Catalogues_NotRequired,
            CatalogueStatus.Missing => Strings.About_Catalogues_Missing,
            _ when state.Source is null => Strings.About_Catalogues_Missing,
            CatalogueStatus.Error => string.Format(
                CultureInfo.CurrentCulture, Strings.About_Catalogues_ErrorFormat,
                CatalogueText.Version(state.Source), CatalogueText.Lower(state.Source.Origin)),
            _ => string.Format(
                CultureInfo.CurrentCulture, Strings.About_Catalogues_CellFormat,
                CatalogueText.Version(state.Source), CatalogueText.Lower(state.Source.Origin)),
        };
    }

    private static string KindVersionOrigin(CatalogueKind kind, CatalogueSourceInfo source)
    {
        var text = string.Format(
            CultureInfo.CurrentCulture,
            kind == CatalogueKind.Feature ? Strings.Catalogues_Row_FeatureFormat : Strings.Catalogues_Row_PortrayalFormat,
            CatalogueText.Version(source));
        return source.Origin == CatalogueOrigin.BuiltIn
            ? text
            : string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_DrawnWith_OriginFormat, text, CatalogueText.Lower(source.Origin));
    }

    private static string Missing(CatalogueKind kind) =>
        kind == CatalogueKind.Feature ? Strings.Catalogues_Row_FeatureMissing : Strings.Catalogues_Row_PortrayalMissing;

    private static bool Same(string a, string b) =>
        SpecName.TryNormalize(a, out var x) && SpecName.TryNormalize(b, out var y)
            ? string.Equals(x, y, StringComparison.Ordinal)
            : string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
