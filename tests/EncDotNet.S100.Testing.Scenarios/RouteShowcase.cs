using EncDotNet.S100.Testing.Scenarios.World;
using EncDotNet.S100.Testing.Scenarios.Writers;

namespace EncDotNet.S100.Testing.Scenarios;

/// <summary>One route of the <see cref="RouteShowcase"/> and the state it shows.</summary>
/// <param name="Route">The S-421 route.</param>
/// <param name="State">The state it shows in Check route, in a few words.</param>
/// <param name="LookFor">What to look at in the viewer.</param>
public sealed record ShowcaseRoute(S421RouteSpec Route, string State, string LookFor);

/// <summary>
/// "Route showcase": every principal state a route can be in, side by side
/// in one exchange set over Synthetic Sound, for fit and finish work on the
/// viewer's route UX. Load it once and select each route in turn.
/// </summary>
/// <remarks>
/// <para>
/// It merges the products of <see cref="SyntheticScenarios.SpitCrossing"/>,
/// <see cref="SyntheticScenarios.DangersNearRoute"/>,
/// <see cref="SyntheticScenarios.ApproachWithTideWindow"/>,
/// <see cref="SyntheticScenarios.WarningsOnRoute"/>,
/// <see cref="SyntheticScenarios.RestrictedAreasOnRoute"/> and
/// <see cref="SyntheticScenarios.PortPlanVsOwnRoute"/> (their hazards,
/// regulated areas, warnings, protected areas, buoys and S-129 plan), and
/// adds an S-102 surface over open water north of the shoals, where the
/// clean routes run in parallel lanes 150 m apart.
/// </para>
/// <para>
/// Each route is named for its state with a number, so the Routes panel
/// lists them in <see cref="Routes"/> order. The routes that share a track
/// in the merged scenarios are moved apart by 40–50 m so that each can be
/// told from the others on the map.
/// </para>
/// </remarks>
public static class RouteShowcase
{
    /// <summary>The scenario's name and folder.</summary>
    public const string Name = "route-showcase";

    /// <summary>The open-water S-102 surface the clean lanes run over.</summary>
    public static LocalRect NorthSurface { get; } = new(1800, 4950, 6000, 5950);

    private static readonly DateTime Morning = new(2026, 3, 1, 6, 0, 0, DateTimeKind.Utc);

    private static readonly PassageValues Coaster = new(DraftMeters: 5.0, SquatMeters: 0.3, SpeedKnots: 8, RequiredUkcMeters: 1.0);

    private static readonly Dictionary<string, (double X, double Y)> ShowcasePlaces = new()
    {
        // Open water north of the shoals: one lane per clean state.
        ["clear-west"] = (2000, 5200),
        ["clear-east"] = (5800, 5200),
        ["tight-west"] = (2000, 5350),
        ["tight-east"] = (5800, 5350),
        ["rtz-west"] = (3400, 5500),
        ["rtz-east"] = (5800, 5500),
        ["nodraft-west"] = (2000, 5650),
        ["nodraft-east"] = (5800, 5650),
        ["late-west"] = (2000, 5800),
        ["late-east"] = (5800, 5800),
        ["nomargin-west"] = (2000, 5925),
        ["nomargin-east"] = (5800, 5925),

        // West of the S-102 surfaces (S-101 only) into the approach surface.
        ["mixed-west"] = (-1500, 5050),
        ["mixed-east"] = (3500, 5050),

        // Down the shoal's east flank, 130 m east of its crest.
        ["bands-north"] = (1330, 5000),
        ["bands-south"] = (1330, 3800),

        // The approach over the shoal at the wrong time, 50 m east of the port's route.
        ["low-sea-buoy"] = (1260, 5800),
        ["low-crest"] = (1260, 4400),
        ["low-junction"] = (1260, 3100),
        ["low-berth"] = (2300, 3100),

        // The evening channel transit, 50 m south of the morning's.
        ["evening-west"] = (300, 3000),
        ["evening-east"] = (5700, 3000),

        // Off the harbour cell to the west: S-101 coastal depths only, many legs.
        ["zig-1"] = (-500, 5800),
        ["zig-2"] = (-1500, 5000),
        ["zig-3"] = (-2500, 5800),
        ["zig-4"] = (-3500, 5000),
        ["zig-5"] = (-4500, 5800),
        ["zig-6"] = (-5500, 5000),
        ["zig-7"] = (-6500, 5800),
        ["zig-8"] = (-7500, 5000),
        ["zig-9"] = (-8500, 5800),
        ["zig-10"] = (-9500, 5000),
        ["zig-11"] = (-10500, 5800),
        ["zig-12"] = (-11500, 5000),
    };

