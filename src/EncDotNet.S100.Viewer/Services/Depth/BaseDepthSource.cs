namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// Identifies which S-100 data source supplied a picked location's base
/// depth, in the resolver's fallthrough priority order (S-102 bathymetry
/// first, then S-101 vector fallbacks). See the depth-assimilation design:
/// prefer the continuous, uncertainty-bearing bathymetric surface when it
/// overlaps, then the charted dredged/depth areas, then the nearest
/// individual sounding.
/// </summary>
internal enum BaseDepthSource
{
    /// <summary>
    /// S-102 bathymetric coverage sampled at the nearest grid cell. Carries
    /// a vertical uncertainty and a declared vertical datum.
    /// </summary>
    Bathymetry,

    /// <summary>
    /// S-101 <c>DredgedArea</c> minimum depth (<c>depthRangeMinimumValue</c> /
    /// DRVAL1). Preferred over a plain depth area because a dredged depth is
    /// a maintained, surveyed value.
    /// </summary>
    DredgedArea,

    /// <summary>
    /// S-101 <c>DepthArea</c> minimum depth (<c>depthRangeMinimumValue</c> /
    /// DRVAL1) — the shoalest depth of the area containing the pick.
    /// </summary>
    DepthArea,

    /// <summary>
    /// S-101 nearest charted <c>Sounding</c> point (per-point Z), used when
    /// no area depth is available.
    /// </summary>
    Sounding,
}
