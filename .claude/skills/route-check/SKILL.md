---
name: route-check
description: |
  Route check engine: checking a planned route (PassagePlan) against
  chart and environmental data and reporting findings, a verdict and
  per-check status. Covers RouteChecker and its checks (chart, areas,
  warnings, clearance/UKC, weather, opt-in timing), RouteCheckSources and
  catalog adapters, finding codes and ids, and how RTZ and S-421 routes
  become a PassagePlan. USE FOR: changes to EncDotNet.S100.Navigation,
  Datasets.Pipelines/Navigation, Routes.Rtz, or the hosts (MCP
  CheckRouteTool, `s100 route check`, viewer PassageCheck); adding or
  changing a finding code; safety-contour, danger, UKC or water-level
  logic; route-check tests incl. S-164 Parts 5-7. DO NOT USE FOR: S-421
  encoding/portrayal (s421-route-plans), S-129 datasets (s129-ukc), S-101
  semantics (s101-enc), driving the viewer (viewer-evaluation).
---

# Route check engine expert

## When engaged
- Anything touching `RouteChecker` or an `IRouteCheck` in `src/EncDotNet.S100.Navigation/`,
  the source adapters in `src/EncDotNet.S100.Datasets.Pipelines/Navigation/`, or the
  route converters `RtzPassagePlan.cs` / `S421PassagePlan.cs`.
- Host code: `src/EncDotNet.S100.Mcp.Tools/CheckRouteTool.cs`,
  `tools/EncDotNet.S100.Cli/Commands/RouteCheckCommand.cs`,
  `src/EncDotNet.S100.Viewer/Services/PassageCheck/`.
- Docs that must move with code: `docs/how-a-route-check-works.md` (pipeline, time,
  keel, verdict, status), `docs/route-checks.md` (every code, group, threshold),
  `src/EncDotNet.S100.Navigation/README.md`.

## Standards anchors
- S-101 alert catalogue: findings are driven by the portrayal's `AlertReference`
  (`NavHazard`, `ProhAre`, `SafetyContour`); rules `UDWHAZ05`, `OBSTRN07`,
  `WRECKS05`, `SOUNDG03`. S-98 (MSC.530(106)) rates these as *Indication* in route
  planning (`docs/design/route-findings-volume.md` §1).
- S-164 Parts 5–7 conformance: `tests/EncDotNet.S100.S164.Tests/Part{5,6,7}*Tests.cs`
  and `S64/` via `S164RouteCheck.cs`.
- S-102 Ed 3.0.0 / S-104 Ed 2.0.0 vertical datums; S-129 Ed 2.0.0 §3.2, §7.1.2 is
  *mirrored* (below setting / tight / departure sweep) but no S-129 dataset is read
  or produced (`ClearanceCheck.UkcModel.cs`).
- RTZ = IEC 61174 Annex S, versions 1.0–1.2; S-421 1.0.

## Architecture in this repo (implemented)
- `RouteChecker(sources, options?, additionalChecks?)` runs `ChartCheck`, `AreaCheck`,
  `WarningCheck`, `ClearanceCheck`, `WeatherCheck` (+ extras such as `TimingCheck`,
  which only MCP adds) and returns one `RouteCheckReport`: sorted `Findings`,
  `Verdict` (Kind + Headline), `Counts`, `Checks` (state/reason per check) and typed
  details (`report.Chart`, `.Clearance`, `.Weather`, ...).
- Inputs: `PassagePlan` of `PassageLeg` (points, `LegGeometry` Rhumb/GreatCircle,
  Port/StarboardLimit, Speed, Draft, Squat, RequiredUkc) + departure;
  `RouteCheckSettings` (SafetyContour/Depth, `At`, CorridorOverride, TrackClearance
  30 m, NearbyDistance 0.1 NM, BerthRadius 100 m; `FromMarinerSettings`);
  `RouteCheckOptions` (e.g. DefaultLimit 50 m for a side without a limit).