    /// <summary>
    /// The routes, in showcase order, each with the state it shows: the
    /// clearance states first (01–09), then the route checks (10–14), then
    /// the list and editing states.
    /// </summary>
    public static IReadOnlyList<ShowcaseRoute> Routes { get; } =
    [
        new(new S421RouteSpec("421ZZ00SC01", "ZZ00-SHOWCASE-01", "01 Clear passage",
                ["clear-west", "clear-east"], Coaster, Morning),
            "Meets your settings",
            "A flat green clearance band well above the required UKC, S-102 + S-104 sources, nothing in the findings list; departure window open all day."),
        new(new S421RouteSpec("421ZZ00SC02", "ZZ00-SHOWCASE-02", "02 Tight clearance",
                ["tight-west", "tight-east"], Coaster with { DraftMeters = 14.7 }, Morning),
            "Tight",
            "The clearance band just above the required UKC line, amber; departure sweep shows tight hours at low water."),
        new(new S421RouteSpec("421ZZ00BAND", "ZZ00-SHOWCASE-BANDS", "03 Meets, tight and below across the shoal's flank",
                ["bands-north", "bands-south"], SyntheticScenarios.ApproachRoute.Passage with { DraftMeters = BandsDraft }, Morning),
            "All three bands in one profile",
            "Green in deep water, amber on the flank, red over the shallowest part: the profile and the route colouring change state along the route."),
        new(new S421RouteSpec("421ZZ00SC05", "ZZ00-SHOWCASE-05", "04 Below UKC setting over the shoal crest",
                ["low-sea-buoy", "low-crest", "low-junction", "low-berth"], SyntheticScenarios.ApproachRoute.Passage, Morning),
            "Below your UKC setting",
            "Must review; the clearance dips below zero over the 4 m crest; departure sweep shows the high-water windows."),
        new(SyntheticScenarios.ApproachRoute with { Name = "05 Approach inside the tide window (port's S-129 plan)", Departure = SyntheticScenarios.PortPlanDeparture },
            "Tight at the crest, in the tide window",
            "Same crossing near high water: the crest clears, tightly; the port's S-129 plan (129ZZ00PORT) is for this route; route checks still flag the shoal and safety contour."),
        new(new S421RouteSpec("421ZZ00MIXD", "ZZ00-SHOWCASE-MIXED", "06 S-101 depths, then S-102",
                ["mixed-west", "mixed-east"], Coaster, Morning),
            "Lower confidence, then full confidence",
            "The first stretch has S-101 depths only (dashed band, lower confidence); the profile turns solid where the S-102 surface begins."),
        new(new S421RouteSpec("421ZZ00NOUK", "ZZ00-SHOWCASE-NO-UKC", "07 Draft but no required UKC",
                ["nomargin-west", "nomargin-east"], Coaster, Morning) { WritesRequiredUkc = false },
            "No margin set",
            "The clearance is computed and drawn, but there is no required-UKC line to judge it against."),
        new(new S421RouteSpec("421ZZ00SC04", "ZZ00-SHOWCASE-04", "08 Water level ends before arrival",
                ["late-west", "late-east"], Coaster, new DateTime(2026, 3, 2, 23, 50, 0, DateTimeKind.Utc)),
            "Data ends",
            "Neutral verdict: the S-104 data ends (3 March 00:00) before the vessel arrives; the profile stops where the tide does."),
        new(new S421RouteSpec("421ZZ00SC03", "ZZ00-SHOWCASE-03", "09 No draft set",
                ["nodraft-west", "nodraft-east"], Coaster, Morning) { WritesDraft = false },
            "No draft",
            "Clearance row \"Not checked\" with + Add draft and no profile; route checks still run."),
        new(new S421RouteSpec("421ZZ00SPITX", "ZZ00-SPIT-X", "10 Across the spit and ashore",
                ["spit-approach", "spit-west", "spit-east", "ashore"],
                new PassageValues(DraftMeters: 2.0, SquatMeters: 0.1, SpeedKnots: 6, RequiredUkcMeters: 0.5), Morning),
            "Crosses drying ground and land",
            "Must review led by Crosses land; route findings list with leg badges; S-101 depth (lower confidence)."),
        new(new S421RouteSpec("421ZZ00DNGR", "ZZ00-DANGERS-1", "11 Dangers in the corridor",
                ["dangers-west", "dangers-mid", "dangers-east"],
                new PassageValues(DraftMeters: 10, SquatMeters: 0.5, SpeedKnots: 8, RequiredUkcMeters: 1.5), Morning),
            "Dangers in the corridor",
            "Wrecks, rock and obstructions on, near and off the track; the shoal makes it Below your UKC setting too, and the channel's areas and warnings are close by."),
        new(new S421RouteSpec("421ZZ00WARN", "ZZ00-CHANNEL-1", "12 Channel transit, morning (warnings in force)",
                ["west-entrance", "east-berth"],
                new PassageValues(DraftMeters: 7.0, SquatMeters: 0.4, SpeedKnots: 8, RequiredUkcMeters: 1.0), Morning),
            "Warnings and restricted areas",
            "S-124 warnings on and near the route (in force, later, cancelled), S-101 regulated areas, the S-122 sanctuary and berth reserve, S-125 buoys: the busiest findings list."),
        new(new S421RouteSpec("421ZZ00EVEN", "ZZ00-CHANNEL-3", "13 Channel transit, evening (warnings expired)",
                ["evening-west", "evening-east"],
                new PassageValues(DraftMeters: 7.0, SquatMeters: 0.4, SpeedKnots: 8, RequiredUkcMeters: 1.0), new DateTime(2026, 3, 1, 20, 0, 0, DateTimeKind.Utc)),
            "Same areas, warnings no longer in force",
            "Compare with 12: the warnings are no longer in force, the firing range and other areas stay."),
        new(new S421RouteSpec("421ZZ00OFFC", "ZZ00-OFFCHART-1", "14 East off the charts",
                ["east-berth", "off-chart"],
                new PassageValues(DraftMeters: 2.0, SquatMeters: 0.1, SpeedKnots: 6, RequiredUkcMeters: 0.5), Morning),
            "Outside chart coverage / incomplete data",
            "Leaves the last chart at x = 18 km: no charted depth (dotted, no data) on the last stretch."),
        new(new S421RouteSpec("421ZZ00OWN", "ZZ00-OWN-1", "15 Own route round the shoal (S-129 control point off route)",
                ["sea-buoy", "own-west", "channel-junction", "berth-approach"], SyntheticScenarios.ApproachRoute.Passage, SyntheticScenarios.PortPlanDeparture),
            "Differs from the port's plan",
            "Rounds the shoal west of the crest, so the S-129 crest control point is off the route."),
        new(new S421RouteSpec("421ZZ00SC13", "ZZ00-SHOWCASE-13",
                "16 Long coastal passage with twelve waypoints and a deliberately long name to test truncation in lists and cards",
                ["zig-1", "zig-2", "zig-3", "zig-4", "zig-5", "zig-6", "zig-7", "zig-8", "zig-9", "zig-10", "zig-11", "zig-12"],
                Coaster, Morning),
            "Many legs, S-101 depths only, tide runs out",
            "Long name in the list and cards, eleven legs in the leg table, the lower-confidence (dashed) band from S-101-only depths, and Incomplete data where the last legs leave the S-104 coverage."),
    ];

