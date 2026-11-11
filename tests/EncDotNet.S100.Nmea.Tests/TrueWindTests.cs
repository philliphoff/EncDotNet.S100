using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Nmea.Tests;

public sealed class TrueWindTests
{
    private static Angle Deg(double d) => Angle.FromDegrees(d);
    private static Speed Kn(double k) => Speed.FromKnots(k);

    [Fact]
    public void Motoring_into_a_calm_feels_like_a_head_wind()
    {
        // Heading north at 10 kn, apparent wind 10 kn dead ahead: no true wind.
        var wind = TrueWind.OverGround(Deg(0), Kn(10), Deg(0), Deg(0), Kn(10));

        Assert.Equal(0, wind.Speed.TotalKnots, 9);
    }

    [Fact]
    public void A_stationary_vessel_feels_the_true_wind()
    {
        // Moored heading 090°, wind 30° on the starboard bow: from 120°.
        var wind = TrueWind.OverGround(Deg(30), Kn(12), Deg(90), Deg(0), Kn(0));

        Assert.Equal(120, wind.FromDirection.TotalDegrees, 9);
        Assert.Equal(12, wind.Speed.TotalKnots, 9);
    }

    [Fact]
    public void A_beam_reach_true_wind_is_aft_of_the_apparent_wind()
    {
        // Heading 000° at 6 kn; true wind from 090° at 8 kn. Apparent:
        // 10 kn from 53.13° on the starboard bow.
        double awa = Math.Atan2(8, 6) * 180 / Math.PI;
        var wind = TrueWind.ThroughWater(Deg(awa), Kn(10), Deg(0), Kn(6));

        Assert.Equal(90, wind.FromDirection.TotalDegrees, 6);
        Assert.Equal(8, wind.Speed.TotalKnots, 6);
    }

    [Fact]
    public void Ground_and_water_wind_differ_by_the_current()
    {
        // The fixture's case: heading 090°, 6.0 kn through the water, 6.5 kn
        // over the ground on 095°, true wind over the ground from 200° at 14 kn,
        // which gives an apparent wind of 083.0° R at 13.8 kn.
        var ground = TrueWind.OverGround(Deg(83.0), Kn(13.8), Deg(90), Deg(95), Kn(6.5));
        var water = TrueWind.ThroughWater(Deg(83.0), Kn(13.8), Deg(90), Kn(6.0));

        Assert.Equal(200.0, ground.FromDirection.TotalDegrees, 0.5);
        Assert.Equal(14.0, ground.Speed.TotalKnots, 0.1);
        Assert.Equal(197.4, water.FromDirection.TotalDegrees, 0.5);
        Assert.Equal(14.4, water.Speed.TotalKnots, 0.1);
    }

    [Fact]
    public void Directions_wrap_into_0_to_360()
    {
        // Heading 350°, apparent wind 20° to starboard, stationary: from 010°.
        var wind = TrueWind.OverGround(Deg(20), Kn(5), Deg(350), Deg(0), Kn(0));

        Assert.Equal(10, wind.FromDirection.TotalDegrees, 9);
        Assert.Equal(0, NmeaAngles.Normalise(Deg(360)).TotalDegrees);
        Assert.Equal(270, NmeaAngles.Normalise(Deg(-90)).TotalDegrees);
        Assert.Equal(15, NmeaAngles.Normalise(Deg(735)).TotalDegrees);
    }
}
