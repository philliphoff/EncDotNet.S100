using System.Runtime.InteropServices;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// A single water level measurement containing height and trend.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public readonly struct WaterLevelValue
{
    /// <summary>Height of the water level in metres relative to the vertical datum.</summary>
    [FieldOffset(0)]
    public readonly float Height;

    /// <summary>Trend of the water level (e.g. rising, falling, steady).</summary>
    [FieldOffset(4)]
    public readonly float Trend;

    public WaterLevelValue(float height, float trend)
    {
        Height = height;
        Trend = trend;
    }
}
