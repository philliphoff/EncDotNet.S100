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
/// (<c>S57DatasetProcessor</c>), so it keeps product identity
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

    /// <summary>
    /// The S-104 portrayal: S-104 Edition 2.0.0 §9 defines none, so the station
    /// gauges and surface isolines are SoundCharts' own (see
    /// <c>S104CoverageStyle</c>).
    /// </summary>
    public static ApplicationPortrayal S104Portrayal { get; } = new("S-104", "2.0.0");

    /// <summary>
    /// Returns the <see cref="ApplicationPortrayal"/> for a product whose
    /// specification defines no portrayal, or <see langword="null"/> when the
    /// product is drawn from its official portrayal catalogue (or isn't drawn).
    /// Hosts use it to label the portrayal wherever they describe it: the
    /// viewer's pick report and dataset inspector, the CLI's <c>info</c> and
    /// the MCP tools.
    /// </summary>
    /// <param name="productSpecName">The canonical product-spec code, e.g. <c>"S-104"</c>.</param>
    public static ApplicationPortrayal? ApplicationPortrayalFor(string? productSpecName)
        => string.Equals(productSpecName, S104Portrayal.ProductSpec, StringComparison.OrdinalIgnoreCase)
            ? S104Portrayal
            : null;
}

/// <summary>
/// A portrayal that SoundCharts defines itself because the product
/// specification defines none, so it isn't IHO symbology.
/// </summary>
/// <param name="ProductSpec">The product-spec code, e.g. <c>"S-104"</c>.</param>
/// <param name="Edition">The edition that defines no portrayal, e.g. <c>"2.0.0"</c>.</param>
public sealed record ApplicationPortrayal(string ProductSpec, string Edition)
{
    /// <summary>The specification and edition, e.g. <c>"S-104 Ed 2.0.0"</c>.</summary>
    public string Basis => $"{ProductSpec} Ed {Edition}";

    /// <summary>
    /// The English label, e.g. <c>"SoundCharts portrayal: S-104 Ed 2.0.0
    /// defines no symbology"</c>. The viewer localises its own copy.
    /// </summary>
    public string Note => $"SoundCharts portrayal: {Basis} defines no symbology";
}
