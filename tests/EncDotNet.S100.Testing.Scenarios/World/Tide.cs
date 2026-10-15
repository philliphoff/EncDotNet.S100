namespace EncDotNet.S100.Testing.Scenarios.World;

/// <summary>
/// One harmonic constituent whose amplitude and phase lag vary linearly
/// from the world's west reference to its east reference.
/// </summary>
/// <param name="Name">The constituent's name, such as <c>"M2"</c>.</param>
/// <param name="SpeedDegreesPerHour">The angular speed in degrees per hour.</param>
/// <param name="AmplitudeWest">Amplitude at the west reference, in metres.</param>
/// <param name="AmplitudeEast">Amplitude at the east reference, in metres.</param>
/// <param name="PhaseWestDegrees">Phase lag at the west reference, in degrees.</param>
/// <param name="PhaseEastDegrees">Phase lag at the east reference, in degrees.</param>
public sealed record TideConstituent(
    string Name,
    double SpeedDegreesPerHour,
    double AmplitudeWest,
    double AmplitudeEast,
    double PhaseWestDegrees,
    double PhaseEastDegrees)
{
    /// <summary>The principal lunar semidiurnal constituent's speed (degrees per hour).</summary>
    public const double M2Speed = 28.9841042;

    /// <summary>The principal solar semidiurnal constituent's speed (degrees per hour).</summary>
    public const double S2Speed = 30.0;
}

/// <summary>A tide gauge position in the world.</summary>
/// <param name="Identifier">The station identifier written to S-104.</param>
/// <param name="Name">A fictitious display name.</param>
/// <param name="X">Metres east of the origin.</param>
/// <param name="Y">Metres north of the origin.</param>
public sealed record TideStation(string Identifier, string Name, double X, double Y);

/// <summary>
/// The tide: water-level height above the water-level datum from harmonic
/// constituents, smooth in space between a west and an east reference line.
/// </summary>
/// <remarks>
/// <c>h(x, t) = MeanLevel + Σ A(x) cos(speed · (t − Epoch) − g(x))</c>, with
/// <c>A</c> and <c>g</c> interpolated linearly in <c>x</c> between
/// <see cref="WestX"/> and <see cref="EastX"/> and held constant beyond
/// them. Stations and grid nodes are both samples of this one field, so
/// they agree wherever they meet.
/// </remarks>
/// <param name="Epoch">The time origin of the phase lags (UTC).</param>
/// <param name="MeanLevel">Mean water level above the datum, in metres.</param>
/// <param name="WestX">The west reference line, in metres east.</param>
/// <param name="EastX">The east reference line, in metres east.</param>
/// <param name="Constituents">The constituents.</param>
/// <param name="Stations">Where the S-104 stations are.</param>
public sealed record TideModel(
    DateTime Epoch,
    double MeanLevel,
    double WestX,
    double EastX,
    IReadOnlyList<TideConstituent> Constituents,
    IReadOnlyList<TideStation> Stations)
{
    /// <summary>The highest the tide can reach above the datum, in metres.</summary>
    public double MaximumHeight
    {
        get
        {
            var height = MeanLevel;
            foreach (var c in Constituents)
            {
                height += Math.Max(c.AmplitudeWest, c.AmplitudeEast);
            }

            return height;
        }
    }

    /// <summary>The lowest the tide can reach above the datum, in metres.</summary>
    public double MinimumHeight
    {
        get
        {
            var height = MeanLevel;
            foreach (var c in Constituents)
            {
                height -= Math.Max(c.AmplitudeWest, c.AmplitudeEast);
            }

            return height;
        }
    }

    /// <summary>The height above the water-level datum at a point and time.</summary>
    /// <param name="x">Metres east of the origin.</param>
    /// <param name="y">Metres north of the origin (the field doesn't vary north–south).</param>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>Metres above the datum.</returns>
    public double HeightAt(double x, double y, DateTime utc)
    {
        _ = y;
        var hours = (utc - Epoch).TotalHours;
        var f = Math.Clamp((x - WestX) / (EastX - WestX), 0, 1);
        var height = MeanLevel;
        foreach (var c in Constituents)
        {
            var amplitude = c.AmplitudeWest + f * (c.AmplitudeEast - c.AmplitudeWest);
            var phase = c.PhaseWestDegrees + f * (c.PhaseEastDegrees - c.PhaseWestDegrees);
            height += amplitude * Math.Cos((c.SpeedDegreesPerHour * hours - phase) * Math.PI / 180.0);
        }

        return height;
    }

    /// <summary>The rate of change at a point and time, in metres per hour.</summary>
    /// <param name="x">Metres east of the origin.</param>
    /// <param name="y">Metres north of the origin.</param>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>Metres per hour (positive rising).</returns>
    public double RateAt(double x, double y, DateTime utc)
    {
        _ = y;
        var hours = (utc - Epoch).TotalHours;
        var f = Math.Clamp((x - WestX) / (EastX - WestX), 0, 1);
        double rate = 0;
        foreach (var c in Constituents)
        {
            var amplitude = c.AmplitudeWest + f * (c.AmplitudeEast - c.AmplitudeWest);
            var phase = c.PhaseWestDegrees + f * (c.PhaseEastDegrees - c.PhaseWestDegrees);
            var omega = c.SpeedDegreesPerHour * Math.PI / 180.0;
            rate -= amplitude * omega * Math.Sin((c.SpeedDegreesPerHour * hours - phase) * Math.PI / 180.0);
        }

        return rate;
    }
}

/// <summary>
/// The vertical references, as S-100 vertical datum codes (S-100 Part 4a
/// register, source identifier 996), kept explicit so a scenario can make
/// them disagree on purpose.
/// </summary>
/// <param name="ChartDatum">The datum the charted depths (S-101, S-102) refer to.</param>
/// <param name="WaterLevelDatum">The datum the water levels (S-104) refer to.</param>
public sealed record WorldDatums(int ChartDatum, int WaterLevelDatum)
{
    /// <summary>Lowest Astronomical Tide (S-100 code 23).</summary>
    public const int LowestAstronomicalTide = 23;

    /// <summary>Mean Lower Low Water (S-100 code 12).</summary>
    public const int MeanLowerLowWater = 12;
}
