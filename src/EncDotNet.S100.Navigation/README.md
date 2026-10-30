# EncDotNet.S100.Navigation

`EncDotNet.S100.Navigation` holds passage-planning calculations that combine
S-100 products: leg distances and ETAs, and an under-keel clearance (UKC)
check along a route. The check computes, at each sample's ETA:

```text
UKC = charted depth + water level − draft − squat
```

and compares it with the required UKC. Charted depth comes from S-102, with
S-101 as a low-confidence fallback; water level comes from S-104. The package
depends only on `EncDotNet.S100.Core`. Data reaches it through small source
interfaces, gathered in one `RouteCheckSources` bundle, so it never loads a
file itself. `CatalogRouteCheckSources` in `EncDotNet.S100.Datasets.Pipelines`
builds the bundle from loaded datasets, and `RouteCheckSourceCache` keeps one
per set of loaded datasets. Every source is safe to call from several threads
at once, so one bundle can serve concurrent checks.

This is a calculation aid for exploring data. It isn't for navigation.

## Install

```bash
dotnet add package EncDotNet.S100.Navigation
```

To check a passage against loaded datasets, also install
`EncDotNet.S100.Datasets.Pipelines` and a CRS backend such as
`EncDotNet.S100.Crs.ProjNet`.

## Check a route

`RouteChecker` runs every check on a plan and returns one `RouteCheckReport`:
the findings of the chart, area, warning and clearance checks in one sorted
list, a verdict, and whether each check ran. Create one checker per sources
bundle and keep it while the data is loaded:

```csharp
var sources = CatalogRouteCheckSources.FromDatasets(catalog.Datasets, new ProjNetCrsTransformFactory());
var checker = new RouteChecker(sources);

var report = checker.Check(plan, RouteCheckSettings.FromMarinerSettings(marinerSettings, DateTimeOffset.UtcNow));

Console.WriteLine($"{report.Verdict.Kind}: {report.Verdict.Headline?.Title}");   // MustReview: Crosses land
foreach (var check in report.Checks)
{
    Console.WriteLine($"{check.Name}: {check.State} {check.Reason}");             // clearance: Skipped draft_missing
}
```

- **Verdict.** `MustReview` when any finding is Must review or the clearance
  is below your setting; then `WaterLevelEndsBeforeArrival`; then `Check` for
  Check findings or a tight or incomplete clearance; else `Clear`. The
  headline is the Must review finding with the lowest
  `RouteFindingCode.HeadlineRank`, or the first Check finding.
- **Skipped checks.** A check that can't run says why rather than finding
  nothing (`RouteCheckReasons`): no charts, areas, warnings, depth or water
  level in the sources, or a leg without a draft (`draft_missing`). Without
  a safety contour and safety depth the chart check runs `Partial`
  (`safety_settings_missing`): land, drying ground and coverage only.
- **Reuse.** The checks keep what the last run computed. The chart sweep is
  reused while the legs, depth settings and corridor override are unchanged,
  and the clearance check's depth profile is reused leg by leg, so changing
  the departure resamples nothing and moving a waypoint resamples only the
  legs that meet there. `RouteCheckSettings.DepartureWindow` adds a departure
  sweep (`report.Departures`), which a departure change alone doesn't redo.
- **Shared work.** `RouteTrack` (the legs' distances and corridor
  half-widths, `PositionAt` and `PathBetween`) and the `PassageSchedule` are
  built once per run and shared by every check through `RouteCheckContext`.
- **Your own checks.** Implement `IRouteCheck` and pass it as
  `additionalChecks`, or pass exactly the checks to run, such as
  `new RouteChecker(sources, [new ClearanceCheck()])`.

`RouteCheckOptions` tunes every check at once: `DefaultLimit` (50 m) and
`Formatter` apply to all of them, and `Chart`, `Areas`, `Warnings` and
`Clearance` tune each one. `Check` may be called from several threads at once.

[How a route check works](../../docs/how-a-route-check-works.md) explains
which dataset feeds each check, the time and keel rules, the verdict and
each check's status. The
[route check sample](../../samples/EncDotNet.S100.Samples.RouteCheck/README.md)
runs a check end to end.

