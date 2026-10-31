# Chart-wide water level adjustment — Design Note

> Status: **Proposed** ([#1132](https://github.com/philliphoff/EncDotNet.S100/issues/1132)).
> Nothing in this note is built yet. It proposes how SoundCharts applies
> S-104 water levels to the depths, the safety contour and the S-102 surface
> across the displayed chart, not only at a picked point and along a route.
> The status-bar indication that tells the mariner what the chart shows is
> designed separately in [Depth basis indicator](depth-basis-indicator.md)
> ([#1191](https://github.com/philliphoff/EncDotNet.S100/issues/1191)); this
> note defines the state that indicator reports.

The mocks for this note are the **Chart-wide Water Level Adjustment · design
pass** artboard on the *Chart display* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>): the **Water level adjustment** setting
(A1–A3), the same chart charted and adjusted with the boundary and a refused
area (B1–B6), the adjusted-sounding options for Q3 (C0–C3), the Depth card
and sounding pick report (D1, D2), and the timeline while dragging (E1, E2).

## 0. Problem

S-104 water level is applied in three places today, and the chart isn't one
of them:

| Where | Code | What it does |
|---|---|---|
| A picked point | `LocationDepthProbe`, `DepthAssimilationService` (`src/EncDotNet.S100.Viewer/Services/Depth/`) | Base depth + tide series at the point, shown in the **Depth** card. |
| Over time at a point | `DepthOverTimeViewModel` | The same curve across the S-104 time range. |
| Along a route | `ClearanceCheck.UkcModel` (`src/EncDotNet.S100.Navigation/`) | `UKC = charted depth + water level − draft − squat` at each sample's ETA. |

On the chart, S-101 depth areas, contours and soundings and the S-102
surface are drawn at their charted values relative to the vertical datum,
whatever the timeline shows. The safety contour is the charted one. S-164
records this as finding 7 ([S-164 conformance](s164-conformance.md) §4), and
the matrix rates WaterLevelAdjustment, AdjustmentOfDepthValues,
WLAFeatureInformation, UncertaintyWLA, WLATimePeriod, IncompatibleDatums,
UserSelectedSafetyContour and SafetyContourWLA as **Partial**.

Bridge teams have the same need. In the ECDIS Ltd simulator trials comparing
S-57 with S-100 products (Technical Report ECDIS/TR/2026/001, as summarised by
George Koursaris), 86% of participants reported more confidence in tidal
navigation with S-104, and the automatically adjusting safety contour was
singled out for praise. The same trials found that some participants still
worked out under-keel clearance by hand because they couldn't tell which
soundings had been adjusted for the tide. Both findings shape this design: the
adjustment has to happen on the chart, and the chart has to say, every time,
what was adjusted, with what data, for what time.

## 1. Constraints

- **S-98 Appendix D** (water level adjustment) and the S-164 tests listed
  above define the expected behaviour: adjusted depths and safety contour
  across the displayed area, the boundary of the adjusted area, a permanent
  indication, adjustment over a user-defined time period, combined
  uncertainty, and refusal of data on incompatible vertical datums.
- **S-104 Ed 2.0.0 has no portrayal** (clause 9): its purpose is to adjust
  the ENC and bathymetry. SoundCharts' isolines and gauges
  ([S-104 README](../../src/EncDotNet.S100.Datasets.S104/README.md#portrayal))
  stay as an optional view of the data, not as the adjustment.
- **S-98 Part A §A-6.9.1** has S-102 replace S-101 depth areas and contours
  where it's displayed (R-101-102-A/B), and its NOTE allows OEMs to add
  safety contour functions. The S-102-derived safety contour is listed as
  deferred in [S-98 interoperability](s98-interoperability.md) §3.5; this note
  takes it up.
- **One answer everywhere.** The chart, the pick report and the route check
  must give the same adjusted depth for the same point and time. Today they
  don't share a water-level model: the route check uses
  `CatalogWaterLevelSource` (`src/EncDotNet.S100.Datasets.Pipelines/Navigation/`),
  and the pick report samples `S104TideCandidate`s through
  `S104TimeSeriesSampler` and its own selection.
- **Decision support, not decision.** The adjustment must never be silent.
  Wherever an adjusted value is shown, the source dataset, the time (or
  period) and the datum status must be one step away.
- **Rendering cost.** Scrubbing the timeline already redraws S-104, S-111 and
  S-411. Re-running the whole S-101 Lua portrayal on every step would not
  keep up ([Mapsui rendering performance](mapsui-performance.md)).

## 2. Adjustment model

### 2.1 Sign conventions

The existing types already agree, and the design keeps them:

- `ChartedDepth.DepthMeters`: metres below the vertical datum, positive down,
  negative for drying heights.
- `WaterLevelReading.HeightMeters`: metres above the vertical datum, positive
  up (S-104 Ed 2.0.0 Table 5-1, vertical CS 6499).

The adjusted depth is `d' = d + h`. A negative `h` (water below chart datum)
makes the water shallower, and the adjustment must apply it: it's the case
that matters most.

Every comparison against a mariner threshold `T` (safety contour, safety
depth, shallow and deep contours) can be written either way:
`d + h < T` ⇔ `d < T − h`. The design uses both forms (§3.2).

### 2.2 The water-level field

A new pure model in `EncDotNet.S100.Navigation`, next to `IWaterLevelSource`:

```csharp
public interface IWaterLevelField
{
    // The water level to apply at a point, or null where nothing applies.
    WaterLevelSample? Sample(GeoPosition position);

    // The area where the field applies, for the adjusted-area boundary.
    IReadOnlyList<GeoPolygon> Footprint { get; }

    // What was applied: the time or period, and each contributing source.
    WaterLevelBasis Basis { get; }
}

public readonly record struct WaterLevelSample(
    double HeightMeters, double? UncertaintyMeters, WaterLevelSourceInfo Source);
```

Two implementations:

- `WaterLevelSnapshot`: the field at one instant, built from an
  `IWaterLevelSource` and a UTC time. Each point uses the first (most
  preferred) series from `Locate` and its `ValueAt(utc)`.
- `WaterLevelPeriodMinimum`: the lowest water level at each point over
  `[start, end]`, for the WLATimePeriod option (§2.4).

`CatalogWaterLevelSource` already implements the selection rule shared with
the route check (grids before stations, `WaterLevelPreference` among grids,
nearest station within `MaxStationDistanceMeters`), so the field is built on
it and the route check, the chart and the pick report all read one rule. The
pick report moves from `S104TideCandidate` selection to the same source
(phase 1).

Spatial sampling follows what the source already does:

- **Grids (data coding format 2):** the nearest node, as
  `CatalogWaterLevelSource.GridSeries` reads it. Bilinear interpolation would
  give smoother contours, but would make the chart disagree with the route
  check at the same point; see open question 1.
- **Stations (data coding format 8):** each station applies within its
  Voronoi cell, clipped to `MaxStationDistanceMeters` (10 km). There's no
  blending between stations: inverse-distance weighting would invent a
  surface the producer didn't supply. The cell edges are drawn as part of
  the adjusted-area boundary (§4), so a step at a cell edge is explained on
  the chart.
- **Time:** linear between the bracketing steps (`IWaterLevelSeries.ValueAt`).
  Outside a series' `Start…End` the point isn't adjusted. It is never
  clamped to the first or last step, matching the route check's
  "tide not applied".

### 2.3 Datum compatibility

`VerticalDatumCheck.Compare` gives `Same`, `Different` or `Unknown`. The
point probe warns and continues on anything but `Same` ("assume + warn");
the route check refuses `Different`. For the chart:

| Agreement | Chart behaviour |
|---|---|
| `Same` | Adjust. |
| `Different` | Refuse in that area. The area is drawn as charted, and the basis says why: "Not adjusted: S-102 on MLLW, S-104 on LAT". |
| `Unknown` | Refuse by default, with the reason "datum not declared". See open question 2. |

The depth side needs a datum per area:

- **S-102:** the root `verticalDatum`, already read into `ChartedDepth.VerticalDatum`.
- **S-101:** isn't read today. S-101 Ed 2.0.0 carries it on the
  `SoundingDatum` / `VerticalDatumOfData` meta features (`verticalDatum`),
  which the portrayal draws (`SoundingDatum.lua`) but nothing compares.
  Phase 1 reads it per cell (meta-feature coverage, falling back to the
  dataset default) so S-101 can be checked like S-102.

Datum refusal is per depth source and per S-104 dataset, so a chart can be
adjusted over S-102 and refused over an S-101 cell with an undeclared datum
in the same view. The basis lists each.

### 2.4 Time: view time or a period

The mariner chooses one in **Mariner Settings** (§6):

- **At view time:** a `WaterLevelSnapshot` at `GlobalTimeService.CurrentTime`.
  When the timeline is live, that's now.
- **Lowest over a period:** a `WaterLevelPeriodMinimum` over a start and end
  the mariner sets (S-98 Appendix D-2.5, S-164 WLATimePeriod). At each point
  the field is the minimum of the series' values at every step inside the
  period and at the two interpolated end points. It's the conservative
  choice for a passage window: the chart shows the least water the period
  offers.

## 3. What gets adjusted

### 3.1 S-102 surface

`d' = d + h(cell)` per S-102 cell, where `h` is sampled at the cell centre.
Cells with no field value (outside the footprint, refused datum, outside the
time range) keep their charted depth.

- **Colour bands.** `S102PortrayalCatalogue` builds a `CoverageColorScheme`
  from the mariner settings, and bands stay as they are. The adjusted values
  are what get banded. This is an `AdjustedBathymetrySource` wrapping
  `S102CoverageSource`: the band lookup reads `depth + h` per cell on demand,
  and the grid itself isn't copied.
- **Enhanced safety contour.** `IsolineTracer` (`src/EncDotNet.S100.Core/Pipelines/Coverage/`),
  which already traces the S-104 isolines, traces the adjusted S-102 grid at
  the safety contour value. It's drawn with the S-101 safety contour line
  style, on `BaseChartOver` with the S-101 line work, and ends at the S-102
  coverage limit with the coverage-limit border S-98 asks for. Where S-101
  takes over beyond the S-102 extent, the S-101 safety contour (§3.2)
  continues it.
- Without S-104, the same tracer draws the unadjusted S-102 safety contour
  (S-164 UserSelectedSafetyContour). That part doesn't depend on S-104 and
  ships first (phase 2).
- R-101-102-B keeps its MSC.232(82) §5.8 exception: the S-101 safety contour
  stays visible under S-102. When both exist, open question 4 decides which
  one is primary.

### 3.2 S-101 depth features

S-101 portrayal reads the thresholds as Lua context parameters
(`S101ContextParameterBindings` in `S101LuaRuleExecutor.cs`) and the depths
as feature attributes and sounding `ScaledZ`. Two ways to apply `h` follow
from §2.1, and the design uses each where it fits:

- **Shift the thresholds** for areas and contours (`DepthArea`,
  `DredgedArea`, `DepthContour`): run their rules with `SafetyContour − h`,
  `SafetyDepth − h`, `ShallowContour − h` and `DeepContour − h`. The rules
  (`DEPARE03.lua`, `DepthContour.lua`) then choose the shades and the safety
  contour edge as they would for a mariner who had set those values. The
  safety contour still snaps to a charted contour, as S-101 requires, and
  contour labels keep their charted `VALDCO`.
- **Shift the values** for soundings and depth-valued dangers (`Sounding`,
  and `UnderwaterAwashRock`, `Wreck` and `Obstruction` with
  `valueOfSounding`): present `ScaledZ + h` and `valueOfSounding + h` to the
  rules through a feature view. The figures the mariner reads are then the
  adjusted ones (S-98 Appendix D-3.1, S-164 AdjustmentOfDepthValues), and
  `SOUNDG03`'s and `UDWHAZ05`'s comparisons against the unshifted thresholds
  come out the same as the threshold form.

Adjusted soundings are drawn so they can't be mistaken for charted ones; the
symbol treatment is open question 3. The pick report lists the charted value,
`h`, its source and time, and the adjusted value for every adjusted feature
(S-164 WLAFeatureInformation).

`h` varies across a cell, and a context parameter is one number per Lua run.
Each depth feature gets the **minimum** `h` over the part of its geometry
inside the footprint, floored to a 0.1 m bucket. Rounding down never
overstates the water. Features are grouped by bucket and the depth rules run
once per bucket; a typical harbour view has a handful of buckets. A feature
that straddles the footprint's edge is left charted in phase 3 (open
question 5).

Only the depth classes above are re-portrayed. Everything else in the cell
keeps its cached instructions.

### 3.3 Uncertainty

S-98 Appendix D-2 asks for the S-104 uncertainty to be combined with the
S-102 one. Treating them as independent, `σ = √(σ₁₀₂² + σ₁₀₄²)`. It's shown
in the depth card and pick report next to the adjusted depth. It doesn't move
the safety contour: S-98 doesn't ask for that, and a contour that moved with
a confidence setting would be harder to read. `S104DatasetReader` doesn't
read an uncertainty today; phase 1 adds it where the dataset carries one, and
the card says "S-104 uncertainty not provided" where it doesn't.

## 4. Adjusted-area boundary

The footprint is the union of the S-104 grid nodes that have a value at the
applied time (fill values and land-masked nodes excluded) and the clipped
station cells, minus the areas refused for datum. It's drawn as one line
style of its own, on `MarinerOverlay` so no chart fill hides it, with the
station cell edges inside it. Selecting it shows the basis for that area:
the dataset, station or grid, the time or period, and the datum status.

Outside the footprint the chart is charted, and the basis says so.

## 5. Recomputing on a time change

`GlobalTimeService.CurrentTimeChanged` drives a viewer-side
`ChartWaterLevelAdjustmentService`. On a change it:

1. Builds the new field. This is cheap: the S-104 grids are coarse, and the
   field is evaluated lazily per sample.
2. Re-buckets the S-101 depth features. Only features whose bucket changed
   are re-portrayed; their instructions are cached per (feature, bucket), so
   scrubbing back and forth reuses them.
3. Invalidates the adjusted S-102 tiles. The band lookup is one add per cell.
4. Publishes a new `ChartAdjustmentState` (§7) once the layers for that time
   are drawn.

While the timeline is dragged, the map keeps the last completed adjustment,
and the state reports the time that adjustment is for, not the slider's
time, until the new one lands. A chart must never show one time's depths
under another time's label.

Adjusted sub-layers are memory-cached only. The warm tile disk cache keys on
dataset content and style (`ContentScope`), and adding the water level and
time to the key would fill the cache with tiles that are rarely reused.

## 6. Mariner setting

In **Settings** > **Chart display** > **Mariner Settings**, a new
**Water level adjustment** setting:

- **Off** (default): the chart is charted, as today.
- **At view time**
- **Lowest over period**, with a start and an end.

It's a viewer setting, not a `MarinerSettings` member: the library's
`MarinerSettings` are the S-100 Part 9 portrayal parameters, and the
adjustment is a host decision about which field to apply. The S-101 and
S-102 processors take the field as an explicit portrayal input
(`DepthAdjustment`), so the `s100` CLI and the MCP `render_to_image` tool can
apply it with a `--water-level-time` option or argument in a later phase.

When the setting is **Off** and an S-104 dataset covering the view is
loaded, the depth basis indicator says so (see its note). That's the case
the trial found most confusing.

## 7. State reported to the depth basis indicator

The [Depth basis indicator](depth-basis-indicator.md) owns how the state is
shown, and defines the seam this note plugs into:
`IWaterLevelAdjustmentState`, whose only implementation until this lands is
`NoWaterLevelAdjustment`. This note owns what the state is.
`ChartWaterLevelAdjustmentService` implements `IWaterLevelAdjustmentState`,
replaces the stub registration, raises `Changed` when the setting or the
applied result changes, and exposes the current result as:

```csharp
internal sealed record ChartAdjustmentState(
    WaterLevelAdjustmentMode Mode,           // Off, AtViewTime, LowestOverPeriod
    DateTime? AppliedTime,                   // the time the drawn layers are for
    (DateTime Start, DateTime End)? Period,
    IReadOnlyList<WaterLevelSourceInfo> Sources,
    IReadOnlyList<AdjustmentRefusal> Refusals,   // area, reason (WaterLevelStatus / DatumAgreement)
    bool AdjustedAreaInView);
```

`DepthBasisEvaluator` turns this record, with the loaded datasets and the
view, into the indicator's `DepthBasis`. Whether S-104 is loaded but not
applied is the evaluator's decision, not this record's, so the two can't
disagree. Refusal reasons use the existing `WaterLevelStatus` and
`DatumAgreement` enums, as the indicator does. The MCP `get_depth_basis`
tool and the pick report read the resulting `DepthBasis`, so all of them
use one vocabulary.

## 8. Alternatives rejected

- **Re-run all S-101 portrayal per time step.** Correct and simple, but it
  costs a full cell portrayal per step and loses the cached instructions for
  features the water level doesn't affect.
- **Rewrite feature geometry or attributes in the dataset.** It would change
  the data the pick report, validation and the route check read, and the
  charted values would be lost.
- **Interpolate between stations.** It makes a surface the producer didn't
  publish, and the steps at station cell edges are easier to explain than an
  invented gradient.
- **Clamp to the first or last time step.** It would show a tide for a time
  the data doesn't cover. The route check already refuses that.
- **Adjust only the S-102 surface.** It leaves the S-101 safety contour and
  soundings charted beside an adjusted surface, which is the "which ones were
  adjusted?" confusion the trial reported.

## 9. Tests

- **Unit:** sign conventions, including negative `h`; the period minimum at
  steps and interpolated ends; floor bucketing; datum refusal for each
  `DatumAgreement`; station cells and the 10 km clip; no adjustment outside
  `Start…End`.
- **Consistency:** for random points and times over the bundled S-102 and
  S-104 fixtures, the chart field, the pick report and `ClearanceCheck` give
  the same `d + h`.
- **Portrayal:** threshold shift and value shift give the same shades and
  safety contour edge as an equivalent mariner setting on unadjusted data.
- **S-164:** in `tests/EncDotNet.S100.S164.Tests`, using the EnhancedSafetyContour,
  WaterLevelAdjustment and WLAOverlap exchange sets from the v2.0.0 release
  (`s164-v2.0.0.json`). They cover WaterLevelAdjustment,
  AdjustmentOfDepthValues, WLAFeatureInformation, UncertaintyWLA,
  WLATimePeriod, IncompatibleDatums, UserSelectedSafetyContour,
  SafetyContourWLA, and Part 2's S-102 and S-104 overlay tests. Headless
  renders are compared with the reference plots, as slice 4 did.
- **Performance:** scrub across a day of hourly steps over a NOAA harbour
  view and record frame times with the instrumentation in
  [Mapsui rendering performance](mapsui-performance.md).

## 10. Phases

| Phase | Scope | Closes |
|---|---|---|
| 1 | `IWaterLevelField`, `WaterLevelSnapshot` and `WaterLevelPeriodMinimum` over `CatalogWaterLevelSource`; the pick report moves to it; S-101 vertical datum read; S-104 uncertainty read; combined uncertainty in the card. No chart change. | UncertaintyWLA; IncompatibleDatums at a point |
| 2 | S-102 safety contour from the S-102 grid with no adjustment, with the coverage-limit border. | UserSelectedSafetyContour |
| 3 | The mariner setting; adjusted S-102 bands and enhanced safety contour; S-101 threshold and value shifts; adjusted-area boundary; `ChartAdjustmentState` to the indicator. | WaterLevelAdjustment, AdjustmentOfDepthValues, WLAFeatureInformation, SafetyContourWLA, IncompatibleDatums |
| 4 | **Lowest over period**. | WLATimePeriod |
| 5 | `--water-level-time` in `s100 render` and the MCP render tools. | — |

Phase 3 needs #1191's indicator: the chart doesn't adjust without saying so.

## 11. Open questions

1. **Grid sampling.** Nearest node keeps the chart, the pick report and the
   route check identical. Bilinear gives smoother contours. Should all three
   move to bilinear together?
2. **Undeclared datums.** Much pre-release S-104 and S-102 data declares no
   S-100 datum code, or an EPSG code that `VerticalDatumCheck` treats as
   unknown. Refusing it is what S-164 expects, but it would refuse most of
   the NOAA pilot data. Is an explicit "Apply anyway: datum unconfirmed"
   override acceptable, with that wording in the basis?
3. **How adjusted soundings look.** A different colour would overload the
   depth palette. Options are an italic or underlined figure, or a small
   marker. S-98 doesn't say.
4. **Two safety contours.** Where S-102 is loaded, which one is primary:
   the S-102-derived contour (traced at the exact value) or the S-101 one
   (snapped to the next charted contour)? Drawing both in the same style
   would confuse; drawing the S-101 one faint where S-102 covers is one
   option.
5. **Features straddling the footprint.** Phase 3 leaves them charted. Large
   S-101 depth areas often straddle the edge of an S-104 grid, so clipping
   their fills at the boundary may be needed.
6. **Alerts.** Should the S-101 safety contour alert references
   (`AlertReference:SafetyContour`, `NavHazard`) follow the adjusted
   contour? The route check would then need the same field at each ETA,
   which `ClearanceCheck` already has.
