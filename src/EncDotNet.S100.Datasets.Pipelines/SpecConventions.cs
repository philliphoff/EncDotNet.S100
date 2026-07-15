using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Central, authoritative mapping between a dataset's <em>product identity</em>
/// (the specification it declares conformance to, e.g. <c>"S-57"</c>) and the
/// <em>portrayal specification</em> whose Feature Catalogue, Portrayal
/// Catalogue, and ECDIS display conventions are actually used to process and
/// draw it.
/// </summary>
/// <remarks>
/// <para>
/// The two coincide for every native S-100 product. They diverge for the
/// legacy S-57 ENC format: an S-57 cell is translated in-memory to an
/// <c>S101Document</c> and portrayed through the S-101 catalogue
/// (<see cref="S57DatasetProcessor"/>), so it keeps product identity
/// <c>"S-57"</c> (labels, validation rebadging, examiner links) while acting
/// as <c>"S-101"</c> for catalogue resolution, viewing-group / display-category
/// state keying, and the S-100 Part 9 §11.7 display-mode selection.
/// </para>
/// <para>
/// Keeping this rule in one place prevents the <c>spec == "S-57" ? "S-101" : spec</c>
/// idiom from drifting across the several consumers that need the portrayal
/// spec rather than the product identity. Prefer
/// <see cref="IDatasetProcessor.PortrayalSpec"/> when a processor instance is
/// available; use the string overload only where a caller has just the raw
/// product-spec code (e.g. the viewer's <c>DatasetEntry.ProductSpec</c>).
/// </para>
/// </remarks>
public static class SpecConventions
{
    private const string S57 = "S-57";
    private const string S101 = "S-101";

    /// <summary>
    /// Returns the portrayal specification for <paramref name="product"/>:
    /// the S-101 spec (edition unspecified) for an S-57 product, otherwise
    /// <paramref name="product"/> unchanged.
    /// </summary>
    /// <param name="product">The dataset's declared product specification.</param>
    /// <returns>The specification whose catalogue / conventions portray it.</returns>
    public static SpecRef PortrayalSpecFor(SpecRef product)
        => string.Equals(product.Name, S57, StringComparison.OrdinalIgnoreCase)
            ? new SpecRef(S101, default)
            : product;

    /// <summary>
    /// String overload of <see cref="PortrayalSpecFor(SpecRef)"/> for callers
    /// that hold only the canonical product-spec code (e.g. the viewer's
    /// <c>DatasetEntry.ProductSpec</c>): maps <c>"S-57"</c> to <c>"S-101"</c>
    /// and returns every other code unchanged.
    /// </summary>
    /// <param name="productSpecName">The canonical product-spec code.</param>
    /// <returns>The portrayal-spec code.</returns>
    public static string PortrayalSpecName(string productSpecName)
    {
        ArgumentNullException.ThrowIfNull(productSpecName);
        return string.Equals(productSpecName, S57, StringComparison.OrdinalIgnoreCase)
            ? S101
            : productSpecName;
    }
}