## Run one check

Each built-in check is an `IRouteCheck<TDetail>`, and its `Run` returns a
`RouteCheckOutcome<TDetail>`: the findings, the state and reason, and the
check's own result, typed, with no cast. Build a `RouteCheckContext` for the
run and pass it:

```csharp
var context = new RouteCheckContext(plan, settings, sources);
var chart = new ChartCheck().Run(context);

Console.WriteLine($"{chart.State}: {chart.Findings.Count} findings");
Console.WriteLine(chart.Detail?.LengthMeters);                    // null only when skipped
```

| Check | `Name` | `Detail` | In the report |
|---|---|---|---|
| `ChartCheck` | `chart` | `ChartCheckResult`: each leg's corridor | `report.Chart` |
| `AreaCheck` | `areas` | `AreaCheckResult`: every area near the route | `report.Areas` |
| `WarningCheck` | `warnings` | `WarningCheckResult`: every warning near the route | `report.Warnings` |
| `ClearanceCheck` | `clearance` | `ClearanceCheckResult`: the `PassageCheckResult` and the departure sweep | `report.Clearance`, `report.Departures` |

A check object keeps what its last run computed, as it does inside a
checker, so keep one per sources bundle when you run it often. A check of
your own can implement `IRouteCheck<TDetail>` too, and
`report.Detail<TDetail>(name)` gives its result back.

## Check a passage

This example builds the sources from a catalog of loaded datasets, describes a
two-leg passage, and checks its under-keel clearance:

```csharp
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Quantities;

IDatasetCatalog catalog = /* S-102, S-101 and S-104 datasets */;
var sources = CatalogRouteCheckSources.FromDatasets(catalog.Datasets, new ProjNetCrsTransformFactory());

var a = new GeoPosition(50.672, -0.978);
var b = new GeoPosition(50.725, -1.040);
var c = new GeoPosition(50.763, -1.105);
PassageLeg Leg(GeoPosition from, GeoPosition to, double knots) => new()
{
    Points = [from, to],
    Speed = Speed.FromKnots(knots),
    Draft = Depth.FromMetres(11.5),
    Squat = Depth.FromMetres(0.3),
    RequiredUkc = Depth.FromMetres(1.0),
};

var plan = new PassagePlan([Leg(a, b, 12), Leg(b, c, 8)], DateTimeOffset.Parse("2021-04-01T07:10:00Z"));
var settings = new RouteCheckSettings { At = DateTimeOffset.UtcNow };   // used only when the plan has no departure

var result = new ClearanceCheck().Run(new RouteCheckContext(plan, settings, sources)).Detail!.Clearance;

Console.WriteLine(result.Verdict);                        // BelowSetting, MeetsSettings, ...
Console.WriteLine(result.Minimum?.ClearanceMeters);       // smallest margin's UKC
Console.WriteLine(result.Minimum?.Time);                  // and when it occurs
foreach (var gap in result.Gaps)
{
    Console.WriteLine($"{gap.Kind}: {gap.StartDistanceMeters:F0}–{gap.EndDistanceMeters:F0} m");
}
```

Each `PassageLeg` carries the effective values for that leg: its points,
geometry, cross-track limits, speed, draft, squat and required UKC. Resolve
route defaults and per-leg overrides before you build it. Every check reads
the same legs. The clearance check needs a draft on every leg, and is
skipped (`draft_missing`) without one; everything else is optional:

- Without a speed, there are no ETAs, and every sample uses the departure
  time. Without a departure, the plan leaves at `RouteCheckSettings.At`.
- Without a required UKC, the check uses 0 m and sets `NoMarginSet`.
- Squat is a fixed allowance; it isn't modelled from speed.
- The speed is the speed over ground. S-111 currents aren't applied.

## What the result says

Each sample stands for a stretch of the leg of at most
`PassageCheckOptions.SampleSpacingMeters` (default 50 m) and carries the
shallowest depth found in it. Along S-102 the check steps at half the finest
grid cell, so it doesn't skip any cell the leg crosses. A sample's `State` is
one of:

| State | Meaning |
|---|---|
| `MeetsSettings` | UKC is at least 0,5 m above the required UKC. |
| `Tight` | UKC meets the required UKC with less than 0,5 m to spare. |
| `BelowSetting` | UKC is less than the required UKC. |
| `NoData` | There's no charted depth, or no water level that can be applied. |

The water level counts as missing outside the S-104 extent or time range,
where a value is NoData, or where its vertical datum differs from the charted
depth's. A datum mismatch is reported as a gap and the two values are never
added. A datum that isn't declared, or isn't an S-100 vertical datum code,
can't be confirmed. In that case the water level is applied and the sample is
lower confidence.

Confidence is separate from the state. A sample's `Confidence` lists why it's
lower confidence (`ChartedDepthFromS101`, `DatumUnconfirmed`), and the state
still says whether the clearance meets, is tight or is below. Legs, the
minimum, the whole result and each departure option combine their samples'
reasons the same way. A departure that meets your settings with lower
confidence still meets them.

`Verdict` follows this priority:

1. `WaterLevelEndsBeforeArrival`
2. `BelowSetting`
3. `IncompleteData`
4. `MeetsSettingsTight`
5. `MeetsSettings`

`Gaps` and `LowConfidence` list the affected stretches, with their distances
and times. Stretches of one kind less than 100 m apart are reported as one,
and low-confidence stretches shorter than 100 m are left out (the samples
still carry their confidence); `PassageCheckOptions.StretchMergeGapMeters`
and `MinimumLowConfidenceStretchMeters` tune this. `Legs` summarises each leg, and `Sources` names the datasets,
stations and datums that were used.

## Recompute cheaply

The clearance check splits its work in two. Sampling walks the legs, reads
the charted depths and finds the covering S-104 series: the slow, spatial
step, which depends only on the legs' points and geometry. Evaluating
applies times and vessel values to those samples. A `ClearanceCheck` keeps
its last samples for the same sources bundle and options, so:

- changing the departure, speed, draft, squat or required UKC samples
  nothing again
- moving a waypoint samples only the legs whose points changed

`RouteCheckSettings.DepartureWindow` adds a departure sweep, evaluated over
the same samples, in `ClearanceCheckResult.Departures` (`report.Departures`):

- `DepartureRange.Between(from, to, step)` sweeps fixed departures.
- `DepartureRange.WaterLevelData(step)` sweeps the whole time range of the
  S-104 data that covers the route, starting at the first whole step. Over
  a long forecast, the step widens so the sweep stays under `maxDepartures`
  (default 2000). Without S-104 coverage there's no sweep.

`DepartureSweep.Windows()` returns the runs of departures that meet the
settings, `NearestMeeting(departure)` the meeting departure closest to one,
and `ArrivalAfterDataEnds()` the departures that arrive after the water-level
data ends. The check keeps its last sweep too, which a changed departure
alone doesn't redo.

## ETAs on their own

`PassageSchedule.Compute(legs, departure)` returns each leg's distance,
duration and start and end ETA, and the arrival time. `EtaAt(leg, distance)`
gives the ETA at any point along a leg.

## Check a route against the chart

`ChartCheck` checks a route against charted land, drying ground and the
extent of the loaded charts, and, given the mariner's depths, against charted
dangers and the safety contour. It reads charted features through
`IRouteChartSource` (`sources.Charts`), and S-102 through the same
`IChartedDepthSource` the clearance check uses (`sources.Depth`), reading only
its S-102 surfaces (`SampleBathymetry`):

```csharp
var sources = CatalogRouteCheckSources.FromDatasets(catalog.Datasets, new ProjNetCrsTransformFactory());

var plan = new PassagePlan(
[
    new PassageLeg
    {
        Points = [a, b],
        PortLimit = Length.FromMetres(185),
        StarboardLimit = Length.FromMetres(185),
        Draft = Depth.FromMetres(11.5),       // this leg's dangers are also judged against draft + squat + UKC
        Squat = Depth.FromMetres(0.3),
        RequiredUkc = Depth.FromMetres(1.0),
    },
    new PassageLeg { Points = [b, c] },       // both sides use RouteCheckOptions.DefaultLimit (50 m)
]);

// The mariner's safety contour and safety depth turn on the depth and danger checks.
var settings = RouteCheckSettings.FromMarinerSettings(marinerSettings, DateTimeOffset.UtcNow);

foreach (var finding in new ChartCheck().Run(new RouteCheckContext(plan, settings, sources)).Findings)
{
    Console.WriteLine($"{finding.Group} {finding.Code} at {finding.Distance?.TotalMetres:F0} m: {finding.Meta}");
}
```

