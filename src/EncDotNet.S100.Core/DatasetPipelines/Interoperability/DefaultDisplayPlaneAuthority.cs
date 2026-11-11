using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// A per-product table of default S-98 display planes. The product registry
/// (<c>ProductRegistry</c> in <c>EncDotNet.S100.Datasets.Pipelines</c>)
/// implements it from each registration's <c>DefaultPlanes</c> trait, so the
/// table lives with the products rather than in a <c>switch</c> here.
/// </summary>
public interface IProductPlaneTable
{
    /// <summary>
    /// Returns the default plane <paramref name="productSpec"/> gives
    /// <paramref name="layerKind"/>, or <see langword="null"/> when the product
    /// is unknown or gives that kind no plane.
    /// </summary>
    /// <param name="productSpec">The canonical product spec, e.g. <c>"S-101"</c>.</param>
    /// <param name="layerKind">The layer kind (e.g. <c>"area"</c>, <c>"s104.stations"</c>), or <see langword="null"/>.</param>
    S98DisplayPlane? FindDefaultPlane(string productSpec, string? layerKind);
}

/// <summary>
/// Mapsui-free default-plane authority over an <see cref="IProductPlaneTable"/>,
/// whose built-in entries follow S-98 Main §9.2.1 / MSC.530(106)/Rev.1
/// §Appendix 2 "priority of information". This is the authoritative
/// <see cref="IDisplayPlaneAuthority.GetDefaultPlane"/> implementation; the
/// Mapsui-typed cross-dataset stack authority delegates its own plane lookups
/// here so there is a single table.
/// </summary>
/// <remarks>
/// A product the table doesn't know, or a layer kind it gives no plane, lands
/// on <see cref="S98DisplayPlane.OtherChartOverlays"/> so the renderer doesn't
/// lose the layer. The built-in table (the registry's product traits) is:
/// <list type="bullet">
///   <item><description>S-101 / S-57 / S-401: <c>"area"</c> → BaseChartUnder; otherwise BaseChartOver.</description></item>
///   <item><description>S-102: Bathymetry.</description></item>
///   <item><description>S-104: <c>"s104.stations"</c> → OtherChartOverlays; otherwise OnDemandSurface.</description></item>
///   <item><description>S-111: every sub-layer (<c>"s111.arrows"</c> grid arrows and <c>"s111.stations"</c> station / mesh arrows) → DynamicArrows.</description></item>
///   <item><description>S-124: CautionsAndWarnings; S-129: OnDemandSurface; the other GML products: OtherChartOverlays.</description></item>
/// </list>
/// </remarks>
public sealed class DefaultDisplayPlaneAuthority : IDisplayPlaneAuthority
{
    private readonly IProductPlaneTable? _products;

    /// <summary>Creates an authority over <paramref name="products"/>.</summary>
    /// <param name="products">The per-product plane table, usually the product registry.</param>
    public DefaultDisplayPlaneAuthority(IProductPlaneTable products)
    {
        ArgumentNullException.ThrowIfNull(products);
        _products = products;
    }

    private DefaultDisplayPlaneAuthority()
    {
    }

    /// <summary>
    /// An authority that knows no products and puts every layer on
    /// <see cref="S98DisplayPlane.OtherChartOverlays"/>. Used where no product
    /// table is in reach (a renderer without the dataset pipelines); hosts pass
    /// the product registry's authority instead.
    /// </summary>
    public static DefaultDisplayPlaneAuthority Empty { get; } = new();

    /// <inheritdoc />
    public S98DisplayPlane GetDefaultPlane(string productSpec, string? featureTypeOrLayerKind = null)
    {
        ArgumentNullException.ThrowIfNull(productSpec);
        return _products?.FindDefaultPlane(productSpec, featureTypeOrLayerKind)
            ?? S98DisplayPlane.OtherChartOverlays;
    }
}
