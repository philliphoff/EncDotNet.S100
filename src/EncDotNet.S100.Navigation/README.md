# EncDotNet.S100.Navigation

`EncDotNet.S100.Navigation` holds passage-planning calculations that combine
S-100 products: leg distances and ETAs, and an under-keel clearance (UKC)
check along a route. The check computes, at each sample's ETA:

```text
UKC = charted depth + water level − draft − squat
```

and compares it with the required UKC. Charted depth comes from S-102, with
S-101 as a low-confidence fallback; water level comes from S-104. The package
depends only on `EncDotNet.S100.Core`. Data reaches it through two small
interfaces, `IChartedDepthSource` and `IWaterLevelSource`, so it never loads a
file itself. `EncDotNet.S100.Datasets.Pipelines` implements both over loaded
datasets.

This is a calculation aid for exploring data. It isn't for navigation.

## Install

```bash
dotnet add package EncDotNet.S100.Navigation
```

To check a passage against loaded datasets, also install
`EncDotNet.S100.Datasets.Pipelines` and a CRS backend such as
`EncDotNet.S100.Crs.ProjNet`.

## Check a passage

This example builds the sources from a catalog of loaded datasets, describes a
two-leg passage, and checks it:

```csharp
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Navigation;
using EncDotNet.S100.Navigation;

IDatasetCatalog catalog = /* S-102, S-101 and S-104 datasets */;
var sources = PassageDataSources.FromDatasets(catalog.Datasets, new ProjNetCrsTransformFactory());
var engine = new PassageCheckEngine(sources.Depth, sources.WaterLevel);

var a = new GeoPosition(50.672, -0.978);
var b = new GeoPosition(50.725, -1.040);
var c = new GeoPosition(50.763, -1.105);
var legs = new[]
{
    new PassageLeg([a, b], DraftMeters: 11.5, SpeedKnots: 12, SquatMeters: 0.3, RequiredUkcMeters: 1.0),
    new PassageLeg([b, c], DraftMeters: 11.5, SpeedKnots: 8, SquatMeters: 0.3, RequiredUkcMeters: 1.0),
};

var result = engine.Check(new PassagePlan(legs, DateTimeOffset.Parse("2021-04-01T07:10:00Z")));

Console.WriteLine(result.Verdict);                        // BelowSetting, MeetsSettings, ...
Console.WriteLine(result.Minimum?.ClearanceMeters);       // smallest margin's UKC
Console.WriteLine(result.Minimum?.Time);                  // and when it occurs
foreach (var gap in result.Gaps)
{
    Console.WriteLine($"{gap.Kind}: {gap.StartDistanceMeters:F0}–{gap.EndDistanceMeters:F0} m");
}
```

Each `PassageLeg` carries the effective values for that leg. Resolve route
defaults and per-leg overrides before you build it. Every value is optional
apart from the draft and the points:

- Without a speed, there are no ETAs, and every sample uses the departure time.
- Without a required UKC, the check uses 0 m and sets `NoMarginSet`.
- Squat is a fixed allowance; it isn't modelled from speed.
- The speed is the speed over ground. S-111 currents aren't applied.

## What the result says

Each sample stands for a stretch of the leg of at most
`PassageCheckOptions.SampleSpacingMeters` (default 50 m) and carries the
shallowest depth found in it. Along S-102 the engine steps at half the finest
grid cell, so it doesn't skip any cell the leg crosses. A sample's `State` is
one of:

| State | Meaning |
|---|---|
| `MeetsSettings` | UKC is at least 0,5 m above the required UKC. |
| `Tight` | UKC meets the required UKC with less than 0,5 m to spare. |
| `BelowSetting` | UKC is less than the required UKC. |
| `LowConfidence` | UKC meets the required UKC, but the depth is from S-101, or the depth and water-level datums can't be confirmed to agree. |
| `NoData` | There's no charted depth, or no water level that can be applied. |

The water level counts as missing outside the S-104 extent or time range,
where a value is NoData, or where its vertical datum differs from the charted
depth's. A datum mismatch is reported as a gap and the two values are never
added. A datum that isn't declared, or isn't an S-100 vertical datum code,
can't be confirmed. In that case the water level is applied and the sample is
low confidence.

`Verdict` follows this priority:

1. `WaterLevelEndsBeforeArrival`
2. `BelowSetting`
3. `IncompleteData`
4. `MeetsSettingsTight`
5. `MeetsSettings`

`Gaps` and `LowConfidence` list the affected stretches, with their distances
and times. `Legs` summarises each leg, and `Sources` names the datasets,
stations and datums that were used.

## Recompute cheaply

`Check` runs in two steps that you can call separately:

- `SampleDepths(legs)` walks the legs, reads the charted depths and finds the
  covering S-104 series. This is the slow step. It's spatial, so it depends
  only on the legs' points.
- `Evaluate(profile, plan)` applies times and vessel values. Call it again when
  the departure, speed, draft, squat or required UKC changes.

After you drag a waypoint, pass the previous profile to
`SampleDepths(legs, previous)`. Legs whose points haven't changed are copied
from it.

`SweepDepartures(profile, legs, from, to, step)` evaluates a range of
departures over the same profile and returns the verdict for each departure.
`DepartureSweep.Windows()` returns the runs of departures that meet the
settings, `NearestMeeting(departure)` the meeting departure closest to one,
and `ArrivalAfterDataEnds()` the departures that arrive after the water-level
data ends.

`SweepWaterLevelRange(profile, legs, step)` sweeps the whole time range of the
S-104 data that covers the route (`profile.WaterLevelTimeRange()`), starting
at the first whole step. Over a long forecast, the step widens so the sweep
stays under `maxDepartures` (default 2000).

Every method takes a `CancellationToken`.

## ETAs on their own

`PassageSchedule.Compute(legs, departure)` returns each leg's distance,
duration and start and end ETA, and the arrival time. `EtaAt(leg, distance)`
gives the ETA at any point along a leg.

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