Each leg's corridor reaches `PortLimit` to port of the track and
`StarboardLimit` to starboard. Both are **half-widths**, measured from the
track to one edge, the same as S-421 and RTZ cross-track limits, so the full
corridor is their sum. `RouteCheckSettings.CorridorOverride` replaces every
leg's limits with one half-width.

| Code | Group | When |
| --- | --- | --- |
| `route_check.crosses_land` | MustReview | The track crosses a `LandArea`, or a `Coastline` where no land area is charted |
| `route_check.crosses_drying` | MustReview | The track crosses a `DepthArea` that dries (DRVAL1 below the datum) |
| `route_check.land_in_corridor` | Check | Land reaches inside the cross-track limits, but the track doesn't cross it |
| `route_check.drying_in_corridor` | Check | The same for drying ground |
| `data.outside_chart_coverage` | Check | The track leaves the `DataCoverage` of every loaded cell |
| `route_check.danger_in_corridor` | MustReview | A wreck, obstruction or rock inside the limits is shallower than the threshold, or of unknown depth |
| `route_check.danger_near_route` | Check | The same within `DangerNearbyMeters` (default 0,1 NM) beyond the limits |
| `route_check.crosses_safety_contour` | MustReview | The track crosses depth, dredged or drying areas shallower than the safety contour |
| `route_check.safety_contour_in_corridor` | Check | Such areas reach inside the limits, but the track doesn't cross them |
| `route_check.soundings_below_safety_depth` | Check | Soundings inside the limits, in deeper areas, are shallower than the safety depth |
| `route_check.crosses_s102_shoal` | MustReview | The track crosses S-102 cells shallower than the safety contour |
| `route_check.s102_shoal_in_corridor` | Check | S-102 cells shallower than the safety contour inside the limits, off the track |

Crossings of the track are found exactly. Ground that only reaches into the
corridor is found at cross-sections every
`ChartCheckOptions.StationSpacingMeters` (default 10 m). Where cells overlap,
only the finest cell at a place counts (`CellScaleRanking`), so a coarse cell's
generalised land doesn't fire where a finer cell charts water.

The depth checks run only when the settings have both a safety contour and a
safety depth. Dangers are judged against the safety contour, as the S-101
portrayal's `UDWHAZ05` judges them, and, on a leg with a draft, against that
leg's draft + squat + required UKC on the static charted depth; the meta line
names the threshold that fired. A danger counts wherever it lies, also inside
water already shallower than the safety contour.
Adjacent shallow areas, soundings and S-102 cells merge into one span, so a
long bank is one finding. `ChartCheck.Dangers.cs` writes out the rules.

Once the cells' indexes are built (the first check), checking the 24 NM Solent
route against 17 UKHO trial cells takes about 50 ms, and re-checking the two
legs either side of a dragged waypoint about 11 ms. Its findings don't depend
on time, so the check keeps its last sweep and reuses it while the legs, the
depth settings, the corridor override, the options and the sources are
unchanged. The wording comes from `RouteCheckOptions.Formatter`, so hosts can
pass their own resources.

`SkinOfTheEarth` holds the water, land and drying rules the route check and
the viewer's pick report share.

## Check a route against navigational warnings

`WarningCheck` checks a route against S-124 navigational warnings
(Editions 1.0.0 and 2.0.0): which ones cross its corridor or come near it, and
whether each is in force while the vessel is there. It reads the warnings
through `IRouteWarningSource` (`sources.Warnings`), which gives the parts near
each leg and the set of every loaded warning, including cancellations and
in-force bulletins. The catalog bundle has one part per S-124 `NavwarnPart`,
with its geometry and affected areas; `RouteWarningList` wraps a list you
already have:

