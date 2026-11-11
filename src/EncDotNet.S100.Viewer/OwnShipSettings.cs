namespace EncDotNet.S100.Viewer;

/// <summary>
/// User-configured own-ship vessel dimensions. Persisted as part of
/// <see cref="ViewerSettings"/> and consumed by the own-ship dynamic
/// source so the renderer can draw a true-scale hull outline at
/// close zoom.
/// </summary>
/// <remarks>
/// <para>
/// All measurements are in metres. The four offsets describe the
/// position of the GPS / reference point relative to the hull
/// rectangle, matching the IEC 62388 CCRP convention:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="LengthMetres"/> — overall bow-to-stern length.</description></item>
///   <item><description><see cref="BeamMetres"/> — overall port-to-starboard beam.</description></item>
///   <item><description><see cref="BowOffsetMetres"/> — distance aft of bow to the GPS antenna.</description></item>
///   <item><description><see cref="PortOffsetMetres"/> — distance starboard of port side to the antenna.</description></item>
/// </list>
/// <para>
/// Defaults model a small ~50 m vessel with an amidships,
/// centreline antenna — visible at moderate zoom levels for a
/// default-config user.
/// </para>
/// </remarks>
public sealed class OwnShipSettings
{
    /// <summary>Bow-to-stern length in metres. Default 50.</summary>
    public double LengthMetres { get; set; } = 50.0;

    /// <summary>Port-to-starboard beam in metres. Default 10.</summary>
    public double BeamMetres { get; set; } = 10.0;

    /// <summary>GPS antenna distance aft of bow in metres. Default 25 (amidships).</summary>
    public double BowOffsetMetres { get; set; } = 25.0;

    /// <summary>GPS antenna distance starboard of port side in metres. Default 5 (centreline).</summary>
    public double PortOffsetMetres { get; set; } = 5.0;
}