- Data: Navigation depends only on Core; data arrives via the `RouteCheckSources`
  bundle (Charts, Areas, Warnings, Depth, WaterLevel, Weather, Cyclones).
  `CatalogRouteCheckSources.FromDatasets` builds it; `RouteCheckSourceCache` keeps one
  per dataset set. Product → check map is the table in
  `docs/how-a-route-check-works.md` "Where each dataset is used" (S-101/S-401/S-57→S-101
  charts; S-102 depth first, S-101 fallback; S-104 grids then stations ≤10 km by water;
  S-122 areas; S-124 warnings; GRIB2/NetCDF + NHC cyclones). S-111 only matters when
  speeds are through the water.
- Findings: `RouteFinding` (stable `Id`, `RouteFindingCode` with Category /
  DefaultGroup / HeadlineRank, Group, LegIndex, Distance, Location, Feature, typed
  `Details`) — text is rendered separately by `RouteFindingFormatter`. Codes live only
  in `RouteFindingCodes`; hazard classes in `RouteHazardClasses`.
- `docs/design/route-check-architecture.md` is marked **Implemented** (#1034): its §1
  describes the *old* four-engine shape (`PassageCheckEngine`, `RouteCheckEngine`,
  `RouteAreaCheck`, `RouteWarningCheck`) that no longer exists — read §2 for the shape
  you see now. `docs/design/route-findings-volume.md` still says **Proposed**, but its
  engine fields (`RouteHazardClass`, `RouteFindingPosition`, `AreaKind`, NavHazard
  codes, berth grouping) have landed via #1127; the viewer row/span/filter parts
  (`RouteFindingRows`) have not. Verify in code before trusting either header.

## Review checklist
1. A new or changed code goes in `RouteFindingCodes` and in the tables of
   `docs/route-checks.md` — `RouteFindingCodesDocsTests` fails otherwise. Codes and
   ids are a contract for MCP clients and CI (`s100 route check` exits 7 by verdict).
2. A check that cannot run returns Skipped/Partial with a `RouteCheckReasons`
   constant; never silently return no findings ("not checked" ≠ "nothing found").
3. Time rule is shared: leg ETA → plan `Departure` → `settings.At`. A leg without
   speed has no ETAs, nor do the legs after it. The chart check ignores time.
4. Keel is per leg (`Draft + Squat + RequiredUkc`); dangers use the static charted
   depth. Clearance runs only when every leg has a draft. Don't reintroduce a
   route-wide keel.
5. Inside-limits-off-track is one group lighter than on-track; a danger near a
   turn is reported once, on the closer leg; shoal soundings/S-102 cells <100 m
   apart merge.
6. Checks are shared across threads and cache between runs (chart result while legs
   + depth settings unchanged; clearance resamples only edited legs) — caches must
   be thread-safe and keyed on everything they depend on.
7. Keep fusion (verdict, sort, keel, time) in the engine, not in a host; all three
   hosts must produce the same verdict and codes for the same input.
8. Public API changes update `src/EncDotNet.S100.Navigation/PublicApi.txt`.

## Known pitfalls
- **Geodesy:** WGS-84 via `PassageGeodesy`/`Wgs84Geodesy` (GeographicLib); great-circle
  legs follow the ellipsoidal geodesic. Don't use `PlanarGeometry` for distances
  along the route.
- **Units:** use `Length`/`Depth`/`Speed` quantities. RTZ cross-track limits are
  nautical miles (converted in `RtzPassagePlan.cs`); everything else is metres. RTZ
  leg `i` is read from waypoint `i+1`; draft is the deeper of fwd/aft; neither format
  carries squat.
- **Datums:** when depth and water-level vertical datums differ, the level is
  reported but not applied (`data.datum_mismatch`); undeclared datums are applied
  with `confidence.datum_unconfirmed`. Tide predictions are lower confidence and
  never override S-104.
- **Dangers without VALSOU** use S-101 `defaultClearanceDepth` (unless reaching the
  surface); the meta line describes the water-level effect, not that depth.
- **Volume:** NavHazard/SafetyContour alerts can produce hundreds of findings on a
  buoyed channel (route-findings-volume §2); berth grouping and merge rules exist to
  contain it. Overhead features aren't yet judged against air draft.
- **Missing data:** the catalog bundle fills every member except `Weather`, so a
  missing product usually shows as `data.*` coverage findings rather than a skipped
  check; leave a member `null` to skip.
