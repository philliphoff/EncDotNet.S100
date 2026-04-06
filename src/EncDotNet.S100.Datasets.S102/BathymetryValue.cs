using System.Runtime.InteropServices;

namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// A single bathymetric measurement containing a depth value and its uncertainty.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 8)]
public readonly struct BathymetryValue
{
    /// <summary>Depth in metres. Positive values indicate depth below the vertical datum.</summary>
    [FieldOffset(0)]
    public readonly float Depth;

    /// <summary>Uncertainty of the depth measurement in metres.</summary>
    [FieldOffset(4)]
    public readonly float Uncertainty;

    public BathymetryValue(float depth, float uncertainty)
    {
        Depth = depth;
        Uncertainty = uncertainty;
    }
}
