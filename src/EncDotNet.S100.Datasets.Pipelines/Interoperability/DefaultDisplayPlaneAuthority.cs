using System;
using EncDotNet.S100.Interoperability;

namespace EncDotNet.S100.Datasets.Pipelines.Interoperability;

/// <summary>
/// Mapsui-free default-plane table derived from S-98 Main §9.2.1 /
/// MSC.530(106)/Rev.1 §Appendix 2 "priority of information". This is the
/// authoritative <see cref="IDisplayPlaneAuthority.GetDefaultPlane"/>
/// implementation; the Mapsui-typed cross-dataset stack authority delegates
/// its own plane lookups here so there is a single table.
/// </summary>
/// <remarks>
/// Recognised <c>featureTypeOrLayerKind</c> values:
/// <list type="bullet">
///   <item><description>S-101 / S-57: <c>"area"</c> → BaseChartUnder; otherwise BaseChartOver.</description></item>
///   <item><description>S-104: <c>"s104.stations"</c> → OtherChartOverlays; otherwise OnDemandSurface.</description></item>
///   <item><description>S-111: <c>"s111.arrows"</c> → DynamicArrows; <c>"s111.stations"</c> → OtherChartOverlays.</description></item>
/// </list>
/// </remarks>
public sealed class DefaultDisplayPlaneAuthority : IDisplayPlaneAuthority
{
    /// <summary>A shared, stateless instance.</summary>
    public static readonly DefaultDisplayPlaneAuthority Instance = new();

    /// <inheritdoc />
    public S98DisplayPlane GetDefaultPlane(string productSpec, string? featureTypeOrLayerKind = null)
    {
        ArgumentNullException.ThrowIfNull(productSpec);

        var kind = featureTypeOrLayerKind?.Trim();

        return productSpec switch
        {
            // S-101 ENC / S-57. Split between fills and line work per
            // S-98 Annex A §A-6.9.1 (so S-102 lands between them).
            "S-101" or "S-57" => string.Equals(kind, "area", StringComparison.OrdinalIgnoreCase)
                ? S98DisplayPlane.BaseChartUnder
                : S98DisplayPlane.BaseChartOver,

            // S-102 Bathymetric Surface (S-98 Annex A §A-6.9.1).
            "S-102" => S98DisplayPlane.Bathymetry,

            // S-104 Water Level. Coverage band on the on-demand surface
            // plane; station glyphs are point overlays.
            "S-104" => kind switch
            {
                "s104.stations" => S98DisplayPlane.OtherChartOverlays,
                _ => S98DisplayPlane.OnDemandSurface,
            },

            // S-111 Surface Currents (Edition 2.0.0). Arrows above warnings
            // as a dynamic overlay; station glyphs as point overlays.
            "S-111" => kind switch
            {
                "s111.arrows" => S98DisplayPlane.DynamicArrows,
                "s111.stations" => S98DisplayPlane.OtherChartOverlays,
                _ => S98DisplayPlane.DynamicArrows,
            },

            // S-124 Navigational Warnings — MSC.530(106)/Rev.1 §Appendix 2
            // layers 3-4; S-98 Main §9.2.1.
            "S-124" => S98DisplayPlane.CautionsAndWarnings,

            // S-129 Under Keel Clearance Management (PR-L1 placement).
            "S-129" => S98DisplayPlane.OnDemandSurface,

            // Out-of-S-98-scope products. MSC.530(106)/Rev.1 §Appendix 2
            // default plane assignment.
            "S-122" or "S-125" or "S-127" or "S-128"
                or "S-131" or "S-201" or "S-411" or "S-421"
                => S98DisplayPlane.OtherChartOverlays,

            // Unknown product — land at the catch-all overlay plane so the
            // renderer doesn't lose the layer.
            _ => S98DisplayPlane.OtherChartOverlays,
        };
    }
}

/// <summary>
/// The default <see cref="IDisplayPlaneAuthorityProvider"/>: exposes the
/// stateless <see cref="DefaultDisplayPlaneAuthority"/> and never raises
/// <see cref="CurrentChanged"/> (the plane table is policy-invariant).
/// </summary>
public sealed class DisplayPlaneAuthorityProvider : IDisplayPlaneAuthorityProvider
{
    /// <inheritdoc />
    public IDisplayPlaneAuthority Current => DefaultDisplayPlaneAuthority.Instance;

    /// <inheritdoc />
    public event Action? CurrentChanged
    {
        add { }
        remove { }
    }
}
