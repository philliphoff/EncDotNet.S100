using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// The chart reference entries a pick's codes name (symbol dictionary design
/// note §4.3, R9, #1678): its feature type, each attribute and each listed
/// value. A code the index holds names its entry; otherwise an S-57 acronym
/// names one through the feature catalogue's aliases (<see cref="S57Aliases"/>).
/// </summary>
internal static class PickTerms
{
    private const string S57 = "S-57";

    /// <summary>
    /// The product whose chart reference describes <paramref name="hit"/>: the
    /// one it is drawn with, so an S-57 cell's pick reads S-101's.
    /// </summary>
    /// <param name="hit">The pick.</param>
    /// <returns>The product, or <see langword="null"/> when the pick names none.</returns>
    public static SpecRef? SpecOf(PickHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);
        var name = hit.OwningProcessor?.PortrayalSpec.Name
            ?? (string.Equals(hit.ProductSpec, S57, StringComparison.OrdinalIgnoreCase) ? "S-101" : hit.ProductSpec);
        return string.IsNullOrWhiteSpace(name) ? null : new SpecRef(name, default);
    }

    /// <summary>The feature type <paramref name="code"/> names: by its code, or by its S-57 acronym.</summary>
    /// <param name="index">The index.</param>
    /// <param name="code">The pick's feature type code.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public static FeatureTypeEntry? FeatureType(ChartReferenceIndex index, string? code)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (string.IsNullOrWhiteSpace(code))
            return null;
        return index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, code), out var entry)
            ? entry as FeatureTypeEntry
            : index.S57Aliases.FeatureType(code);
    }

    /// <summary>The attribute <paramref name="code"/> names: by its code, or by its S-57 acronym.</summary>
    /// <param name="index">The index.</param>
    /// <param name="code">The pick's attribute code.</param>
    /// <returns>The entry, or <see langword="null"/>.</returns>
    public static AttributeEntry? Attribute(ChartReferenceIndex index, string? code)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (string.IsNullOrWhiteSpace(code))
            return null;
        return index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.Attribute, code), out var entry)
            ? entry as AttributeEntry
            : index.S57Aliases.Attribute(code);
    }

    /// <summary>
    /// "LateralBeacon (BCNLAT)": a code, with the other form of it in
    /// brackets when the entry knows one (its S-57 acronym, or the S-101
    /// code an acronym maps to). For Copy identity, where codes now live.
    /// </summary>
    /// <param name="code">The pick's code.</param>
    /// <param name="entryCode">The entry's code, or <see langword="null"/>.</param>
    /// <param name="alias">The entry's S-57 acronym, or <see langword="null"/>.</param>
    public static string CodeText(string code, string? entryCode, string? alias)
    {
        ArgumentNullException.ThrowIfNull(code);
        // Ordinal: S-101's colour and S-57's COLOUR differ only in case.
        var other = string.Equals(code, entryCode, StringComparison.Ordinal) ? alias : entryCode;
        return string.IsNullOrWhiteSpace(other) || string.Equals(other, code, StringComparison.Ordinal)
            ? code
            : $"{code} ({other})";
    }
}
