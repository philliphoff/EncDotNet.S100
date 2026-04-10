using System.Runtime.InteropServices;

namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// A single surface current measurement containing speed and direction.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public readonly struct SurfaceCurrentValue
{
    /// <summary>Speed of the surface current in knots.</summary>
    [FieldOffset(0)]
    public readonly float Speed;

    /// <summary>Direction of the surface current in degrees from true north (clockwise).</summary>
    [FieldOffset(4)]
    public readonly float Direction;

    public SurfaceCurrentValue(float speed, float direction)
    {
        Speed = speed;
        Direction = direction;
    }
}
