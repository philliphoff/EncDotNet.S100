namespace EncDotNet.S100.Tides;

/// <summary>A predicted high or low water.</summary>
/// <param name="Time">The time of the high or low water (UTC).</param>
/// <param name="HeightMeters">The predicted height above the station's datum, in metres.</param>
/// <param name="Kind">High or low water.</param>
public readonly record struct TideExtreme(DateTime Time, double HeightMeters, TideExtremeKind Kind);
