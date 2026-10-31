# Depth basis indicator — Design Note

> Status: **Built** ([#1191](https://github.com/philliphoff/EncDotNet.S100/issues/1191)): the
> #1191 parts of §5 shipped together; the adjusted states wait for #1132.
> A sub-issue of [#1132](https://github.com/philliphoff/EncDotNet.S100/issues/1132)
> (chart-wide S-104 water level adjustment). This note designs the
> permanent indication of what the depths on the chart are relative to,
> and the state model behind it. #1132 will later drive that model;
> [Chart-wide water level adjustment](chart-wide-water-level-adjustment.md)
> covers the adjustment itself.

The mocks for this note are the **Depth Basis Indicator · design pass**
artboard on the *Chart display* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>): the status bar in each state (A1–A6) and
its placement (A7), the flyout before and after #1132 (B1, B2), the Depth
card in the new wording (C1–C3), and the Clearance readout and route-check
findings (D1, D2).

## 0. Problem

In the ECDIS Ltd simulator trials comparing S-57 with S-100 products
(Technical Report ECDIS/TR/2026/001: 100 seafarers, 10 ECDIS
manufacturers, around 1,000 assessed hours, 20 pilotage and BRM
scenarios; findings as summarised by George Koursaris), S-102 display
quality was rated 71% negative. Some participants still worked out
under-keel clearance by hand, because they couldn't tell which soundings
had been adjusted for the tide.

SoundCharts has the same ambiguity. The depths drawn on the chart are
always charted depths, relative to each dataset's vertical datum: S-101
depth areas and soundings, S-57 through the S-101 pipeline, and the
S-102 surface. The S-104 water level is applied in three places, none of
which is the chart:

| Where | Code | What the user sees |
|---|---|---|
| A picked point | `Services/Depth/LocationDepthProbe.cs`, `Services/Depth/DepthAssimilationService.cs` | The Object information **Depth** card: **Base**, **Tide**, **DEPTH AT SELECTED TIME** or **DEPTH (STATIC)** |
| Depth over time at a point | `ViewModels/DepthOverTimeViewModel.cs` | The card's chart: **Tide-adjusted depth** against **Safety depth** |
| Route clearance | `EncDotNet.S100.Navigation/ClearanceCheck.UkcModel.cs` | The Clearance readout: **Charted depth**, **Water level**, **UKC** |

Nothing on the chart says which of these the drawn depths are. A
mariner who has loaded S-104 and moved the Timeline can reasonably
assume the depths and safety contour follow it. They don't.

[S-164 conformance](s164-conformance.md) records the same gap as
cross-cutting finding 7: no adjusted depths, no adjustment boundary, and
"no permanent indication". The S-164 tests WaterLevelAdjustment and
WLAFeatureInformation (S-98 Appendix D-2, D-3) are rated Partial for it.

## 1. Constraints

- **Permanent.** S-98 Appendix D expects a permanent indication while
  water level adjustment is on. The indicator can't be something the
  user dismisses, and it can't live only in a panel.
- **True today, extended later.** The indicator ships before #1132, so
  today it can only ever say "charted". It must not need redesigning
  when #1132 adds an adjusted state; #1132 should only add inputs.
- **One vocabulary.** The depth card, the Clearance readout and the
  route findings already describe the same quantities with different
  words (§4). The indicator is the third surface; it should settle the
  words, not add a fourth set.
- **Not colour alone.** The state must read from the text. Colour can
  add emphasis but must work in the Day, Dusk and Night palettes.
- **Reachable by automation.** Every status-bar control has an
  automation id and accessible name (`AccessibilityGuardTests`), and an
  agent should be able to read the basis without a screenshot.

## 2. Decision

### 2.1 Where it goes

A pill in the status bar of `MainWindow.axaml`, directly after the
**OVERSCALE** pill and before the MCP status. Both are chart-safety
indications about what the map is showing, so they sit together. It
follows the overscale pill's pattern exactly:

- a `Button` with a `Flyout` (`Placement="Top"`) for the detail;
- state on `MainViewModel` (`DepthBasis…` properties next to
  `IsOverscaleVisible`, `OverscaleFactorText`, `OverscaleCells`);
- pushed by an `Update…` method, as `UpdateOverscale` is.

Unlike the overscale pill, it is shown whenever a depth-bearing dataset
is loaded and active, whether or not it is in view: S-101, S-57 (and
S-401) cells, and S-102 surfaces. It is hidden only when no such
dataset is loaded, because then there are no depths to qualify.

### 2.2 States and wording

The pill has a short label and a value, like **OVERSCALE** **4.6×**.
Times use the user's **Local**/**UTC** setting through
`TimeFormatting.Format`.

| State | When | Pill | Emphasis |
|---|---|---|---|
| Hidden | No depth-bearing dataset loaded and active | — | — |
| **Charted** | No S-104 loaded, or none whose footprint meets the view | **DEPTHS** **Charted** | Neutral, like the scale readout |
| **Charted, water level not applied** | An active S-104 dataset's footprint meets the view, and the chart isn't adjusted | **DEPTHS** **Charted · water level not applied** | Informational accent |
| **Adjusted** *(#1132)* | Adjustment is on and applies somewhere in the view | **DEPTHS** **+ water level 14:20** | Distinct accent, always on |
| **Adjusted, part of view** *(#1132)* | As above, but the adjusted area doesn't cover all charted depths in view | **DEPTHS** **+ water level 14:20 · part of view** | As Adjusted |
| **Charted, adjustment unavailable** *(#1132)* | Adjustment is on but can't apply in view: outside S-104 time range, NoData, or datums incompatible | **DEPTHS** **Charted · no water level at 14:20** / **Charted · datum mismatch** | Caution accent |

The second row is the case the trial describes and the reason the issue
exists: S-104 is on screen, the Timeline is moving, and the chart isn't
following. It gets an accent; plain **Charted** doesn't, so the pill
stays quiet for a user with only charts loaded.

Proposed `Strings.resx` keys, following the `Status_Overscale*`
pattern:

| Key | Value |
|---|---|
| `Status_DepthBasisLabel` | `DEPTHS` |
| `Status_DepthBasis_Charted` | `Charted` |
| `Status_DepthBasis_ChartedNotApplied` | `Charted · water level not applied` |
| `Status_DepthBasis_AdjustedFormat` | `+ water level {0}` |
| `Status_DepthBasis_AdjustedPartialFormat` | `+ water level {0} · part of view` |
| `Status_DepthBasis_NoWaterLevelAtFormat` | `Charted · no water level at {0}` |
| `Status_DepthBasis_DatumMismatch` | `Charted · datum mismatch` |
| `Label_DepthBasis` | `Depth basis` (automation name) |
| `Tooltip_DepthBasis` | `What the depths and safety contour on the chart are relative to. Click for the sources, the vertical datum and where water level is applied.` |

Only the first three rows ship with #1191. The `Adjusted*`,
`NoWaterLevelAt*` and `DatumMismatch` strings are added by #1132; they
are listed here so the vocabulary is settled once.

#### 2.2.1 Fitting a narrow status bar

The status bar is one line, so the pill can't wrap. The right-hand
cluster in `MainWindow.axaml` is a horizontal `StackPanel` that doesn't
shrink, and at the window's 800 px minimum width the longest values
(**Charted · water level not applied**, **+ water level 14:20 · part of
view**) don't fit beside the cursor position, scale and OVERSCALE.

An ellipsis isn't safe here: "Charted · water lev…" or "+ water level
14:20 · par…" can be read as a different state. So the pill never
truncates. Each state has three whole forms, and the pill shows the
longest one that fits:

| State | Full | Short | Compact |
|---|---|---|---|
| Charted | **Charted** | same | same |
| Charted, water level not applied | **Charted · water level not applied** | **Charted · not applied** | **Charted** + info icon |
| Adjusted *(#1132)* | **+ water level 14:20** | same | **Adjusted 14:20** |
| Adjusted, part of view *(#1132)* | **+ water level 14:20 · part of view** | **Part adjusted 14:20** | same as Short |
| No water level *(#1132)* | **Charted · no water level at 23:40** | **Charted · no water level** | **Charted** + caution icon |
| Datum mismatch *(#1132)* | **Charted · datum mismatch** | same | **Charted** + caution icon |

Rules:

- Every form is a true statement on its own. Only the qualifier is
  dropped; the **DEPTHS** label, the accent and the state's icon stay,
  and Compact always shows the icon so the state doesn't rest on colour.
- Shrink order: the left-hand status message ellipsizes first (it isn't
  safety information), then the pill steps Full → Short → Compact.
  OVERSCALE and DEPTHS are never hidden.
- The Full form is always available: it is the pill's tooltip, its
  accessible name (with `Label_DepthBasis`) and the flyout's first line,
  whatever form is drawn.
- Each form is its own `Strings.resx` entry (`…_Short`, `…_Compact`
  suffixes on the keys above), so a translation picks a form that fits
  instead of being cut.
- `DepthBasisText` returns all three forms; the view picks one by
  measuring, so the evaluator and its tests stay independent of layout.
  The pill's `TextBlock` sets `TextWrapping="NoWrap"` and
  `TextTrimming="None"`.

The mock is part A8 of the **Depth Basis Indicator · design pass**
artboard.

### 2.3 The flyout

The flyout answers three questions: what are the depths relative to,
what water level is loaded, and where is it applied. Its sections, with
the header style of `Status_OverscalePopupHeader`:

**DEPTHS ON THE CHART**

- One line for the basis: "Charted depths, relative to the vertical
  datum below. The safety contour and depth shading use these values."
  After #1132, when adjusted: "Charted depth + water level at 14:20
  (S-104 cbofs, run 12:00Z). The safety contour and depth shading use
  the adjusted values."
- **Vertical datum**: the datum label from `VerticalDatums.TryGetLabel`
  for the depth sources in view, for example "Mean Lower Low Water". When
  sources in view disagree, "Several datums in view" followed by one line
  per datum and the datasets on it. When a source doesn't declare one,
  "Not declared" (the existing `Clearance_DatumUnknown` text).
- **Sources in view**: the S-102 surfaces and S-101 cells contributing
  depths in the view, by display name, as the overscale flyout lists
  cells. Capped at five with "and N more".

**WATER LEVEL**

- Not loaded: "No S-104 water level loaded."
- Loaded, not applied: each active S-104 dataset meeting the view, with
  its kind (grid or station), its run, and its time range, then "Not
  applied to the chart."
- After #1132: which dataset supplies the adjustment at the view time
  (the `WaterLevelPreference` choice), the step time actually used, and
  the datum agreement (`DatumAgreement`): same, unconfirmed or
  different.

**WHERE WATER LEVEL IS APPLIED**

A fixed list that tells the user where they can get an adjusted depth
today:

- "Object information: the Depth card at a picked point."
- "Route check: clearance at each ETA."
- After #1132: "The chart, at the Timeline's view time." with a link
  button to the adjustment setting in **Mariner Settings**.

The flyout has no controls of its own until #1132; it is read-only
information plus, later, one link.

### 2.4 The state model

One evaluator, one service, one record. #1132 consumes the record and
supplies one input; it doesn't add a second source of truth.

```csharp
namespace EncDotNet.S100.Viewer.Services.Depth;

/// What the depths drawn on the chart are relative to.
internal enum DepthBasisKind
{
    None,      // no depth-bearing dataset loaded: indicator hidden
    Charted,   // charted depths, relative to each dataset's vertical datum
    Adjusted,  // charted depth + S-104 water level (#1132)
}

/// Why the chart shows charted depths when water level exists.
internal enum DepthBasisReason
{
    None,                 // Kind is None or Adjusted, or no S-104 meets the view
    NotApplied,           // S-104 meets the view; chart adjustment off or not built
    WaterLevel,           // adjustment on but unavailable: see WaterLevelStatus
}

internal sealed record DepthBasisSource(
    string DatasetId,          // display name, as the Datasets panel shows it
    string Spec,               // "S-101", "S-102", "S-104", ...
    int? VerticalDatumCode,    // S-100 vertical datum code, or null when not declared
    DateTime? Start = null,    // S-104 only: first time step (UTC)
    DateTime? End = null,      // S-104 only: last time step (UTC)
    WaterLevelSourceKind? Kind = null);

internal sealed record DepthBasis(
    DepthBasisKind Kind,
    DepthBasisReason Reason,
    WaterLevelStatus? WaterLevelStatus,   // set when Reason is WaterLevel, or Kind is Adjusted
    DateTimeOffset? ViewTime,             // the Timeline's view time the basis was evaluated at
    DateTime? WaterLevelStepTime,         // the S-104 step actually used (#1132)
    bool CoversWholeView,                 // false for "part of view" (#1132)
    DatumAgreement? Datum,                // depth vs water level, when both are known
    IReadOnlyList<DepthBasisSource> DepthSources,
    IReadOnlyList<DepthBasisSource> WaterLevelSources)
{
    public static DepthBasis Hidden { get; } = …;
}
```

`WaterLevelStatus` and `DatumAgreement` are the existing public enums in
`EncDotNet.S100.Navigation` (`PassageCheckResult.cs`, `WaterLevel.cs`).
Reusing them means the indicator, the route check and the chart
adjustment report a gap with the same reason codes: `NoCoverage`,
`BeforeStart`, `AfterEnd`, `NoData`, `DatumMismatch`.

The pieces:

- **`DepthBasisEvaluator`** (static, pure): takes a
  `DepthBasisInputs` snapshot — active datasets with spec, footprint and
  datum; the view extent; the view time; and an
  `IWaterLevelAdjustmentState` — and returns a `DepthBasis`. All the
  decisions in §2.2 live here, so they are unit-testable without
  Avalonia.
- **`IWaterLevelAdjustmentState`**: the seam for #1132. It answers "is
  chart adjustment on, and what did it apply in this view at this
  time?" For #1191 the only implementation is
  `NoWaterLevelAdjustment`, which always reports off. #1132 registers
  its own implementation (`ChartWaterLevelAdjustmentService`, whose
  result is the `ChartAdjustmentState` record in
  [Chart-wide water level adjustment](chart-wide-water-level-adjustment.md)
  §7) and raises its `Changed` event when the setting or the applied
  result changes.
- **`DepthBasisService`**: owns the current `DepthBasis`, recomputes
  it, and raises `Changed`. It listens to
  `IDatasetLoaderService.DatasetLoaded`, `DatasetRemoved`,
  `ActiveChanged` and `LayerStackChanged`; `IViewTime.Changed`; and
  `IWaterLevelAdjustmentState.Changed`. Viewport changes reach it the
  way they reach the overscale pill: `MapInteractionController` calls it
  next to `UpdateOverscale(viewport)`. Recomputation is cheap (footprint
  intersection and a few lookups) and is coalesced to the next UI
  frame.
- **`DepthBasisText`** (static): the only place that turns a
  `DepthBasis` into strings, for the pill, the flyout, the depth card
  (§4) and the MCP `display` field (§2.6).

Whether a dataset meets the view comes from the same test the
Timeline's **In map view** filter uses (`ITimelineMapScope.IsInMapView`
in `Services/TimelineMapScope.cs`). Datums come from where
each product already declares them: the S-102 root `verticalDatum`
(`S102Dataset.VerticalDatum`), the S-104 dataset's `verticalDatum`, and
for S-101 the `SoundingDatum` meta-feature, read as
`CatalogChartedDepthSource.SoundingDatumAt` already does for the route
check.

### 2.5 Vertical datum display

The datum is shown in the flyout, not on the pill. The pill must stay
short enough for the status bar, and in most views there is a single
datum, so repeating it on the pill would be noise. Where datums in view
disagree, the flyout says so (§2.3); when #1132 refuses adjustment for
incompatible datums, the pill says **datum mismatch**.

Today `BaseDepthResult.VerticalDatumCode` is `null` for S-101 sources
("on the chart's sounding datum"). The route check reads the
`SoundingDatum` meta-feature instead. The indicator needs the S-101
datum too, so step 2 of §5 makes the depth probe read it the same way.
This also lets `DepthAssimilationService.IsDatumMismatch`, which
currently checks only S-102 bases, apply the "assume and warn" policy
to S-101 bases.

### 2.6 MCP

A new read-only viewer tool, `get_depth_basis`, in
`McpTools/ViewerStateTools.cs` next to `get_timeline_state`. It reads
`DepthBasisService.Current` and returns:

```json
{
  "kind": "charted",
  "reason": "not_applied",
  "waterLevelStatus": null,
  "viewTime": "2026-10-31T14:20:00Z",
  "waterLevelStepTime": null,
  "coversWholeView": true,
  "datum": null,
  "display": "Charted · water level not applied",
  "depthSources": [
    { "id": "US5WA22M", "spec": "S-101", "verticalDatum": 12, "verticalDatumLabel": "Mean Lower Low Water" }
  ],
  "waterLevelSources": [
    { "id": "104US00_CBOFS_20261031T12Z", "spec": "S-104", "kind": "grid",
      "start": "2026-10-31T12:00:00Z", "end": "2026-11-02T12:00:00Z", "verticalDatum": 12 }
  ]
}
```

Codes are snake_case, as the other viewer tools report them. `display`
is the pill text exactly as shown, so an agent driving a scenario can
assert what the user sees. The same object is added as a `depthBasis`
field to the viewer's `get_check_state` result
(`McpTools/PassageCheckTools.cs`), so an agent reporting a route's
clearance also reports whether the chart it is looking at is adjusted.
The shared `check_route` tool in `EncDotNet.S100.Mcp.Tools` has no map,
so it doesn't get the field.

### 2.7 Accessibility

- Automation id `Map.DepthBasis`; accessible name from
  `Label_DepthBasis`; `AutomationProperties.HelpText` from
  `Tooltip_DepthBasis`, as the overscale button does.
- The accessible name includes the state ("Depth basis: Charted, water
  level not applied"), not just the label, so a screen reader announces
  it without opening the flyout.
- When the kind changes between `Charted` and `Adjusted`, the change is
  announced politely, where Avalonia's automation peer supports a live
  region. A change that removes adjustment is the one a mariner must not
  miss.
- Colours come from `DynamicResource` brushes defined per palette, not
  the overscale pill's hard-coded hex values, so Dusk and Night stay
  legible. The text carries the state in every case.

## 3. Alternatives rejected

- **Wait for #1132.** The ambiguity exists now. With S-104 loaded, the
  Depth card and the route check already show adjusted values while the
  chart doesn't, which is the confusing case the trial describes.
- **A map watermark or chart-corner label.** It competes with chart
  content and moves with pan and zoom chrome. The status bar already
  carries the overscale indication in the same role.
- **Show the indicator only when S-104 is loaded.** Then its absence
  would have to mean "charted", which is exactly the inference the
  trial says mariners get wrong. A permanent **Charted** costs one short
  pill.
- **A per-dataset flag in the Datasets panel.** The question is about
  the picture, not one dataset. A panel can be closed.
- **Compute the basis in `MainViewModel`.** It would be easy to bind,
  but #1132, the depth card and MCP all need it, and `MainViewModel` is
  already large. A service with a pure evaluator is testable and has one
  owner.

## 4. Shared vocabulary

The three depth surfaces use different words for the same quantities.
This note settles on the S-98 and S-104 terms, "water level" and
"adjusted", and retires "tide" and "base" in the UI. (Code names such as
`S104TideCandidate` are left alone.)

| Quantity | Settled term | Today: depth card | Today: Clearance readout and findings |
|---|---|---|---|
| Depth relative to the vertical datum | **Charted depth** | **Base** | **Charted depth** |
| S-104 height above that datum | **Water level** | **Tide** | **Water level** |
| Their sum | **Adjusted depth** | **Tide-adjusted depth**, **DEPTH AT SELECTED TIME** | — |
| No water level used | **water level not applied** | **DEPTH (STATIC)**, "No tide data covers this location…" | "tide not applied", **no tide**, "No S-104 at this ETA · tide not applied" |

String changes, all in `Resources/Strings.resx`:

| Key | Today | Proposed |
|---|---|---|
| `Pick_Depth_Label_Base` | Base | Charted depth |
| `Pick_Depth_Label_Tide` | Tide | Water level |
| `Pick_Depth_Series_TideAdjusted` | Tide-adjusted depth | Adjusted depth |
| `Pick_Depth_Label_DepthNow` | DEPTH AT SELECTED TIME | ADJUSTED DEPTH AT SELECTED TIME |
| `Pick_Depth_Label_DepthStatic` | DEPTH (STATIC) | CHARTED DEPTH · WATER LEVEL NOT APPLIED |
| `Pick_Depth_NoTide` | No tide data covers this location — depth is static (base value only, no time-based correction). | No S-104 water level covers this location, so this is the charted depth. |
| `Findings_TideNotApplied` | tide not applied | water level not applied |
| `Findings_TideNotAppliedForFormat` | tide not applied for {0:0.0} NM | water level not applied for {0:0.0} NM |
| `Check_ChipNoTide` | no tide | no water level |
| `Clearance_NoTide` | No S-104 at this ETA · tide not applied | No S-104 at this ETA · water level not applied |
| `Clearance_TideDatumMismatch` | S-104 on another datum · tide not applied | S-104 on another datum · water level not applied |
| `Clearance_Gap_DatumMismatch` | Datum mismatch, tide not applied | Datum mismatch, water level not applied |

The depth card also gains one line under its header, from
`DepthBasisText`: "The chart shows charted depths" or, after #1132, "The
chart shows adjusted depths at 14:20". The card then says both what the
point's depth is and how it relates to what the chart draws there.

Key names stay as they are, to keep the diff to values; renaming keys is
a separate cleanup. Docs that quote these labels
(`src/EncDotNet.S100.Viewer/README.md`, `docs/how-a-route-check-works.md`,
`docs/route-checks.md`) are updated in the same change.

## 5. Implementation plan

Each step is a separate pull request that builds and passes on its own.

1. **State model and evaluator.** Add `DepthBasisKind`,
   `DepthBasisReason`, `DepthBasisSource`, `DepthBasis`,
   `DepthBasisInputs`, `IWaterLevelAdjustmentState`,
   `NoWaterLevelAdjustment` and `DepthBasisEvaluator` under
   `src/EncDotNet.S100.Viewer/Services/Depth/`. Unit tests only; nothing
   is wired up.
2. **Datums.** Read the S-101 `SoundingDatum` for depth sources (the
   evaluator) and for picked points (`BaseDepthResolver`, so
   `BaseDepthResult.VerticalDatumCode` is set for S-101 sources). Extend
   `DepthAssimilationService.IsDatumMismatch` to S-101 bases. Share the
   lookup with `CatalogChartedDepthSource` rather than copying it.
3. **Service.** Add `DepthBasisService`, register it, and wire its
   inputs: dataset events, `IViewTime.Changed`, and a viewport call from
   `MapInteractionController` beside `UpdateOverscale`.
4. **Status bar.** Add `MainViewModel.UpdateDepthBasis(DepthBasis)` and
   its bound properties; add the pill and flyout to `MainWindow.axaml`;
   add the `Status_DepthBasis*`, `Label_DepthBasis` and
   `Tooltip_DepthBasis` strings; add per-palette brushes.
5. **Vocabulary.** Apply the string changes in §4, add the depth card's
   basis line, and update the docs that quote the labels.
6. **MCP.** Add `get_depth_basis`, its DTOs in `ViewerMcpJsonContext`,
   and the `depthBasis` field on the viewer's `get_check_state` result.
   Document both in `docs/mcp-server.md`.
7. **Docs and conformance.** Describe the indicator in the viewer
   README (next to "Overscale indication"), and update the
   WaterLevelAdjustment and WLAFeatureInformation rows in
   [S-164 conformance](s164-conformance.md) to say the permanent
   indication exists and adjustment is still #1132.

#1132 then adds its `IWaterLevelAdjustmentState` implementation and the
`Adjusted`, `NoWaterLevelAt` and `DatumMismatch` strings. The pill,
flyout, service and MCP tool need no structural change.

## 6. Tests

- **`DepthBasisEvaluatorTests`** (Viewer.Tests): a table over the
  inputs. No depth datasets → hidden. S-101 only → Charted. S-102 + S-104
  outside the view → Charted. S-104 meeting the view → Charted,
  NotApplied. S-104 inactive (Active off) → Charted. Each
  `WaterLevelStatus` through a fake `IWaterLevelAdjustmentState` →
  the matching reason. Partial coverage → `CoversWholeView = false`.
  Mixed datums → both listed, `Datum` reported.
- **`DepthBasisTextTests`**: every state's pill and flyout text,
  **Local** and **UTC** time formats, and a non-English current culture
  for the number and time formats.
- **`DepthBasisServiceTests`**: recomputes on each input event and
  raises `Changed` once per coalesced burst.
- **`DepthAssimilationServiceTests`**: S-101 bases with known, unknown
  and differing datums.
- **MCP**: `get_depth_basis` returns the evaluator's state, and
  `display` matches the pill text.
- **Accessibility**: `AccessibilityGuardTests` covers the new button
  automatically; add an assertion that its accessible name includes the
  state.
- **Visual regression**: one status-bar snapshot per state shipped in
  #1191, in Day and Night.

## 7. Open questions

1. **Should plain Charted show at all with no S-104 loaded?** This note
   says yes, because the absence of the pill would otherwise carry
   meaning. A setting to hide it would be reasonable for chart-only use,
   but never while adjustment is on.
2. **View or whole session?** The NotApplied emphasis uses S-104 footprints
   in the view. A user zoomed away from S-104 coverage sees plain
   Charted. That seems right but should be checked against the S-164
   WLAOverlap data.
3. **Station series.** A fixed-station S-104 (data coding format 8) has
   no footprint beyond its station positions. Should a station near the
   view count as "meets the view"? The route check limits stations by
   distance (`MaxStationDistanceMeters` in `CatalogWaterLevelSource`);
   reuse that limit.
4. **Key renames.** §4 keeps the `Tide` key names for a smaller diff.
   Rename them in the same series or leave for a later cleanup?
5. **The `s100` CLI and headless renders.** `s100 render` draws charted
   depths only. Should a render note the basis in its metadata or
   legend, so an exported PNG isn't mistaken for an adjusted chart?
