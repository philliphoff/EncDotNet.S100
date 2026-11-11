namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// Optional vessel-geometry sidecar for a moving point feature
/// (own-ship, AIS target). Lets renderers draw a true-scale hull
/// outline when the viewport is zoomed in far enough for the vessel
/// to be visually distinguishable.
/// </summary>
/// <remarks>
/// <para>
/// All lengths are in metres. The four offsets together describe
/// the position of the GPS / reference point ("CCRP" per IEC 62388)
/// relative to the hull rectangle:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="LengthMetres"/> — bow-to-stern length.</description></item>
///   <item><description><see cref="BeamMetres"/> — port-to-starboard width.</description></item>
///   <item><description><see cref="BowOffsetMetres"/> — distance aft of bow to the antenna.</description></item>
///   <item><description><see cref="PortOffsetMetres"/> — distance starboard of port side to the antenna.</description></item>
/// </list>
/// <para>
/// The shape mirrors the AIS Type 5 reference-point payload
/// (dimA/dimB/dimC/dimD) so AIS adapters map cleanly:
/// <c>LengthMetres = dimA + dimB</c>,
/// <c>BeamMetres = dimC + dimD</c>,
/// <c>BowOffsetMetres = dimA</c>, <c>PortOffsetMetres = dimC</c>.
/// </para>
/// <para>
/// Validation (positive lengths, bow/port offsets within the hull)
/// is documented but not enforced by the record — the type tolerates
/// partial / out-of-range data the same way <see cref="DynamicMotion"/>
/// does. Producers (e.g. settings UIs) are expected to guard input.
/// </para>
/// </remarks>
public sealed record DynamicVesselGeometry
{
    /// <summary>Bow-to-stern length in metres. Must be &gt; 0.</summary>
    public required double LengthMetres { get; init; }

    /// <summary>Port-to-starboard beam in metres. Must be &gt; 0.</summary>
    public required double BeamMetres { get; init; }

    /// <summary>Distance aft of bow to the GPS antenna in metres
    /// (0 ≤ x ≤ <see cref="LengthMetres"/>).</summary>
    public required double BowOffsetMetres { get; init; }

    /// <summary>Distance starboard of port side to the GPS antenna in
    /// metres (0 ≤ x ≤ <see cref="BeamMetres"/>).</summary>
    public required double PortOffsetMetres { get; init; }
}
