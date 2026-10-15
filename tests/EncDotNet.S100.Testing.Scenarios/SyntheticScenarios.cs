using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios;

/// <summary>The scenarios the generator knows by name.</summary>
public static class SyntheticScenarios
{
    /// <summary>The default seed.</summary>
    public const ulong DefaultSeed = 941;

    /// <summary>
    /// "Spit across scales" (a Calshot-style passage, #941): a dredged channel
    /// past a drying spit in Synthetic Sound.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// Two S-101 cells cover the spit: a harbour-band cell sampled at 25 m
    /// with contours at 0, 2, 5, 10, 15 and 20 m, and a coastal-band cell
    /// sampled at 150 m with shoal-biased generalisation over 150 m and
    /// contours at 0, 5, 10 and 20 m. Off the spit's tip they disagree about
    /// DRVAL1: at <c>spit-shoulder</c> the harbour cell charts 2 m and the
    /// coastal cell a drying area (#938).
    /// </description></item>
    /// <item><description>
    /// An S-102 surface covers the channel (<c>y</c> 2800–3500 m) but not the
    /// spit, so a passage across the spit falls back to S-101.
    /// </description></item>
    /// <item><description>
    /// S-104 stations sit either side of the spit (<c>SYN-W</c>,
    /// <c>SYN-E</c>, 10-minute records) and an hourly S-104 grid covers the
    /// east of the harbour (<c>x</c> ≥ 3600 m).
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="seed">The world's seed.</param>
    /// <param name="declareS102VerticalDatum">
    /// <c>false</c> leaves <c>verticalDatum</c> out of the S-102 file, as some
    /// producers do (#939).
    /// </param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition SpitAcrossScales(ulong seed = DefaultSeed, bool declareS102VerticalDatum = true)
    {
        var world = SyntheticWorld.SyntheticSound(seed);
        var name = declareS102VerticalDatum ? "spit-across-scales" : "spit-across-scales-no-s102-datum";
        return new ScenarioDefinition(
            name,
            world,
            Issued: new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc),
            WaterLevelStart: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            WaterLevelEnd: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            Cells:
            [
                new S101CellSpec(
                    "101ZZ00SPIT5",
                    "Synthetic Sound harbour (SYNTHETIC, fictitious; not for navigation)",
                    new LocalRect(0, 0, 6000, 6000),
                    GridSpacing: 25,
                    GeneralisationRadius: 0,
                    DepthContours: [0, 2, 5, 10, 15, 20],
                    SoundingSpacing: 250,
                    MinimumDisplayScale: 22000,
                    OptimumDisplayScale: 12000,
                    MaximumDisplayScale: 3000,
                    FoidSubdivision: 1),
                new S101CellSpec(
                    "101ZZ00SPIT3",
                    "Synthetic Sound coastal (SYNTHETIC, fictitious; not for navigation)",
                    new LocalRect(-12000, -12000, 18000, 18000),
                    GridSpacing: 150,
                    GeneralisationRadius: 150,
                    DepthContours: [0, 5, 10, 20],
                    SoundingSpacing: 1500,
                    MinimumDisplayScale: 350000,
                    OptimumDisplayScale: 90000,
                    MaximumDisplayScale: 22000,
                    FoidSubdivision: 2),
            ],
            DredgedChannels:
            [
                new DredgedChannel("Synthetic Sound approach channel (fictitious)", new LocalRect(0, 2850, 6000, 3150), MaintainedDepth: 12.5),
            ],
            Surfaces:
            [
                new S102SurfaceSpec("102ZZ00SPIT", new LocalRect(0, 2800, 6000, 3500), Spacing: 50, DeclareVerticalDatum: declareS102VerticalDatum),
            ],
            Stations: new S104StationsSpec("104ZZ00SPIT_STN", TimeSpan.FromMinutes(10)),
            WaterLevelGrids:
            [
                new S104GridSpec("104ZZ00SPIT_GRD", new LocalRect(3600, 2000, 6000, 4400), Spacing: 300, TimeSpan.FromHours(1)),
            ],
            Places: new Dictionary<string, (double X, double Y)>
            {
                // On S-102 nodes (50 m grid from 0, 2800).
                ["channel-west"] = (1000, 3000),
                // On an S-102 node and an S-104 grid node (300 m grid from 3600, 2000).
                ["channel-east"] = (5100, 2900),
                // Harbour band 2–5 m; the coastal cell's generalised spit dries here.
                ["spit-shoulder"] = (2900, 2200),
                ["start"] = (300, 3050),
                ["turn-west"] = (2300, 3050),
                ["turn-east"] = (3700, 3050),
                ["end"] = (5700, 3050),
            },
            Route: ["start", "turn-west", "spit-shoulder", "turn-east", "end"]);
    }
}
