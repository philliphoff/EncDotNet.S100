namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// The intensity a coded surface bulletin gives a front: <c>WK</c>,
/// <c>MDT</c> or <c>STG</c> after the front's code. The standard bulletin
/// documents them; current WPC bulletins, and every trough, leave them out.
/// </summary>
public enum SurfaceFrontStrength
{
    /// <summary>The bulletin gives no intensity.</summary>
    Unspecified,

    /// <summary>Weak, coded <c>WK</c>.</summary>
    Weak,

    /// <summary>Moderate, coded <c>MDT</c>.</summary>
    Moderate,

    /// <summary>Strong, coded <c>STG</c>.</summary>
    Strong,
}
