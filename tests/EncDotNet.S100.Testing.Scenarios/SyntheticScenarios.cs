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
            Cells: SyntheticSoundCells,
            DredgedChannels: SyntheticSoundChannels,
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

    /// <summary>
    /// "Spit crossing" (#954, for #888's route checks): "Spit across scales"
    /// with three S-421 routes the route check is tested on, and the world's
    /// land and drying ground (<see cref="GroundTruth"/>) as the oracle.
    /// </summary>
    /// <remarks>
    /// <para>Every route has 100 m cross-track limits each side (<see cref="S421RouteBuilder.CrossTrackLimitMeters"/>).</para>
    /// <list type="table">
    /// <listheader><term>Route</term><description>What it crosses</description></listheader>
    /// <item><term><c>421ZZ00SPITX</c></term><description>
    /// South from <c>spit-approach</c> to <c>spit-west</c> in open water, east
    /// along <c>y = 1600 m</c> across the drying spit (it dries within about
    /// 90 m of <c>x = 3000 m</c>), then south from <c>spit-east</c> to
    /// <c>ashore</c>, over the drying foreshore (from about <c>y = 1240 m</c>)
    /// and onto the mainland (from about <c>y = 720 m</c>). It runs aground on
    /// purpose. Both cells cover it; the harbour cell is the finer, and the
    /// coastal cell's generalised land reaches about 150 m further north.
    /// </description></item>
    /// <item><term><c>421ZZ00CHAN</c></term><description>East along the dredged channel (<c>y = 3050 m</c>): nothing.</description></item>
    /// <item><term><c>421ZZ00OFFC</c></term><description>
    /// East from <c>east-berth</c> along the channel's line to <c>off-chart</c>
    /// (<c>x = 24 000 m</c>): it leaves the coastal cell, the last chart, at
    /// <c>x = 18 000 m</c>.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition SpitCrossing(ulong seed = DefaultSeed)
    {
        var spit = SpitAcrossScales(seed);
        var places = new Dictionary<string, (double X, double Y)>(spit.Places)
        {
            ["spit-approach"] = (2400, 2600),
            ["spit-west"] = (2400, 1600),
            ["spit-east"] = (3600, 1600),
            ["ashore"] = (3600, 400),
            ["west-entrance"] = (300, 3050),
            ["east-berth"] = (5700, 3050),
            ["off-chart"] = (24000, 3050),
        };
        var passage = new PassageValues(DraftMeters: 2.0, SquatMeters: 0.1, SpeedKnots: 6, RequiredUkcMeters: 0.5);
        var departure = new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc);
        S421RouteSpec[] routes =
        [
            new("421ZZ00SPITX", "ZZ00-SPIT-X", "Across the spit and ashore (SYNTHETIC, runs aground on purpose)",
                ["spit-approach", "spit-west", "spit-east", "ashore"], passage, departure),
            new("421ZZ00CHAN", "ZZ00-CHANNEL-2", "Synthetic Sound channel transit (fictitious)",
                ["west-entrance", "east-berth"], passage, departure),
            new("421ZZ00OFFC", "ZZ00-OFFCHART-1", "East off the charts (fictitious)",
                ["east-berth", "off-chart"], passage, departure),
        ];
        return spit with
        {
            Name = "spit-crossing",
            Places = places,
            Route = routes[0].Waypoints,
            Routes = routes,
        };
    }

    /// <summary>
    /// "Dangers near route" (#955, for #888's route checks): charted point
    /// dangers in and near a route's corridor, and a small shoal on its track,
    /// in "Spit across scales"'s Synthetic Sound.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The route (S-421 <c>421ZZ00DNGR</c>, 100 m cross-track limits each
    /// side) runs east along <c>y = 3300 m</c>, north of the dredged channel,
    /// from <c>dangers-west</c> (500 m) through <c>dangers-mid</c> (3000 m)
    /// to <c>dangers-east</c> (5500 m), in 13–14 m. Its passage values are a
    /// 10 m draft, 0,5 m squat and 1,5 m required UKC, so draft + squat + UKC
    /// is 12 m. The S-102 surface covers the whole corridor.
    /// </para>
    /// <para>
    /// A shoal (<c>dangers-shoal</c>, a 80 m mound at <c>x = 4000 m</c>)
    /// rises to about 7,6 m on the track, shallower than 10 m for about
    /// 120 m of it. The coastal cell's generalisation widens it.
    /// </para>
    /// <para>
    /// The dangers (<see cref="ScenarioDefinition.Hazards"/>), with the
    /// truth for a route check in <see cref="RouteHazards.For"/>: a 6,2 m
    /// wreck on the track; an 11 m obstruction 60 m to port (a danger only for
    /// the draft); a rock awash 80 m to starboard at the turn; a wreck of
    /// unknown depth 180 m to port (near the route); an obstruction 400 m
    /// north (off the route); an 18 m wreck on the track (no danger); a 5 m
    /// obstruction on the shoal (a danger there too); and a 3 m wreck on the track
    /// charted only in the coastal cell, which the harbour cell overrides.
    /// </para>
    /// </remarks>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition DangersNearRoute(ulong seed = DefaultSeed)
    {
        var spit = SpitAcrossScales(seed);
        var world = spit.World with
        {
            Seabed = spit.World.Seabed with
            {
                Features = [.. spit.World.Seabed.Features, new Mound(CentreX: 4000, CentreY: 3300, RadiusX: 80, RadiusY: 80, CrestDepth: 7)],
            },
        };
        var places = new Dictionary<string, (double X, double Y)>(spit.Places)
        {
            ["dangers-west"] = (500, 3300),
            ["dangers-mid"] = (3000, 3300),
            ["dangers-east"] = (5500, 3300),
            ["dangers-shoal"] = (4000, 3300),
        };
        S421RouteSpec[] routes =
        [
            new("421ZZ00DNGR", "ZZ00-DANGERS-1", "Past the wrecks north of the channel (fictitious)",
                ["dangers-west", "dangers-mid", "dangers-east"],
                new PassageValues(DraftMeters: 10, SquatMeters: 0.5, SpeedKnots: 8, RequiredUkcMeters: 1.5),
                new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc)),
        ];
        PointHazard[] hazards =
        [
            new("Synthetic Sound wreck on the track (fictitious)", HazardKind.Wreck, (1500, 3300), 6.2, WaterLevelEffect: 3),
            new("Synthetic Sound port obstruction (fictitious)", HazardKind.Obstruction, (2200, 3360), 11.0, WaterLevelEffect: 3),
            new("Synthetic Sound turn rock (fictitious)", HazardKind.Rock, (2990, 3220), null, WaterLevelEffect: 5),
            new("Synthetic Sound north wreck (fictitious)", HazardKind.Wreck, (4600, 3480), null, WaterLevelEffect: 3),
            new("Synthetic Sound far obstruction (fictitious)", HazardKind.Obstruction, (4800, 3700), 2.0, WaterLevelEffect: 3),
            new("Synthetic Sound deep wreck (fictitious)", HazardKind.Wreck, (5200, 3300), 18.0, WaterLevelEffect: 3),
            new("Synthetic Sound shoal obstruction (fictitious)", HazardKind.Obstruction, (4000, 3310), 5.0, WaterLevelEffect: 3),
            new("Synthetic Sound coastal-only wreck (fictitious)", HazardKind.Wreck, (2600, 3290), 3.0, WaterLevelEffect: 3, Cells: ["101ZZ00SPIT3"]),
        ];
        return spit with
        {
            Name = "dangers-near-route",
            World = world,
            Places = places,
            Route = routes[0].Waypoints,
            Routes = routes,
            Hazards = hazards,
        };
    }

    /// <summary>
    /// "Approach with tide window" (#942): a route from the sea buoy over the
    /// crest of the shoal north-west of the harbour (charted 4 m) and into the
    /// dredged channel, for a 6 m draft that can cross the crest only near
    /// high water.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// The route (S-421 <c>421ZZ00APPR</c>, 6 kn, 6,0 m draft, 0,3 m squat,
    /// 1,0 m required UKC) runs down <c>x = 1210 m</c>, over the crest at
    /// <c>shoal-crest</c>, to <c>channel-junction</c>, then east along the
    /// channel to <c>berth-approach</c>. It crosses the crest with enough
    /// water only when the tide there is above about 3,3 m, for a little over
    /// an hour either side of high water.
    /// </description></item>
    /// <item><description>
    /// An S-102 surface (50 m) covers the whole route, so every depth is
    /// S-102; the route runs 10 m east of a node column, so each sample's
    /// depth is the node to its south-west.
    /// </description></item>
    /// <item><description>
    /// S-104 stations only (10-minute records), so every water level is
    /// <c>SYN-W</c>'s, the nearest station.
    /// </description></item>
    /// <item><description>
    /// S-111 tidal streams over the Sound: a 300 m grid every 30 minutes and
    /// station series every 10 minutes, from the same tide.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition ApproachWithTideWindow(ulong seed = DefaultSeed)
    {
        var world = SyntheticWorld.SyntheticSound(seed);
        return new ScenarioDefinition(
            "approach-with-tide-window",
            world,
            Issued: new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc),
            WaterLevelStart: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            WaterLevelEnd: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            Cells: SyntheticSoundCells,
            DredgedChannels: SyntheticSoundChannels,
            Surfaces:
            [
                new S102SurfaceSpec("102ZZ00APPR", ApproachSurface, Spacing: 50),
            ],
            Stations: new S104StationsSpec("104ZZ00APPR_STN", TimeSpan.FromMinutes(10)),
            WaterLevelGrids: [],
            Places: ApproachPlaces,
            Route: ApproachRoute.Waypoints)
        {
            Routes = [ApproachRoute],
            CurrentGrids = [new S111GridSpec("111ZZ00APPR_GRD", new LocalRect(0, 2400, 6000, 6000), Spacing: 300, TimeSpan.FromMinutes(30))],
            CurrentStations = new S111StationsSpec("111ZZ00APPR_STN", TimeSpan.FromMinutes(10)),
        };
    }

    /// <summary>
    /// "Warnings on route" (#942, for #888's route checks): an S-421 route
    /// east along the channel and S-124 warnings that cross it, or lie near
    /// it, at different times.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The route (<c>421ZZ00WARN</c>) runs from <c>west-entrance</c> to
    /// <c>east-berth</c> along <c>y = 3050 m</c> at 8 kn, leaving at
    /// 06:00 UTC on 1 March 2026; it passes <c>x</c> metres east at
    /// 06:00 + (x − 300) / 4,116 seconds. The warnings (see
    /// <see cref="RouteWarnings"/>):
    /// </para>
    /// <list type="table">
    /// <listheader><term>No.</term><description>What, and its relation to the passage</description></listheader>
    /// <item><term>1</term><description>Survey operations, an area over the route at 2400–2800 m, 05:00–08:00: in force when the vessel crosses it.</description></item>
    /// <item><term>2</term><description>Cable laying, a line across the route at 4000 m, 12:00–18:00: crosses the route, but starts after the vessel has passed.</description></item>
    /// <item><term>3</term><description>A buoy off station 70 m north of the route at 1500 m, from 27 February, open-ended: in force, near the route.</description></item>
    /// <item><term>4</term><description>A drifting hazard far north of the route: not relevant.</description></item>
    /// <item><term>5</term><description>Diving operations, an area over the route at 4800–5100 m: cancelled by warning 6 before the passage.</description></item>
    /// <item><term>6</term><description>Diving operations moved north of the route, cancelling warning 5 (a "new dataset with cancellation").</description></item>
    /// <item><term>7</term><description>Towing operations, a line across the route at 1000 m whose cancellation date (05:30) is before the vessel reaches it.</description></item>
    /// </list>
    /// <para>
    /// <paramref name="s124Edition"/> 1 writes the warnings in Edition 1.0.0,
    /// which has no cancellation date, so there warning 7 stays in force.
    /// </para>
    /// <para>
    /// The route also crosses an S-122 seal sanctuary at 3100–3700 m whose
    /// seasonal speed restriction (1 March to 31 May) is in force for the
    /// passage (<see cref="SealSanctuary"/>), and S-125 buoys mark the
    /// channel (<see cref="ChannelBuoys"/>); buoy No 2 is the one warning 3
    /// reports off station.
    /// </para>
    /// </remarks>
    /// <param name="seed">The world's seed.</param>
    /// <param name="s124Edition">2 for S-124 Edition 2.0.0, 1 for Edition 1.0.0.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition WarningsOnRoute(ulong seed = DefaultSeed, int s124Edition = 2)
    {
        var world = SyntheticWorld.SyntheticSound(seed);
        var route = new S421RouteSpec(
            "421ZZ00WARN",
            "ZZ00-CHANNEL-1",
            "Synthetic Sound channel transit (fictitious)",
            ["west-entrance", "east-berth"],
            new PassageValues(DraftMeters: 7.0, SquatMeters: 0.4, SpeedKnots: 8, RequiredUkcMeters: 1.0),
            new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc));
        return new ScenarioDefinition(
            s124Edition == 2 ? "warnings-on-route" : "warnings-on-route-s124-ed1",
            world,
            Issued: new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc),
            WaterLevelStart: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            WaterLevelEnd: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            Cells: SyntheticSoundCells,
            DredgedChannels: SyntheticSoundChannels,
            Surfaces: [],
            Stations: null,
            WaterLevelGrids: [],
            Places: new Dictionary<string, (double X, double Y)>
            {
                ["west-entrance"] = (300, 3050),
                ["east-berth"] = (5700, 3050),
            },
            Route: route.Waypoints)
        {
            Routes = [route],
            Warnings = RouteWarnings,
            S124Edition = s124Edition,
            ProtectedAreas = [SealSanctuary],
            Aids = ChannelBuoys,
        };
    }

    /// <summary>
    /// "Restricted areas on route" (#956): the warnings-on-route transit
    /// (1 March, 06:00, 8 kn, 100 m cross-track limits) through S-101
    /// regulated areas and S-122 protected areas, each placed to give one
    /// kind of area finding.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>Area</term><description>Where, and what the area check should find</description></listheader>
    /// <item><term>Firing Range Alpha</term><description>Entry prohibited, military area, over the route at 600–900 m: enters a prohibited area.</description></item>
    /// <item><term>Cable Landing Zone</term><description>Anchoring and fishing prohibited, over the route at 1800–2000 m: crossed, for information.</description></item>
    /// <item><term>Range Danger Zone</term><description>Entry prohibited, 70 m north of the track at 2200–2400 m: inside the cross-track limits only.</description></item>
    /// <item><term>Coastal Exercise Area</term><description>A military practice area at 2600–2800 m charted only in the coastal cell, where the harbour cell charts the place: doesn't count.</description></item>
    /// <item><term>Spit Shoal Caution</term><description>A caution area over the route at 4300–4500 m: to check.</description></item>
    /// <item><term>Summer Speed Zone</term><description>Speed restricted from 1 June to 31 August, over the route at 5000–5200 m: not in force on 1 March.</description></item>
    /// <item><term>Seal Sanctuary (S-122)</term><description><see cref="SealSanctuary"/>, over the route at 3100–3700 m and in force: to check.</description></item>
    /// <item><term>Berth Reserve (S-122)</term><description>Round the east berth (5500–6000 m) with a hole from 5550 m, where the route ends: crossed for 50 m only.</description></item>
    /// </list>
    /// <para>The S-101 areas are charted in both cells unless noted.</para>
    /// </remarks>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition RestrictedAreasOnRoute(ulong seed = DefaultSeed) =>
        WarningsOnRoute(seed) with
        {
            Name = "restricted-areas-on-route",
            Warnings = [],
            Aids = null,
            ProtectedAreas = [SealSanctuary, BerthReserve],
            RegulatedAreas = RouteRegulatedAreas,
        };

    /// <summary>The S-101 regulated areas of <see cref="RestrictedAreasOnRoute"/>.</summary>
    public static IReadOnlyList<RegulatedAreaSpec> RouteRegulatedAreas { get; } =
    [
        new("Firing Range Alpha (fictitious)", "RestrictedArea", new LocalRect(600, 2900, 900, 3300), Restrictions: [7], Categories: [9]),
        new("Cable Landing Zone (fictitious)", "RestrictedArea", new LocalRect(1800, 2800, 2000, 3300), Restrictions: [1, 3], Categories: []),
        new("Range Danger Zone (fictitious)", "RestrictedArea", new LocalRect(2200, 3120, 2400, 3400), Restrictions: [7], Categories: [9]),
        new("Coastal Exercise Area (fictitious)", "MilitaryPracticeArea", new LocalRect(2600, 2950, 2800, 3150), Restrictions: [], Categories: [4], Cells: ["101ZZ00SPIT3"]),
        new("Spit Shoal Caution (fictitious)", "CautionArea", new LocalRect(4300, 3000, 4500, 3400), Restrictions: [], Categories: []),
        new("Summer Speed Zone (fictitious)", "RestrictedArea", new LocalRect(5000, 2900, 5200, 3200), Restrictions: [27], Categories: [])
        {
            Seasons = [new Season(new DayOfYear(6, 1), new DayOfYear(8, 31))],
        },
    ];

    /// <summary>
    /// A reserve round the east berth with a hole the route ends in: the
    /// route enters it at 5500 m and is in the hole from 5550 m.
    /// </summary>
    public static S122ProtectedAreaSpec BerthReserve { get; } = new(
        "122ZZ00BERTH",
        "Synthetic Sound Berth Reserve (fictitious)",
        new LocalRect(5500, 2700, 6000, 3400),
        Coverage: new LocalRect(0, 0, 6000, 6000),
        Seasons: [],
        Authority: "Synthetic Marine Conservation Agency (fictitious)")
    {
        Holes = [new LocalRect(5550, 2900, 5950, 3200)],
    };

    /// <summary>
    /// "Port plan vs own route" (#942): the port's S-129 plan for the
    /// approach route, and the vessel's own S-421 route, which rounds the
    /// shoal to the west instead of crossing its crest.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><description>
    /// The port's route is <see cref="ApproachWithTideWindow"/>'s
    /// (<c>421ZZ00APPR</c>, <c>ZZ00-APPROACH-1</c>), leaving at
    /// <see cref="PortPlanDeparture"/>, inside the tide window. The S-129
    /// plan <c>129ZZ00PORT</c> has control points at <c>shoal-crest</c> and
    /// <c>channel-junction</c> (waypoints) and <c>channel-mid</c> (halfway
    /// along the channel leg), a 1,0 m UKC limit (the route's required UKC)
    /// and a 0,5 m almost non-navigable margin. Its plan area takes in the
    /// tip of the spit, which is non-navigable, and the shoal's crest, which
    /// is almost non-navigable at high water.
    /// </description></item>
    /// <item><description>
    /// The own route (<c>421ZZ00OWN</c>, <c>ZZ00-OWN-1</c>) goes from the sea
    /// buoy to <c>own-west</c>, 310 m west of the crest, then to the
    /// junction and the berth approach: the crest control point is off it.
    /// </description></item>
    /// <item><description>
    /// S-102 as in the approach, and S-104 as a 200 m grid every 10 minutes
    /// (the S-129 fusion reads gridded S-104).
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition PortPlanVsOwnRoute(ulong seed = DefaultSeed)
    {
        var world = SyntheticWorld.SyntheticSound(seed);
        var port = ApproachRoute with { Departure = PortPlanDeparture };
        var own = new S421RouteSpec(
            "421ZZ00OWN",
            "ZZ00-OWN-1",
            "Own route round the shoal (fictitious)",
            ["sea-buoy", "own-west", "channel-junction", "berth-approach"],
            port.Passage,
            PortPlanDeparture);
        var places = new Dictionary<string, (double X, double Y)>(ApproachPlaces)
        {
            ["own-west"] = (900, 4400),
            ["channel-mid"] = (1755, 3050),
        };
        return new ScenarioDefinition(
            "port-plan-vs-own-route",
            world,
            Issued: new DateTime(2026, 2, 28, 12, 0, 0, DateTimeKind.Utc),
            WaterLevelStart: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            WaterLevelEnd: new DateTime(2026, 3, 3, 0, 0, 0, DateTimeKind.Utc),
            Cells: SyntheticSoundCells,
            DredgedChannels: SyntheticSoundChannels,
            Surfaces:
            [
                new S102SurfaceSpec("102ZZ00PORT", ApproachSurface, Spacing: 50),
            ],
            Stations: null,
            WaterLevelGrids:
            [
                new S104GridSpec("104ZZ00PORT_GRD", ApproachSurface, Spacing: 200, TimeSpan.FromMinutes(10)),
            ],
            Places: places,
            Route: port.Waypoints)
        {
            Routes = [port, own],
            UkcPlans =
            [
                new S129PlanSpec(
                    "129ZZ00PORT",
                    "PLAN.ZZ00.PORT",
                    port.DatasetName,
                    ["shoal-crest", "channel-junction", "channel-mid"],
                    UkcLimitMeters: 1.0,
                    AlmostMarginMeters: 0.5,
                    PlanArea: new LocalRect(600, 2200, 3400, 6000),
                    AreaSpacing: 50),
            ],
        };
    }

    /// <summary>
    /// The departure of the port's plan in <see cref="PortPlanVsOwnRoute"/>:
    /// the vessel crosses the shoal's crest near the afternoon high water.
    /// </summary>
    public static DateTime PortPlanDeparture { get; } = new(2026, 3, 1, 13, 15, 0, DateTimeKind.Utc);

    /// <summary>The S-102 surface over the approach: the route and its surroundings.</summary>
    public static LocalRect ApproachSurface { get; } = new(600, 2800, 2600, 6000);

    /// <summary>The approach route's places.</summary>
    public static IReadOnlyDictionary<string, (double X, double Y)> ApproachPlaces { get; } = new Dictionary<string, (double X, double Y)>
    {
        ["sea-buoy"] = (1210, 5800),
        // Over the shoal's crest (charted 4 m); the S-102 node to its south-west is (1200, 4400).
        ["shoal-crest"] = (1210, 4400),
        ["channel-junction"] = (1210, 3050),
        ["berth-approach"] = (2300, 3050),
    };

    /// <summary>The approach route: 6 kn, 6,0 m draft, 0,3 m squat, 1,0 m required UKC, leaving at 06:00 UTC on 1 March 2026.</summary>
    public static S421RouteSpec ApproachRoute { get; } = new(
        "421ZZ00APPR",
        "ZZ00-APPROACH-1",
        "Synthetic Sound approach over the shoal (fictitious)",
        ["sea-buoy", "shoal-crest", "channel-junction", "berth-approach"],
        new PassageValues(DraftMeters: 6.0, SquatMeters: 0.3, SpeedKnots: 6, RequiredUkcMeters: 1.0),
        new DateTime(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc));

    /// <summary>The warnings of <see cref="WarningsOnRoute"/>, numbered in the <c>ZZ00 LOCAL</c> series for 2026.</summary>
    public static IReadOnlyList<S124WarningSpec> RouteWarnings { get; } =
    [
        new S124WarningSpec(
            "124ZZ00W0001", 1, "Survey operations (fictitious)",
            "SYNTHETIC. Survey vessel working in the channel. Pass with caution.",
            TypeGeneral: 17,
            WarningShape.Area(new LocalRect(2400, 2900, 2800, 3200)),
            Published: new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc),
            ValidFrom: new DateTime(2026, 3, 1, 5, 0, 0, DateTimeKind.Utc),
            ValidTo: new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc)),
        new S124WarningSpec(
            "124ZZ00W0002", 2, "Cable laying (fictitious)",
            "SYNTHETIC. Cable laying across the channel. Keep clear.",
            TypeGeneral: 19,
            WarningShape.Line((4000, 2700), (4000, 3400)),
            Published: new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc),
            ValidFrom: new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc),
            ValidTo: new DateTime(2026, 3, 1, 18, 0, 0, DateTimeKind.Utc)),
        new S124WarningSpec(
            "124ZZ00W0003", 3, "Buoy off station (fictitious)",
            "SYNTHETIC. Channel buoy reported off station.",
            TypeGeneral: 1,
            WarningShape.Point(1500, 3120),
            Published: new DateTime(2026, 2, 27, 0, 0, 0, DateTimeKind.Utc),
            ValidFrom: new DateTime(2026, 2, 27, 0, 0, 0, DateTimeKind.Utc),
            ValidTo: null),
        new S124WarningSpec(
            "124ZZ00W0004", 4, "Drifting hazard (fictitious)",
            "SYNTHETIC. Container reported adrift.",
            TypeGeneral: 3,
            WarningShape.Point(3000, 5500),
            Published: new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc),
            ValidFrom: new DateTime(2026, 2, 28, 9, 0, 0, DateTimeKind.Utc),
            ValidTo: null),
        new S124WarningSpec(
            "124ZZ00W0005", 5, "Diving operations (fictitious)",
            "SYNTHETIC. Divers in the water in the channel.",
            TypeGeneral: 17,
            WarningShape.Area(new LocalRect(4800, 2950, 5100, 3150)),
            Published: new DateTime(2026, 2, 27, 0, 0, 0, DateTimeKind.Utc),
            ValidFrom: new DateTime(2026, 2, 27, 0, 0, 0, DateTimeKind.Utc),
            ValidTo: null),
        new S124WarningSpec(
            "124ZZ00W0006", 6, "Diving operations moved (fictitious)",
            "SYNTHETIC. Diving operations moved north of the channel. Warning 0005/2026 cancelled.",
            TypeGeneral: 17,
            WarningShape.Area(new LocalRect(4800, 3600, 5100, 3800)),
            Published: new DateTime(2026, 3, 1, 5, 0, 0, DateTimeKind.Utc),
            ValidFrom: new DateTime(2026, 3, 1, 5, 0, 0, DateTimeKind.Utc),
            ValidTo: null)
        {
            Cancels = [5],
        },
        new S124WarningSpec(
            "124ZZ00W0007", 7, "Towing operations (fictitious)",
            "SYNTHETIC. Tow crossing the channel.",
            TypeGeneral: 18,
            WarningShape.Line((1000, 2700), (1000, 3400)),
            Published: new DateTime(2026, 2, 28, 21, 0, 0, DateTimeKind.Utc),
            ValidFrom: new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            ValidTo: new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc))
        {
            CancellationDate = new DateTime(2026, 3, 1, 5, 30, 0, DateTimeKind.Utc),
        },
    ];

    /// <summary>
    /// A seal sanctuary across the channel east of the survey area, speed
    /// restricted from 1 March to 31 May each year: in force for the
    /// warnings-on-route transit on 1 March.
    /// </summary>
    public static S122ProtectedAreaSpec SealSanctuary { get; } = new(
        "122ZZ00SEAL",
        "Synthetic Sound Seal Sanctuary (fictitious)",
        new LocalRect(3100, 2500, 3700, 3700),
        Coverage: new LocalRect(0, 0, 6000, 6000),
        Seasons: [new Season(new DayOfYear(3, 1), new DayOfYear(5, 31))],
        Authority: "Synthetic Marine Conservation Agency (fictitious)");

    /// <summary>
    /// IALA A buoys along the approach channel (buoyage eastward, so port
    /// hand on the north edge, y = 3150 m): a fairway buoy at the west
    /// entrance, three gates of lateral buoys, and a north cardinal off the
    /// spit. Buoy 0002 is the one warning 0003/2026 reports off station.
    /// </summary>
    public static S125AidsSpec ChannelBuoys { get; } = new(
        "125ZZ00CHAN",
        "Synthetic Sound approach channel buoyage",
        [
            new BuoySpec("ZZ00-0001", "Sound Fairway (fictitious)", BuoyMark.SafeWater, 100, 3000, new BuoyLight(Colour: 1, Character: 3, "(1)", 10, 6)),
            new BuoySpec("ZZ00-0002", "No 2 (fictitious)", BuoyMark.PortHand, 1500, 3150, new BuoyLight(Colour: 3, Character: 2, "(1)", 4, 3)),
            new BuoySpec("ZZ00-0003", "No 1 (fictitious)", BuoyMark.StarboardHand, 1500, 2850, new BuoyLight(Colour: 4, Character: 2, "(1)", 4, 3)),
            new BuoySpec("ZZ00-0004", "No 4 (fictitious)", BuoyMark.PortHand, 3000, 3150),
            new BuoySpec("ZZ00-0005", "No 3 (fictitious)", BuoyMark.StarboardHand, 3000, 2850),
            new BuoySpec("ZZ00-0006", "No 6 (fictitious)", BuoyMark.PortHand, 4500, 3150, new BuoyLight(Colour: 3, Character: 2, "(2)", 6, 3)),
            new BuoySpec("ZZ00-0007", "No 5 (fictitious)", BuoyMark.StarboardHand, 4500, 2850, new BuoyLight(Colour: 4, Character: 2, "(2)", 6, 3)),
            new BuoySpec("ZZ00-0008", "Spit North (fictitious)", BuoyMark.NorthCardinal, 2900, 2700, new BuoyLight(Colour: 1, Character: 4, null, null, 5)),
        ]);

    /// <summary>
    /// The S-101 cells every Synthetic Sound scenario charts: a harbour-band
    /// cell over 0–6000 m and a coastal-band cell around it (see
    /// <see cref="SpitAcrossScales"/>).
    /// </summary>
    public static IReadOnlyList<S101CellSpec> SyntheticSoundCells { get; } =
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
    ];

    /// <summary>The dredged approach channel along <c>y = 3000 m</c>, maintained to 12,5 m.</summary>
    public static IReadOnlyList<DredgedChannel> SyntheticSoundChannels { get; } =
    [
        new DredgedChannel("Synthetic Sound approach channel (fictitious)", new LocalRect(0, 2850, 6000, 3150), MaintainedDepth: 12.5),
    ];
}
