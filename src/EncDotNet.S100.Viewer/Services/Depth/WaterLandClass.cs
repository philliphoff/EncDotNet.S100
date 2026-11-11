namespace EncDotNet.S100.Viewer.Services.Depth;

/// <summary>
/// The water/land classification of a picked location, used to gate the
/// depth-assimilation card (shown on water picks only, per the design's
/// visibility rule).
/// </summary>
internal enum WaterLandClass
{
    /// <summary>
    /// No positive signal either way — neither an S-101 skin-of-the-earth
    /// area nor S-102 coverage was found at the point. The depth card is
    /// suppressed to avoid drawing tide over land.
    /// </summary>
    Unknown,

    /// <summary>
    /// The pick lies on water: an S-101 <c>DepthArea</c> / <c>DredgedArea</c>
    /// / <c>UnsurveyedArea</c> (including drying areas with a negative
    /// <c>depthRangeMinimumValue</c>), or S-102 bathymetric coverage.
    /// </summary>
    Water,

    /// <summary>
    /// The pick lies on an S-101 <c>LandArea</c>.
    /// </summary>
    Land,
}