```csharp
var outcome = new WarningCheck().Run(new RouteCheckContext(
    plan,                                          // legs with cross-track limits and speeds, and the departure
    new RouteCheckSettings { At = viewTime },      // used only when the plan has no departure
    sources));

foreach (var finding in outcome.Findings)
{
    Console.WriteLine($"{finding.Group} {finding.Title}: {finding.Meta}");
}
```

A line or area **crosses** when it touches the track or comes within the
leg's cross-track limit on its side. A warning is **near** when it comes
within `RouteWarningOptions.NearDistanceMeters` of the track (default 0,5 NM),
or is a point inside the corridor. The time is the ETA where the vessel meets
the warning; where a leg has no ETA, the plan's departure; and without one,
`RouteCheckSettings.At`. A warning is cancelled when
`NavigationalWarningSet.WithdrawnAt` withdraws it by the time the vessel gets
there: its cancellation date passes, another warning cancels it
(`References`, category 1) and was published by then, or the latest in-force
bulletin for its series, published after it, leaves it out (S-124 Edition
2.0.0 §9.2.1). Without a set, `RouteWarningList` builds one from its warnings.
The viewer's Warning card and Navigational warnings panel judge with the same
set, so they agree. A finding's `WarningDetails.Withdrawal` says why; one left out of
a bulletin reads "Navigational warning 105/26 not in in-force bulletin
NAVAREA X 110/26".

| Code | Group | When |
| --- | --- | --- |
| `s124.on_route` | MustReview | Crosses the corridor and in force |
| `s124.near_route` | Check | Near the route and in force |
| `s124.not_in_force` | Info | On or near the route, but no validity range overlaps the vessel's time there |
| `s124.cancelled` | Info | On or near the route, but cancelled by then |

Every finding has category `Warning`, the part's feature, and
`WarningDetails` with the message, relation, status and the times it was
judged at. `WarningCheckResult.Matches` (`report.Warnings`) also lists the
warnings near the route that give no finding. The meta line names the series
and number, the locality, the validity and the status; the wording comes from
`RouteCheckOptions.Formatter`.

## Check a route against restricted and protected areas

`AreaCheck` checks a route against regulated areas: S-101 restricted,
military practice, caution, anchorage and traffic areas, and S-122 marine
protected and restricted areas. It finds which ones the track enters or which
reach inside its cross-track limits, and whether each is in force while the
vessel is there. It reads the areas through `IRouteAreaSource`
(`sources.Areas`), over loaded S-101 cells and S-122 datasets:

```csharp
var outcome = new AreaCheck().Run(new RouteCheckContext(plan, new RouteCheckSettings { At = viewTime }, sources));
```

The S-101 feature types it checks are `RestrictedArea` (and the 1.x
`RestrictedAreaNavigational` and `RestrictedAreaRegulatory`),
`MilitaryPracticeArea`, `CautionArea`, `AnchorageArea`, `SeparationZoneOrLine`
surfaces, `PrecautionaryArea`, `InshoreTrafficZone` and
`SubmarineTransitLane`. Cable and pipeline areas, dumping grounds, fairways
and traffic lanes aren't checked: they restrict anchoring or fishing, or are
where a route is meant to go.

| Code | Group | When |
| --- | --- | --- |
| `route_check.enters_prohibited_area` | MustReview | The track enters an S-101 area whose `restriction` is entry prohibited (7) or entry restricted (8) |
| `route_check.prohibited_area_in_corridor` | Check | Such an area reaches inside the limits, but the track doesn't enter it |
| `route_check.restricted_area` | Check or Info | Another S-101 restricted or military practice area; Info when its restrictions don't bear on a passage (fishing, anchoring, dredging and the like) |
| `route_check.caution_area` | Check | A `CautionArea` |
| `route_check.traffic_area` | Check or Info | A separation zone, precautionary area or submarine transit lane; Info for an inshore traffic zone |
| `route_check.anchorage_area` | Check | An `AnchorageArea` |
| `route_check.protected_area` | Check or Info | An S-122 marine protected or restricted area; Info by restriction, as above. Never MustReview |
| `route_check.area_not_in_force` | Info | The area's `fixedDateRange` or season doesn't cover the vessel's time there |

