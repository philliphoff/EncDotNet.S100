using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Testing.Scenarios.World;

/// <summary>
/// A seeded, fictitious piece of sea: its frame, seabed, tide and datums.
/// The model is the truth every writer samples and every test asserts against.
/// </summary>
/// <param name="Name">A fictitious name written into generated metadata.</param>
/// <param name="Seed">The seed the world was drawn from.</param>
/// <param name="Frame">The local frame.</param>
/// <param name="Seabed">The seabed.</param>
/// <param name="Tide">The tide.</param>
/// <param name="Datums">The vertical references.</param>
public sealed record SyntheticWorld(
    string Name,
    ulong Seed,
    WorldFrame Frame,
    Seabed Seabed,
    TideModel Tide,
    WorldDatums Datums)
{
    /// <summary>
    /// The height of high water above chart datum, in metres: the coastline
    /// is the contour where the seabed meets it, so land is where the depth
    /// is less than its negative. Drying areas lie between it and chart datum.
    /// </summary>
    public double HighWaterHeight => Math.Round(Tide.MaximumHeight, 1);

    /// <summary>The depth below chart datum at a local point (see <see cref="Seabed.DepthAt"/>).</summary>
    /// <param name="x">Metres east of the origin.</param>
    /// <param name="y">Metres north of the origin.</param>
    /// <returns>Metres below chart datum.</returns>
    public double DepthAt(double x, double y) => Seabed.DepthAt(x, y);

    /// <summary>The depth below chart datum at a geographic position.</summary>
    /// <param name="position">The position.</param>
    /// <returns>Metres below chart datum.</returns>
    public double DepthAt(GeoPosition position)
    {
        var (x, y) = Frame.ToLocal(position.Latitude, position.Longitude);
        return Seabed.DepthAt(x, y);
    }

    /// <summary>The water level above its datum at a geographic position and time.</summary>
    /// <param name="position">The position.</param>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>Metres above the water-level datum.</returns>
    public double WaterLevelAt(GeoPosition position, DateTime utc)
    {
        var (x, y) = Frame.ToLocal(position.Latitude, position.Longitude);
        return Tide.HeightAt(x, y, utc);
    }

    /// <summary>
    /// "Synthetic Sound": an east–west shore with land to the south, a
    /// dredged channel along <c>y = 3000 m</c>, a drying spit running north at
    /// <c>x = 3000 m</c> whose tip stops short of the channel, a drying bank
    /// to the north-east and a shoal to the north-west. The frame's origin is
    /// in the open South Pacific (41° S, 135° W), far from any real chart and
    /// from the IHO test area at 32° S 63° E.
    /// </summary>
    /// <remarks>
    /// The seed draws the micro-relief and small offsets to the tidal phase
    /// lags; the large-scale geography is the same for every seed, so the
    /// scenarios' named places stay where they are.
    /// </remarks>
    /// <param name="seed">The seed.</param>
    /// <returns>The world.</returns>
    public static SyntheticWorld SyntheticSound(ulong seed)
    {
        var relief = MicroRelief.Draw(ScenarioRandom.For(seed, "relief"), count: 3, amplitude: 0.05);
        var phases = ScenarioRandom.For(seed, "tide-phases");
        var m2Phase = 30 + phases.NextDouble(-5, 5);
        var s2Phase = 60 + phases.NextDouble(-5, 5);

        return new SyntheticWorld(
            "Synthetic Sound",
            seed,
            new WorldFrame(-41.0, -135.0),
            new Seabed(
                new ShoreProfile(CentreY: 1500, Width: 1800, Amplitude: 14, Offset: 2),
                [
                    new Channel(CentreY: 3000, Width: 220, Deepening: 4),
                    new Spit(AxisX: 3000, TipY: 2350, Width: 160, CrestDepth: -1.0, TipTaper: 180),
                    new Mound(CentreX: 4800, CentreY: 4600, RadiusX: 500, RadiusY: 300, CrestDepth: -1.5),
                    new Mound(CentreX: 1200, CentreY: 4400, RadiusX: 300, RadiusY: 300, CrestDepth: 4.0),
                ],
                relief),
            new TideModel(
                Epoch: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                MeanLevel: 2.0,
                WestX: 0,
                EastX: 6000,
                [
                    new TideConstituent("M2", TideConstituent.M2Speed, 1.20, 1.30, m2Phase, m2Phase + 12),
                    new TideConstituent("S2", TideConstituent.S2Speed, 0.40, 0.42, s2Phase, s2Phase + 14),
                ],
                [
                    new TideStation("SYN-W", "Synthetic Sound West (fictitious)", 1800, 3400),
                    new TideStation("SYN-E", "Synthetic Sound East (fictitious)", 4800, 3400),
                ]),
            new WorldDatums(WorldDatums.LowestAstronomicalTide, WorldDatums.LowestAstronomicalTide));
    }
}
