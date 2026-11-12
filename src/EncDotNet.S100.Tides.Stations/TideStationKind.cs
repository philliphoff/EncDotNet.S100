namespace EncDotNet.S100.Tides.Stations;

/// <summary>How a tide station is predicted.</summary>
public enum TideStationKind
{
    /// <summary>From its own harmonic constituents (NOAA's reference stations, type <c>R</c>).</summary>
    Harmonic,

    /// <summary>From a reference station's highs and lows and its own time and height differences (NOAA's type <c>S</c>).</summary>
    Subordinate,
}