    // The draft that puts route 03's shallowest stretch below the required
    // UKC at its morning ETA while its deep ends meet it.
    private const double BandsDraft = 5.2;

    /// <summary>The route written as RTZ for the received-RTZ state; not in the exchange set.</summary>
    public static S421RouteSpec RtzRoute { get; } = new("SC14", "ZZ00-SHOWCASE-14", "17 Received RTZ route",
        ["rtz-west", "rtz-east"], Coaster, Morning);

    /// <summary>The route showcase scenario.</summary>
    /// <param name="seed">The world's seed.</param>
    /// <returns>The scenario.</returns>
    public static ScenarioDefinition Definition(ulong seed = SyntheticScenarios.DefaultSeed)
    {
        var spit = SyntheticScenarios.SpitCrossing(seed);
        var dangers = SyntheticScenarios.DangersNearRoute(seed);
        var port = SyntheticScenarios.PortPlanVsOwnRoute(seed);
        var places = new Dictionary<string, (double X, double Y)>(spit.Places);
        foreach (var source in new[] { dangers.Places, port.Places, ShowcasePlaces })
        {
            foreach (var (name, point) in source)
            {
                places[name] = point;
            }
        }

        var routes = Routes.Select(r => r.Route).ToList();
        return spit with
        {
            Name = Name,
            World = dangers.World,
            Places = places,
            Route = routes[0].Waypoints,
            Routes = routes,
            Surfaces =
            [
                new S102SurfaceSpec("102ZZ00SPIT", new LocalRect(0, 2800, 6000, 3500), Spacing: 50),
                new S102SurfaceSpec("102ZZ00APPR", SyntheticScenarios.ApproachSurface, Spacing: 50),
                new S102SurfaceSpec("102ZZ00NRTH", NorthSurface, Spacing: 50),
            ],
            Stations = new S104StationsSpec("104ZZ00SHOW_STN", TimeSpan.FromMinutes(10)),
            WaterLevelGrids =
            [
                new S104GridSpec("104ZZ00SHOW_GRD", new LocalRect(0, 2000, 6000, 6000), Spacing: 200, TimeSpan.FromMinutes(10)),
            ],
            CurrentGrids = [new S111GridSpec("111ZZ00SHOW_GRD", new LocalRect(0, 2400, 6000, 6000), Spacing: 300, TimeSpan.FromMinutes(30))],
            CurrentStations = new S111StationsSpec("111ZZ00SHOW_STN", TimeSpan.FromMinutes(10)),
            Hazards = dangers.Hazards,
            RegulatedAreas = SyntheticScenarios.RouteRegulatedAreas,
            // The drifting hazard moves north, clear of the open-water lanes.
            Warnings = SyntheticScenarios.RouteWarnings
                .Select(w => w.WarningNumber == 4 ? w with { Shape = WarningShape.Point(3000, 8500) } : w)
                .ToList(),
            ProtectedAreas = [SyntheticScenarios.SealSanctuary, SyntheticScenarios.BerthReserve],
            Aids = SyntheticScenarios.ChannelBuoys,
            UkcPlans = port.UkcPlans,
        };
    }
}
