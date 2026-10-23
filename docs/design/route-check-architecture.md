# Route check architecture — Design Note

> Status: **Implemented** (#1034). Every step in §3 has landed, and
> [How a route check works](../how-a-route-check-works.md) documents the
> result for outside use. This note records why the engine has the shape
> it does: it reviews the route and passage check engine as it stood after
> the incremental work on clearance, chart, area and S-124 checks, and
> proposes the API it then got. The library is in preview, and its only
> clients were SoundCharts, the MCP tools and `s100`, so the proposal
> reshaped the engine's own types rather than adding a facade over them.

## 0. Problem

A route check now combines six products: S-101 (with S-401 and translated
S-57) for land, depths, dangers and regulated areas, S-102 for bathymetry,
S-104 for water level, S-122 for protected areas, S-124 for navigational
warnings and, in SoundCharts only, S-111 for an expired-forecast notice.
Each kind of check was added as its own engine, and each host joined them
up for itself.

The calculations are sound and well tested. What a caller sees is not one
engine but four, plus the joining work they have to redo:

| Engine | Input | Options | Result | Source of data |
|---|---|---|---|---|
| `PassageCheckEngine` | `PassagePlan` of `PassageLeg` | `PassageCheckOptions` | `PassageCheckResult` (no findings) | `IChartedDepthSource`, `IWaterLevelSource` |
| `RouteCheckEngine` | `RouteCheckLeg` list | `RouteCheckOptions` (+ `RouteCheckDepths`, `RouteCheckText`) | `RouteCheckResult` | `IRouteChartSource`, `IChartedDepthSource?` |
| `RouteAreaCheck` | `RouteCheckLeg` list + `RouteWarningTiming` | `RouteAreaOptions` (+ `RouteAreaText`) | `RouteAreaResult` | `IRouteAreaSource`, or lists |
| `RouteWarningCheck` | `RouteCheckLeg` list + `RouteWarningTiming` | `RouteWarningOptions` (+ `RouteWarningText`) | `RouteWarningResult` | lists + `NavigationalWarningSet` |

To get the result that SoundCharts shows, a caller has to do several
things themselves. They resolve one route into two leg types. They compute
a schedule from one leg type to time-check the other. They pick a keel
depth for the dangers check, convert the clearance result into findings,
merge and sort four lists, and derive a verdict. They also build and cache
three source objects per dataset snapshot. No public API does any of this.

## 1. Review

### 1.1 What to keep

- **Navigation depends only on Core.** Data comes in through small
  interfaces, and Pipelines adapts loaded datasets to them. That boundary
  is right and the proposal keeps it.
- **The rules are shared with the pick report.** `ChartedDepthResolver`,
  `WaterLevelPreference`, `VerticalDatumCheck` and `SkinOfTheEarth` give
  the same answer at a point as the check does along a route.
- **The calculations.** The finest-cell rule, exact track crossings,
  corridor cross-sections, the isolated-danger rule, in-force rules judged
  at ETAs, datum checks, and confidence kept separate from state.
- **Incremental UKC.** `SampleDepths` (spatial, slow) is separate from
  `Evaluate` (temporal, cheap), and profiles are reused per leg after an
  edit.
- **Testability.** Every engine runs against fakes, and there are about
  100 unit tests plus scenario tests on real cells.
- **Stable finding codes and ids**, which the MCP and CLI contracts already
  depend on.

### 1.2 Fusion lives in the hosts

The joining logic is written twice, in SoundCharts and in `CheckRouteTool`,
and the copies have drifted:

| Logic | SoundCharts | MCP / `s100` |
|---|---|---|
| Clearance → findings (`clearance.*`, `confidence.*`, `data.*` codes) | `PassageFindings.Build` | not done: `check_route` has no clearance findings, and `get_passage_result` is a separate result |
| Sort | `PassageFindings.Sort` | copied in `CheckRouteTool.Project` |
| Verdict and headline finding | `RouteCheckVerdict.Build`, `MustReviewRank` (matches area codes by substring) | `SummaryText` (an English copy of `NoDraftTitle`); `VerdictTitle` for clearance only |
| Keel for the dangers check | deepest `draft + squat + UKC` over all legs | the same, but with a request draft it takes the maximum squat and the maximum UKC from different legs |
| Time where a leg has no ETA | view time, even when the route has a departure | the departure, else `Time ?? now` |
| Depth checks without safety settings | taken from the mariner settings | skipped silently (MCP); 30 m / 30 m (`s100`) |
| UKC when some legs have no draft | not run | error (MCP); skipped silently (`s100`) |
| Warnings checked | the warnings panel's list and set | every S-124 dataset in the catalog |
| Datasets used | bounding-box overlap | cells near the corridor plus the datasets behind findings |
| Expired S-111 forecast | `data.s111_forecast_expired` | not done |

Route models are converted three more times: `RouteEtas.ToPassageLegs`,
`PassageCheckService.CheckInput.For` and `ViewerPassageRouteProvider`. The
geodesy for "position and ETA at a distance" is in UI classes
(`PassageCheckService.CursorAt`, `RouteCheckOverlay.PathAlong`).

`s100 route check` runs the checks by constructing `CheckRouteTool` and
`GetPassageResultTool`. So it inherits MCP's choices, and its route model
(`PassageRouteSnapshot`) is defined in an MCP tool file.

### 1.3 The engine's own seams are inconsistent

- **Two leg types for one route.** `PassageLeg` has the vessel values,
  `RouteCheckLeg` has the cross-track limits, and both have points and
  geometry. Every host builds both from the same route. `PassageLeg` uses
  draft 0 to mean "no draft".
- **One "depths" concept split across levels.** The vessel values
  (draft, squat, required UKC) are per leg in `PassageLeg`, but the chart
  check takes one route-wide `RouteCheckDepths`. That forces each host to
  invent a keel rule, and the rules have drifted (§1.2). The engine
  already knows which leg each danger is on, so it can use that leg's
  keel.
- **Time has no owner.** `RouteWarningTiming` is named for warnings but
  also drives areas. The clearance check takes a `PassagePlan` departure.
  The schedule is computed by the caller from `PassageLeg`s and passed in.
- **Sources don't match.**
  - Charts and areas have source interfaces, but warnings are passed as
    lists plus a set.
  - `IRouteChartSource` takes `GeoPosition`/`BoundingBox`, while
    `IChartedDepthSource` and `IWaterLevelSource` take bare
    latitude/longitude doubles.
  - `RouteCheckDataSources.Bathymetry` (S-102 only) and
    `PassageDataSources.Depth` (S-102 with S-101 fallback) are two
    `CatalogChartedDepthSource`s over the same surfaces.
  - SoundCharts builds `PassageDataSources` up to three times and
    `RouteCheckDataSources` twice per dataset snapshot.
  - Each host writes its own snapshot cache keyed by reference equality.
- **No finding category for areas.** Area findings say
  `RouteFindingSource.RouteCheck`. That's why SoundCharts recognises them
  by substring.
- **Codes are scattered.** They're in `RouteCheckCodes`, `RouteAreaCodes`
  and `RouteWarningCodes`, while `PassageFindings.Codes` (clearance,
  confidence, data) is in the Viewer. The group and headline rank of a
  code are spread through engine and host code.
- **Text is baked into findings, through four text records.**
  `RouteCheckText`, `RouteAreaText` and `RouteWarningText` each repeat
  `Separator`, `LegFormat` and `Culture`. The clearance wording isn't in
  the SDK at all. Findings carry only rendered `Title`/`Meta` strings,
  except S-124's typed `Warning`. So a client can't get the depth that
  fired, the threshold or the area's restrictions without parsing text.
- **Options repeat themselves.** `DefaultHalfWidthMeters` is in three
  options records, and segment and station spacing are in three.
- **"Not checked" looks the same as "nothing found".** Skipping the depth
  checks for lack of settings, the clearance check for lack of a draft, or
  the warnings check for lack of S-124 all produce no findings and no
  record of the skip.
- **Conventions.**
  - Physical values are `double …Meters` and `…Knots`, where the adopted
    quantity types (`Length`, `Depth`, `Speed`; see
    [Quantity types](quantity-types.md)) are the contract. `MarinerSettings`
    already uses `Depth`.
  - Results and findings are `record`s with collection fields, which
    [API conventions §2](api-conventions.md)
    says should be classes.
  - The Navigation README says every check produces `RouteFinding`s; the
    clearance engine doesn't.
- **Thread safety is undocumented.** `GetPassageResultTool` serialises
  whole runs because the sources share S-101 indexes and transforms. The
  contract for concurrent use isn't written down anywhere.

## 2. Proposed design

There is one route model and one settings object. One set of sources is
built per dataset snapshot. One checker runs a list of checks and returns
one report: findings, verdict, what ran, and the per-check detail. The
existing engines become checks behind that seam, and the hosts become
projection and presentation code.

```mermaid
flowchart LR
  subgraph Pipelines
    DS[Loaded datasets] --> SRC[CatalogRouteCheckSources]
  end
  subgraph Navigation
    PLAN[PassagePlan] --> CK[RouteChecker]
    SET[RouteCheckSettings] --> CK
    SRC --> CK
    CK --> CTX[RouteCheckContext: track, corridors, schedule]
    CTX --> C1[ChartCheck]
    CTX --> C2[AreaCheck]
    CTX --> C3[WarningCheck]
    CTX --> C4[ClearanceCheck]
    CTX --> C5[custom IRouteCheck]
    C1 & C2 & C3 & C4 & C5 --> REP[RouteCheckReport]
  end
  REP --> H1[SoundCharts]
  REP --> H2[MCP]
  REP --> H3[s100]
```

### 2.1 One route model

`PassagePlan` and `PassageLeg` become the only route input. They carry
everything any check reads, with quantity types and honest nulls.

```csharp
public sealed class PassagePlan
{
    public PassagePlan(IReadOnlyList<PassageLeg> legs, DateTimeOffset? departure = null);
    public IReadOnlyList<PassageLeg> Legs { get; }
    public DateTimeOffset? Departure { get; }      // null: no ETAs
}

public sealed class PassageLeg
{
    public required IReadOnlyList<GeoPosition> Points { get; init; }
    public LegGeometry Geometry { get; init; } = LegGeometry.Rhumb;
    public Length? PortLimit { get; init; }        // XTD half-widths; null → options default
    public Length? StarboardLimit { get; init; }
    public Speed? Speed { get; init; }             // SOG; null → this and later legs have no ETA
    public Depth? Draft { get; init; }             // null: no draft (not 0)
    public Depth Squat { get; init; }
    public Depth? RequiredUkc { get; init; }
    public string? Name { get; init; }
}
```

This removes `RouteCheckLeg`, MCP's `PassageRouteSnapshot` and
`PassageRouteLeg`, and SoundCharts' `CheckInput`. Each host keeps exactly
one conversion from its own route type. `Routes.Rtz` and `Datasets.S421`
each convert their route to a `PassagePlan`, next to their readers (§4,
decision 3).

### 2.2 Settings and options

The proposal separates what the user decides on each run (settings) from
how the engine is tuned once (options).

```csharp
public sealed class RouteCheckSettings
{
    public Depth? SafetyContour { get; init; }     // null: depth checks skipped, and reported as skipped
    public Depth? SafetyDepth { get; init; }
    public Length? CorridorOverride { get; init; } // replaces every leg's limits
    public required DateTimeOffset At { get; init; } // judges areas/warnings where there's no ETA
    public DepartureRange? DepartureWindow { get; init; } // optional sweep

    public static RouteCheckSettings FromMarinerSettings(MarinerSettings settings, DateTimeOffset at);
}

public sealed class RouteCheckOptions
{
    public static RouteCheckOptions Default { get; }
    public Length DefaultLimit { get; init; }      // one 50 m default
    public ChartCheckOptions Chart { get; init; }  // station spacing, danger-near distance, merge gaps
    public WarningCheckOptions Warnings { get; init; }
    public ClearanceCheckOptions Clearance { get; init; } // today's PassageCheckOptions
    public RouteFindingFormatter Formatter { get; init; } // §2.5
}
```

It also fixes the rules that drifted between hosts:

- **Time.** An ETA is used where one exists. Without one, the plan's
  departure is used, and without that, `settings.At`. This matches what
  [Route checks](../route-checks.md) already says.
- **Keel.** Each danger is judged against the keel of the leg it's
  reported on: `Draft + Squat + RequiredUkc` from that leg. The route-wide
  `RouteCheckDepths` and both hosts' keel rules go.
- **Gating.** A check that can't run is reported as skipped, with a
  reason (§2.4), never silently. The clearance check runs on legs that
  have a draft. A plan with drafts on only some legs is skipped with
  reason `draft_missing`, so the hosts don't need to make it an error.

### 2.3 One set of sources

```csharp
public sealed class RouteCheckSources
{
    public IRouteChartSource? Charts { get; init; }
    public IChartedDepthSource? Depth { get; init; }        // S-102 with S-101 fallback; ChartedDepth.Kind tells which
    public IWaterLevelSource? WaterLevel { get; init; }
    public IRouteAreaSource? Areas { get; init; }
    public IRouteWarningSource? Warnings { get; init; }     // new
}

public interface IRouteWarningSource
{
    IReadOnlyList<RouteWarning> WarningsNear(BoundingBox extent);
    NavigationalWarningSet Set { get; }                     // cancellations and bulletins
}
```

- **One depth source.** The chart check's S-102 shoal test filters to
  `ChartedDepthKind.Bathymetry`, and the clearance check uses the whole
  fallback chain. One S-102 index serves both.
- **Consistent geometry types.** Every source interface takes
  `GeoPosition` and `BoundingBox`.
- **Pipelines builds the bundle.** `CatalogRouteCheckSources.FromDatasets(datasets, transforms, options)`
  returns a bundle, and `RouteCheckSourceCache` holds the
  snapshot-keyed cache that all three hosts write today.
- **SoundCharts can swap one source.** It passes its own
  `IRouteWarningSource` over the warnings panel, so the checker judges the
  same warnings the panel shows.
- **Thread safety becomes a contract.** Sources must be safe for
  concurrent reads. The Pipelines implementations make their lazy indexes
  thread-safe, and the MCP semaphore goes.

### 2.4 Checker, checks and report

```csharp
public sealed class RouteChecker
{
    public RouteChecker(RouteCheckSources sources, RouteCheckOptions? options = null,
                        IEnumerable<IRouteCheck>? additionalChecks = null);

    // Synchronous, CPU-bound; the token allows cooperative cancellation.
    public RouteCheckReport Check(PassagePlan plan, RouteCheckSettings settings,
                                  CancellationToken cancellationToken = default);
}

public interface IRouteCheck
{
    string Name { get; }                                   // "chart", "areas", "warnings", "clearance", …
    RouteCheckOutcome Run(RouteCheckContext context, CancellationToken cancellationToken);
}

public sealed class RouteCheckContext
{
    public PassagePlan Plan { get; }
    public RouteCheckSettings Settings { get; }
    public RouteCheckSources Sources { get; }
    public RouteTrack Track { get; }                       // densified track, corridors, distance ↔ position
    public PassageSchedule Schedule { get; }               // ETAs, with the time rule in §2.2
    public RouteCheckOptions Options { get; }
}

public sealed class RouteCheckOutcome
{
    public static RouteCheckOutcome Skipped(string reason);
    public static RouteCheckOutcome Ran(IReadOnlyList<RouteFinding> findings, object? detail = null);
}

public sealed class RouteCheckReport
{
    public PassagePlan Plan { get; }
    public PassageSchedule Schedule { get; }
    public RouteTrack Track { get; }
    public IReadOnlyList<RouteFinding> Findings { get; }   // sorted: group, distance, then stable
    public RouteFindingCounts Counts { get; }
    public RouteCheckVerdict Verdict { get; }
    public IReadOnlyList<RouteCheckStatus> Checks { get; } // name, ran/skipped, reason
    public ClearanceResult? Clearance { get; }             // today's PassageCheckResult
    public IReadOnlyList<RouteAreaMatch> Areas { get; }
    public IReadOnlyList<RouteWarningMatch> Warnings { get; }
    public IReadOnlyList<string> DatasetsUsed { get; }     // recorded by the sources, not guessed
}

public sealed class RouteCheckVerdict
{
    public RouteCheckVerdictKind Kind { get; }  // MustReview, WaterLevelEndsBeforeArrival, Check, Clear
    public RouteFinding? Headline { get; }      // the finding that names it, by code rank
}
```

The existing engines become the built-in checks:

- `ChartCheck` is today's `RouteCheckEngine`. It keeps one sweep for land,
  drying ground, coverage, the safety contour, soundings, S-102 shoals and
  dangers, because those all read the same corridor features.
- `AreaCheck` is today's `RouteAreaCheck`.
- `WarningCheck` is today's `RouteWarningCheck`.
- `ClearanceCheck` wraps today's `PassageCheckEngine`. It also turns its
  result into findings: below setting, confidence and gaps, which moves
  `PassageFindings.Build` out of SoundCharts.
  `data.s111_forecast_expired` is dropped (§4, decision 4).

Four pieces of work stop being done once per check:

- `RouteTrack` is built once per run and shared. Today the chart, area
  and warning checks each densify the route at their own spacing.
- `RouteTrack` also exposes `PositionAt(distance)` and `PathBetween(a, b)`,
  which replace the geodesy in SoundCharts' `CursorAt` and `PathAlong`.
- **Incremental work moves into the checker.** It keeps the last depth
  profile per leg geometry and the last chart findings per geometry and
  settings. Moving a waypoint resamples only the changed legs. Changing
  the departure only re-evaluates the clearance and re-judges times.
  Hosts stop holding `PassageDepthProfile`.
- **Departure sweeps** become `settings.DepartureWindow` on the same call.
  The result is `ClearanceResult.Departures`. `SweepWaterLevelRange`
  becomes a `DepartureRange.WaterLevelData` value.

The per-check types (`ChartCheck`, `AreaCheck` and the others) stay public
and can be run on their own. The checker is how you run all of them, not
the only way.

### 2.5 Findings: structured first, text second

```csharp
public sealed class RouteFinding
{
    public string Id { get; }                      // unchanged scheme
    public RouteFindingCode Code { get; }
    public RouteFindingGroup Group { get; }
    public RouteFindingCategory Category { get; }  // Chart, Depth, Area, Warning, Clearance, Confidence, Data
    public int? LegIndex { get; }
    public Length? Distance { get; }
    public RouteFindingLocation? Location { get; }
    public RouteFindingFeature? Feature { get; }
    public RouteFindingDetails? Details { get; }   // typed, per kind
    public string Title { get; }                   // rendered by the formatter
    public string Meta { get; }
}

public abstract class RouteFindingDetails;
public sealed class DangerDetails : RouteFindingDetails { /* feature type, depth or water-level effect, threshold that fired, side, offset */ }
public sealed class ShoalDetails : RouteFindingDetails { /* least depth, threshold, count, S-102 uncertainty */ }
public sealed class AreaDetails : RouteFindingDetails { /* product, kind, restrictions, categories, in-force, from/until */ }
public sealed class WarningDetails : RouteFindingDetails { /* today's RouteFindingWarning */ }
public sealed class ClearanceDetails : RouteFindingDetails { /* minimum, margin, minutes below, state */ }
```

- **One code catalogue.** `RouteFindingCodes` holds every code as a
  `RouteFindingCode` (value, category, default group, headline rank). This
  replaces the four code classes and the Viewer's `MustReviewRank`. The
  verdict uses the rank, and the MCP schema and the docs table can be
  generated from the catalogue.
- **One formatter.** `RouteFindingFormatter` renders `Title` and `Meta`
  from the details and replaces the four text records. Its virtual members
  default to invariant English, and it takes one culture, depth unit and
  time format. SoundCharts subclasses it over `Strings.resx`. MCP and
  `s100` use the default, and MCP adds `details` to its JSON so clients get
  numbers instead of parsing text.
- `RouteFindingSource` becomes `RouteFindingCategory`, with `Area` and
  `Depth` split out, so nothing has to match codes by substring.

### 2.6 Package boundaries and hosts

| Package | Owns |
|---|---|
| `EncDotNet.S100.Navigation` | the plan, settings, options, sources interfaces, checker, checks, report, codes and formatter. Still Core-only. |
| `EncDotNet.S100.Datasets.Pipelines` | `CatalogRouteCheckSources` and `RouteCheckSourceCache`. |
| `EncDotNet.S100.Routes.Rtz`, `EncDotNet.S100.Datasets.S421` | the conversion from their route to `PassagePlan`, with the format's quirks. Both now reference Navigation. |
| `EncDotNet.S100.Mcp.Tools` | projecting `RouteCheckReport` to DTOs. One `check_route` returns the whole report, clearance included, with `includeSamples` and `includeWindow` for the detail. `get_passage_result` is removed. |
| `s100 route check` | reads the file, builds the plan, calls `RouteChecker`, and maps `Verdict.Kind` to the exit code. It stops constructing MCP tools. The CLI still references the MCP packages for `s100 mcp`. |
| SoundCharts | its route → `PassagePlan` conversion, the view time as `At`, a panel-backed `IRouteWarningSource`, a resx formatter, the verdict card, legend and follow. The producers, `PassageFindings` and `RouteCheckVerdict.Build` collapse into presentation code. |

## 3. Migration

Each step is one PR that leaves all three hosts building and their tests
passing. Codes and ids don't change at any step, so the scenario, MCP and
CLI tests act as the regression net.

1. **Route model and quantity types.**
   - Introduce the new `PassagePlan` and `PassageLeg`, and
     `RouteCheckSettings`.
   - Make all four engines take them, and judge dangers by per-leg keel.
   - Replace `RouteWarningTiming` with the schedule and the time rule.
   - Delete `RouteCheckLeg`, `RouteCheckDepths` and both hosts' keel rules.
2. **Sources.**
   - Add `RouteCheckSources`, `IRouteWarningSource`, the single depth
     source and `GeoPosition`-based interfaces.
   - Add `CatalogRouteCheckSources` and `RouteCheckSourceCache`.
   - Make the sources thread-safe and drop the MCP semaphore.
3. **Findings.**
   - Add the code catalogue, categories, details and formatter.
   - Move clearance-to-findings into `ClearanceCheck`.
   - Delete the three text records and `PassageFindings`.
4. **Checker and report.**
   - Add `RouteChecker`, `IRouteCheck`, `RouteTrack`, `RouteCheckReport`,
     the verdict, per-check status and checker-owned incremental caching.
5. **Hosts.**
   - Merge `check_route` and `get_passage_result` into one `check_route`
     over the checker, and rewrite `s100 route check` over it too.
   - Move the RTZ and S-421 conversions into their format packages, and
     drop the CLI's `RouteFileReader` mapping.
   - Collapse SoundCharts' producers.
   - Add a parity test that runs one scenario through the SDK, MCP and
     `s100` and compares their findings.
6. **Lock down.**
   - Add a public API baseline for Navigation.
   - Write the "How a route check works" article and a runnable sample.

Steps 1 and 3 are the largest. Step 4 only makes sense after 1–3.

## 4. Decisions

These were open questions in the first draft of this note. They're now
settled, and the sections above reflect them.

1. **Quantity types in the public API: yes.** The plan, settings,
   options, finding details and clearance result use `Length`, `Depth`
   and `Speed`. The internals stay in metres as doubles. MCP and `s100`
   JSON convert at the edge, as they already do for `MarinerSettings`.
2. **Rendered text on findings: kept.** The checker renders `Title` and
   `Meta` with the configured `RouteFindingFormatter`, so a host can show
   a report straight away. The formatter stays public, so a host can
   re-render after a depth-unit or time-format change without
   re-checking. The alternative was details only, with every host calling
   the formatter. That's purer, but all three hosts would repeat the
   call.
3. **RTZ and S-421 → `PassagePlan`: in each format package.**
   `Routes.Rtz` and `Datasets.S421` reference Navigation, which depends
   only on Core and is small, and each exposes a conversion to
   `PassagePlan`. Each format's quirks then live in one place, next to
   its reader:
   - RTZ reads leg `i` from waypoint `i+1`.
   - RTZ cross-track limits are in nautical miles.
   - The draft is the deeper of the forward and aft draughts.
   - A missing speed is implied from the ETD and ETA.

   Today those quirks are written twice: in the CLI's `RouteFileReader`
   and in SoundCharts' `RouteRtzMapping`. The rejected alternatives were
   a separate `Navigation.Routes` adapter package (one more package to
   ship) and Pipelines taking a reference to `Routes.Rtz`.
4. **Expired S-111 notice: dropped.** `data.s111_forecast_expired`
   reported on data that no check reads, because S-111 currents aren't
   applied to ETAs (a decision in #887). Applying them is now tracked in
   #1033. That check will own forecast-currency reporting when it lands.
   S-104 currency stays with the clearance check, through its `data.*`
   gaps and the `WaterLevelEndsBeforeArrival` verdict. A general
   `DataCurrencyCheck` was rejected because it would duplicate those.
5. **MCP: one `check_route` tool with detail flags.** `check_route`
   returns the whole report: every finding (clearance included), the
   verdict, each check's status and the clearance summary.
   `includeSamples` adds the thinned UKC samples and per-leg results.
   `includeWindow` (with the window bounds) adds the departure sweep.
   `get_passage_result` is removed.
6. **Names: route check, with `PassagePlan` as the input.** The whole is
   the route check (`RouteChecker`, `RouteCheckReport`,
   `RouteCheckSettings`), matching the codes, the docs, `s100 route check`
   and SoundCharts' **Check route**. UKC is the clearance check
   (`ClearanceCheck`, `ClearanceResult`). The input stays `PassagePlan`,
   which in IMO passage-planning terms is a route plus the vessel's
   values and the departure.