An area that only reaches inside the limits is one group lighter (Check
becomes Info). Findings have category `Area` and `AreaDetails`, and the
code catalogue ranks an entry-prohibited area just after land and drying
ground when a verdict names the worst finding. The meta line names the leg,
the product, the restrictions, the categories, the dates and the ETAs.

Time works as for S-124 warnings: the ETAs over the stretch where the vessel
is in the area; without them, the plan's departure; and without one,
`RouteCheckSettings.At`. A `periodicDateRange`
repeats each year by month and day (`--03-01`, `----0301`) and may run over
the new year; an area with both a fixed range and seasons must be in both. A
date that can't be read counts as open. An area that isn't in force stays in
the list as Info, like an S-124 warning that isn't, rather than being
dropped, so the navigator still sees it on the route.

Holes are outside the area, and where S-101 cells overlap only the finest
cell at the place the route meets an area counts, so a coarse cell's copy of
an area gives no second finding. The wording, and the labels of the
restriction and category codes, come from `RouteCheckOptions.Formatter`.

## Findings

`RouteFinding` is one entry in a route's findings list. Each check (the
chart and depth checks, areas, S-124 warnings, and under-keel clearance)
produces these, so one list can show them
all. A finding has:

- a `Code` from `RouteFindingCodes`, the one catalogue of codes. Each
  `RouteFindingCode` has its `Value` (such as `route_check.crosses_land`),
  its `Category`, its default `Group`, and its `HeadlineRank`, the order in
  which Must review findings name a verdict
- a `Group`: `MustReview`, `Check` or `Info`
- a `Category`, what it's about: `Chart`, `Depth`, `Area`, `Warning`,
  `Clearance`, `Confidence` or `Data`
- the `LegIndex` and `Distance` along the route
- a `Location`: a point, or a span of the route
- an optional `Feature` (dataset, feature id and, for chart findings, feature
  type) that pick and describe can open
- typed `Details`, so a client gets the numbers without parsing text:
  `DangerDetails`, `ShoalDetails`, `AreaDetails`, `WarningDetails` or
  `ClearanceDetails`
- a `Title` and a `Meta` line, rendered by a `RouteFindingFormatter`

The formatter's wording is English by default, in the invariant culture,
metres and UTC. Pass a culture, a depth unit or a time zone to its
constructor, or subclass it and override its virtual phrases to use your own
resources (SoundCharts reads its `Strings.resx`). `Render` renders a finding
again, so a host can switch depth unit or time format without checking again.

```csharp
var formatter = new RouteFindingFormatter(CultureInfo.GetCultureInfo("fr-FR"), DepthUnit.Feet);
var report = new RouteChecker(sources, new RouteCheckOptions { Formatter = formatter }).Check(plan, settings);

foreach (var finding in report.Findings)
{
    if (finding.Details is DangerDetails danger)
    {
        Console.WriteLine($"{danger.FeatureType}: {danger.Depth?.TotalMetres} m, {danger.ThresholdKind}");
    }
}
```

## Rules shared with the pick report

SoundCharts' pick report uses the same rules as the passage check, so the two
give the same answer at a point:

- `ChartedDepthResolver` sets the order of charted-depth sources: S-102, then
  S-101 dredged areas, then depth areas, then the nearest sounding.
- `WaterLevelPreference` chooses between overlapping S-104 grids.
- `VerticalDatumCheck` compares the datums.

## Specification references

- S-102 Ed 3.0.0 §5.3 and §10.2.1 (Table 10-2): the vertical datum of S-102
  depths.
- S-104 Ed 2.0.0 §5.2 (Table 5-1): the S-104 vertical datum. It must be
  consistent with the S-101 and S-102 datasets.
- S-129 Ed 2.0.0 §3.2 and §7.1.2: non-navigable and almost non-navigable areas,
  and tidal windows. These correspond to "below your UKC setting", "tight" and
  the departure sweep.
