namespace EncDotNet.S100.DynamicSources.Ais;

/// <summary>
/// Vessel hull dimensions decoded from an AIS Type-5 / Type-24 part-A
/// "reference for position" payload. The four fields A/B/C/D from the
/// AIS message are folded into the same shape as
/// <see cref="DynamicSources.DynamicVesselGeometry"/>:
/// <see cref="LengthMetres"/> = A + B,
/// <see cref="BeamMetres"/> = C + D,
/// <see cref="BowOffsetMetres"/> = A,
/// <see cref="PortOffsetMetres"/> = C.
/// </summary>
/// <remarks>
/// All values are in metres. Producers are expected to drop
/// dimension blocks that violate the AIS spec's positivity / range
/// constraints.
/// </remarks>
public sealed record AisDimensions
{
    /// <summary>Bow-to-stern length in metres (A + B).</summary>
    public required double LengthMetres { get; init; }

    /// <summary>Port-to-starboard beam in metres (C + D).</summary>
    public required double BeamMetres { get; init; }

    /// <summary>Distance aft of bow to the GPS antenna in metres (A).</summary>
    public required double BowOffsetMetres { get; init; }

    /// <summary>Distance starboard of port side to the GPS antenna in metres (C).</summary>
    public required double PortOffsetMetres { get; init; }
}
