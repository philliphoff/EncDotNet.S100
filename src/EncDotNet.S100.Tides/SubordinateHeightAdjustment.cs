namespace EncDotNet.S100.Tides;

/// <summary>How a subordinate station's height differences apply to its reference station's heights.</summary>
public enum SubordinateHeightAdjustment
{
    /// <summary>The reference height is multiplied by the difference (NOAA's <c>R</c>, a ratio).</summary>
    Ratio,

    /// <summary>The difference, in metres, is added to the reference height (NOAA's <c>F</c>, fixed).</summary>
    Offset,
}
