using System.Runtime.InteropServices;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// A single water level measurement containing height and trend.
/// </summary>
/// <remarks>
/// Matches the S-104 HDF5 compound type: waterLevelHeight (float32) + waterLevelTrend (uint8).
/// Trend values: 0 = unknown/undetermined, 1 = decreasing, 2 = increasing, 3 = steady.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 5)]
public readonly struct WaterLevelValue
{
    /// <summary>Height of the water level in metres relative to the vertical datum.</summary>
    public readonly float Height;

    /// <summary>
    /// Trend of the water level encoded as uint8:
    /// 0 = unknown/undetermined, 1 = decreasing, 2 = increasing, 3 = steady.
    /// </summary>
    public readonly byte Trend;

    /// <summary>Initializes a new <see cref="WaterLevelValue"/> with the given height and trend.</summary>
    public WaterLevelValue(float height, byte trend)
    {
        Height = height;
        Trend = trend;
    }
}
