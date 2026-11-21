namespace EncDotNet.S100.Tides;

/// <summary>
/// The vertical datum a station's predictions are given on, as the height of
/// mean sea level above it.
/// </summary>
/// <remarks>
/// Harmonic constituents describe the tide about mean sea level (MSL). A
/// prediction on another datum adds the height of MSL above that datum: for
/// a US station on its chart datum, mean lower low water (MLLW), that's MSL
/// minus MLLW from the station's published datums.
/// </remarks>
/// <param name="Name">The datum's name, such as <c>MSL</c> or <c>MLLW</c>.</param>
/// <param name="MeanSeaLevelAboveDatumMeters">
/// The height of mean sea level above the datum, in metres. Zero for MSL
/// itself; positive for a datum below MSL, such as MLLW.
/// </param>
public sealed record TideDatum(string Name, double MeanSeaLevelAboveDatumMeters)
{
    /// <summary>Mean sea level, the datum the constituents describe the tide about.</summary>
    public static TideDatum MeanSeaLevel { get; } = new("MSL", 0.0);
}
