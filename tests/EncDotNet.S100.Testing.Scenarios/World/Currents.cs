namespace EncDotNet.S100.Testing.Scenarios.World;

/// <summary>A surface current: speed and the direction it flows towards.</summary>
/// <param name="SpeedKnots">Speed in knots.</param>
/// <param name="DirectionDegrees">The direction the water flows towards, in degrees true [0, 360).</param>
public readonly record struct SurfaceCurrent(double SpeedKnots, double DirectionDegrees);

/// <summary>
/// Tidal streams derived from the tide, so they stay consistent with the
/// water levels: the Sound fills from the west, so the stream floods east
/// while the tide rises and ebbs west while it falls, and is slack at high
/// and low water.
/// </summary>
/// <remarks>
/// <para>
/// Speed is proportional to the tide's rate of change at the point:
/// <c>speed = KnotsPerMetrePerHour × |dh/dt|</c>, the continuity balance of
/// a basin filling through its mouth. Direction is
/// <see cref="FloodDirectionDegrees"/> while <c>dh/dt ≥ 0</c> and its
/// reciprocal otherwise. On land (shallower than high water) there is no
/// current.
/// </para>
/// <para>
/// With the Synthetic Sound tide the strongest streams are about 1,3 kn at
/// mid-flood and mid-ebb.
/// </para>
/// </remarks>
/// <param name="KnotsPerMetrePerHour">Knots of stream per metre per hour of tidal rise or fall.</param>
/// <param name="FloodDirectionDegrees">The direction the flood flows towards, in degrees true.</param>
public sealed record CurrentModel(double KnotsPerMetrePerHour, double FloodDirectionDegrees)
{
    /// <summary>The Synthetic Sound streams: 1,5 kn per m/h, flooding east.</summary>
    public static CurrentModel SyntheticSound { get; } = new(1.5, 90);

    /// <summary>The ebb direction, the reciprocal of the flood.</summary>
    public double EbbDirectionDegrees => (FloodDirectionDegrees + 180) % 360;

    /// <summary>The current at a point and time.</summary>
    /// <param name="world">The world.</param>
    /// <param name="x">Metres east of the origin.</param>
    /// <param name="y">Metres north of the origin.</param>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The current, or <c>null</c> on land.</returns>
    public SurfaceCurrent? At(SyntheticWorld world, double x, double y, DateTime utc)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (world.DepthAt(x, y) <= -world.HighWaterHeight)
        {
            return null;
        }

        var rate = world.Tide.RateAt(x, y, utc);
        return new SurfaceCurrent(KnotsPerMetrePerHour * Math.Abs(rate), rate >= 0 ? FloodDirectionDegrees : EbbDirectionDegrees);
    }
}
