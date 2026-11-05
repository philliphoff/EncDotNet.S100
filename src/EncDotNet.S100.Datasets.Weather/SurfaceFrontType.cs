namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The kind of line in a surface analysis, as the WPC coded surface bulletin
/// names it (design note <c>vector-weather.md</c> §5.1, §8.3).
/// </summary>
public enum SurfaceFrontType
{
    /// <summary>A cold front, coded <c>COLD</c>.</summary>
    Cold,

    /// <summary>A warm front, coded <c>WARM</c>.</summary>
    Warm,

    /// <summary>An occluded front, coded <c>OCFNT</c>.</summary>
    Occluded,

    /// <summary>A stationary front, coded <c>STNRY</c>.</summary>
    Stationary,

    /// <summary>
    /// A trough, coded <c>TROF</c>. WPC uses it for a trough, an outflow
    /// boundary, a squall line or a dry line; the bulletin doesn't say which.
    /// </summary>
    Trough,
}
