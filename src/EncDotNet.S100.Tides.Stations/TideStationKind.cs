namespace EncDotNet.S100.Tides.Stations;

/// <summary>How a tide or tidal current station is predicted.</summary>
public enum TideStationKind
{
    /// <summary>From its own harmonic constituents (NOAA's reference tide stations, type <c>R</c>, and harmonic current stations, type <c>H</c>).</summary>
    Harmonic,

    /// <summary>From a reference station's highs and lows, or slacks and maxima, and its own differences (NOAA's type <c>S</c>).</summary>
    Subordinate,
}
