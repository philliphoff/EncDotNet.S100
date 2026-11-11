# Predefined display combinations — Design Note

> Status: **Built**, except Interoperability Catalogue combinations (§3.5),
> which wait for a catalogue loader
> ([#1192](https://github.com/philliphoff/EncDotNet.S100/issues/1192)). Steps
> 1–6 of §8 shipped together; §9 records where the build settled what this
> note left open. The note sets out what a display combination is in
> SoundCharts, what it changes and what it never changes, how it is stored,
> and the order to build it in. It takes the "Predefined Combinations UI" row
> off the deferred list in
> [S-98 interoperability](s98-interoperability.md#35-deferred--explicit-non-goals-for-v1).

The mocks for this note are the **Predefined Display Combinations · design
pass** artboard on the *Chart display* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>): the **View ▾** pill (A1–A3), its menu
(B), the View section of the Display controls panel (C), the **Save current
view** dialog (D), and the Datasets panel with **Charted only** applied (E).

## 0. Problem

SoundCharts can already build a calm, task-specific picture from several
products. The controls for it are spread across four places, and a mariner
has to set each one by hand:

| Control | Where it lives | Stored in |
|---|---|---|
| Display category | **Display: Standard ▾** toolbar pill, **ECDIS Display Controls** panel | `EcdisDisplayState.Category` → `ViewerSettings.EcdisDisplayCategory` |
| Viewing groups (incl. the three S-101 text groups) | **ECDIS Display Controls** panel, **Text ▾** pill | `EcdisDisplayState` hidden set → `ViewerSettings.EcdisHiddenViewingGroups` |
| Per-product display mode (S-411) | Display mode selector | `ViewerSettings.EcdisActiveDisplayModes` |
| Optional product layers (weather isobars, wind fill, wave arrows) | **ECDIS Display Controls** › Weather | `ViewerSettings.EcdisLayerVisibility` |
| Whether a dataset draws | Datasets panel eye | `DatasetEntry.IsVisible` (per dataset, in memory) |
| Whether a dataset takes part in S-98 rules and pick | Layer stack **Active** toggle | `IDatasetLoaderService.SetActive` (per dataset, in memory) |
| Sub-layers (S-111 arrows, S-104 isolines and stations) | Datasets panel sub-layer eye | `DatasetSubLayer.IsVisible` (per dataset) |

The ECDIS Ltd simulator trials comparing S-57 with S-100 products (Technical
Report ECDIS/TR/2026/001, as summarised by George Koursaris) found three
things this bears on:

- 65% of participants said information overload was not avoided. Dense
  layers, symbols and current vectors came up in nearly every debrief.
- The value came from combining products. S-101, S-102 and S-104 together
  did better than any one of them.
- Two of the report's three conditions for success are well-designed,
  configurable displays and introducing the products one at a time.

S-98 has a mechanism for named product combinations,
`S100_IC_PredefinedCombination` (Annex A §11, Part B §B-3.1.2, switching in
§15.4). SoundCharts has no Interoperability Catalogue loader yet: the v1
rules are hard-coded in `S98DefaultRules`
(`src/EncDotNet.S100.Core/DatasetPipelines/Interoperability/`), and the IC
model in [S-98 interoperability §5.2](s98-interoperability.md#52-in-memory-model)
is a proposal.

## 1. Decisions

### 1.1 A combination is a display picture, not a safety configuration

A **display combination** is a named, product-level description of what the
map shows. Applying one changes:

- which products are **in** the picture (by product specification),
- the display category,
- hidden viewing groups, per product,
- per-product display modes,
- map-wide optional-layer switches (`EcdisLayerVisibility`),
- sub-layer visibility by sub-layer key (for example `s111.arrows`,
  `s104.isolines`), applied to every dataset of the product.

Applying one never changes:

- **Mariner settings**: safety contour, safety depth, shallow and deep
  contours, four shades, depth unit, simplified symbols and the rest of
  `IMarinerSettingsProvider`. These describe the ship and the passage, not
  the picture. A combination that silently moved the safety contour would
  be exactly the "software makes the decision" failure the trial warns about.
- **The Navigational warnings switch** (`EcdisDisplayState.ShowNavigationalWarnings`).
  A combination may leave S-124 out of its product list, which makes the
  S-124 datasets inactive, but the warnings panel still lists them and a
  combination can't turn the map-wide switch off. See §6, Q2.
- **Display planes** (Under/Over Radar), which follow the radar setup.
- **Palette** (Day, Dusk, Night), which follows bridge lighting.
- **Dynamic sources**: own ship, AIS and NDBC overlays.
- **The route and route-check state**, and opacity.

### 1.2 Products are in or out through Active, not visibility

A product that a combination leaves out has every loaded dataset of that
spec set **inactive** with `IDatasetLoaderService.SetActive(id, false)`,
not just hidden.

Visibility alone is wrong here. `S98DefaultRules.HasActiveProductSet` keys
every inter-product rule on `LoadedDatasetInfo.Active`, not on
`DatasetEntry.IsVisible`. A "Charted only" combination that only hid S-102
would leave R-101-102-B suppressing the S-101 depth areas and contours
under the hidden surface, and the chart would show holes. Making S-102
inactive takes it out of the rule set, so the ENC depth shading comes back.

A product a combination includes has its datasets made active. Their
visibility is left alone, so the duplicate-coverage collapse in
`DatasetLoaderService.ApplyPostRegistrationPolicies` and any dataset the
mariner hid by hand keep their state.

S-57 and S-401 cells match the chart product: the combination's product list
names `S-101`, and a dataset matches when its
`DatasetEntry.PortrayalSpec` is `S-101` or `S-401`. Weather datasets match a
pseudo-product `Weather`.

### 1.3 Analysis keeps using every loaded dataset

The route check (`ClearanceCheck`), the depth probe
(`LocationDepthProbe`, `DepthAssimilationService`) and the **Depth** chart
don't consult `GetActive` today, and this design keeps it that way. A
combination changes what is drawn, not what the ship's clearance is worked
out from. The route check already names its sources, so a mariner can see
that S-104 was used even when the water-level surface isn't drawn. The depth
basis indicator ([#1191](https://github.com/philliphoff/EncDotNet.S100/issues/1191))
should say the same thing in the status bar. See §6, Q1.

### 1.4 Products that aren't loaded are listed, never loaded

A combination names products. If one isn't loaded, nothing is fetched or
opened: the combination applies to what is loaded and shows the missing
products as **not loaded** chips. When a dataset loads later, its product
rule from the active combination is applied at load, through the same hook
`DatasetLoaderService` already uses for load defaults (§3.3).

Loading data on the mariner's behalf would change what the route check
uses (§1.3) without a deliberate act, so it stays out.

### 1.5 One active combination, with a modified marker

At most one combination is active. When the mariner changes any control that
the combination covers, the combination stays selected and its label gets a
modified marker, for example **Pilotage (modified)**. **Reapply** restores
it; **Save as…** stores the current state as a new user combination.
Changes to things a combination doesn't cover (§1.1) never mark it modified.

## 2. Built-in combinations

Built-ins ship with SoundCharts, can't be edited or deleted, and are kept in
code (`BuiltInDisplayCombinations`) so the strings are localised. The
contents below are the proposal; the exact viewing-group ids come from the
bundled S-101 portrayal catalogue and are fixed in the implementation PR.

| Id | Label | Products in | Category | Other settings |
|---|---|---|---|---|
| `charted` | **Charted only** | S-101 | Standard | S-101 text: Important Text on, Other Text off. |
| `charted-bathymetry` | **Chart + bathymetry** | S-101, S-102 | Standard | As `charted`. |
| `pilotage` | **Pilotage** | S-101, S-102, S-104, S-111, S-124, S-125, S-201 | Standard | S-104: isolines off, stations on. S-111: arrows on, stations off. Weather layers off. |
| `passage-planning` | **Passage planning** | Every product except S-111 and weather | Other Information | S-104 isolines on. S-122, S-127 and S-128 in. |
| `all-loaded` | **Everything loaded** | Every product | the current category | Clears the combination's own overrides; equivalent to today's behaviour. |

The first three are also a ladder: chart, then bathymetry, then water level
and the rest. That is the order the trial report recommends for learning the
products one at a time (§4).

`pilotage` doesn't change S-111 arrow thinning. Thinning is fixed at
`SymbolThinning.DefaultMaxSymbolToSpacingRatio` (0.5), the value S-111 and
S-98 recommend, and has no setting. A sparser arrow density for pilotage is
§6, Q4.

## 3. Model and persistence

### 3.1 Types

In `src/EncDotNet.S100.Viewer/Services/DisplayCombinations/`:

```csharp
internal sealed record DisplayCombination(
    string Id,
    string Label,
    DisplayCombinationOrigin Origin,               // BuiltIn, User, InteroperabilityCatalogue
    IReadOnlySet<string> Products,                 // "S-101", "S-102", …, "Weather"; empty = every product
    EcdisDisplayCategory? Category,                // null = leave the current category
    IReadOnlyDictionary<string, IReadOnlySet<int>> HiddenViewingGroups,
    IReadOnlyDictionary<string, string> DisplayModes,
    IReadOnlyDictionary<string, bool> LayerVisibility,
    IReadOnlyDictionary<string, bool> SubLayerVisibility);  // key: sub-layer key, e.g. "s111.arrows"

internal enum DisplayCombinationOrigin { BuiltIn, User, InteroperabilityCatalogue }
```

The fields reuse the shapes `EcdisDisplayState` and `EcdisDisplaySettings`
already use, so applying a combination is mostly a call to
`EcdisDisplayState.Hydrate` with the combination's values, keeping the
navigational-warnings switch as it is.

`DisplayCombinationService` owns the list (built-ins, user, IC), the active
id and the modified flag. It depends on `EcdisDisplayState`,
`IDatasetLoaderService` and `DatasetsViewModel`, and raises `Changed`.

### 3.2 Settings

Two additions to `ViewerSettings`, both optional so older `settings.json`
files keep loading:

```csharp
public List<DisplayCombinationSettings> DisplayCombinations { get; set; } = new();   // user combinations only
public string? ActiveDisplayCombinationId { get; set; }
```

`DisplayCombinationSettings` is a plain POCO with the same fields as
`DisplayCombination`, using comma-separated id lists the way
`EcdisHiddenViewingGroups` does so the JSON stays editable. It must be added
to `SettingsJsonContext`. Built-ins aren't written to settings.

The `EcdisDisplayState` fields keep persisting through
`EcdisDisplayStatePersistence` as today. The active combination id is
restored at startup but not reapplied: the persisted ECDIS state is the
truth, and the modified flag is recomputed against it.

The per-dataset Active flag is still in memory only (PR-L4 in the S-98 note
was going to persist it). With combinations, the load hook in §3.3 restores
it, so a restart with **Charted only** active brings S-102 back inactive
without persisting Active per dataset.

### 3.3 Applying at load

`IDatasetLoaderService` gets a third load policy alongside
`SetHiddenFeaturesOnLoad` and `SetPortrayalTimeOnLoad`:

```csharp
void SetActiveOnLoad(Func<DatasetEntry, IDatasetProcessor, bool?>? policy);   // null = leave as is
```

`DisplayCombinationService` registers it. `ApplyPostRegistrationPolicies`
calls it before the first S-98 stack evaluation, so an excluded product never
paints for one frame. Sub-layer visibility is applied in the same pass.

### 3.4 Modified detection

The service derives the expected state from the active combination and
compares it with the actual state on `EcdisDisplayState.Changed`,
`IDatasetLoaderService.ActiveChanged` and `LayerStackChanged`:

- the category, if the combination sets one, matches;
- for each product with loaded datasets: in the combination ⇒ at least one
  dataset is active; not in it ⇒ none is;
- the hidden viewing groups, display modes and layer switches the combination
  sets match. Extra hidden groups in a product the combination doesn't
  mention don't count.
- sub-layer visibility: every dataset of the product matches.

Per-dataset visibility doesn't count (§1.2). The comparison is pure and runs
on snapshots, so it is unit-tested without the UI.

### 3.5 Interoperability Catalogue combinations

There's no IC loader. When one lands (the `S98CatalogueLoader` of the S-98
note §5.2), each `S98PredefinedCombination` becomes a
`DisplayCombination` with `Origin = InteroperabilityCatalogue`, its
`ProductSet` as `Products`, and no category or viewing-group settings. Its
`S100_IC_SuppressedFeatureLayer` rows stay with the interoperability
authority, which already suppresses by product set (step 4 of the
S-98 note §5.3). The list shows IC combinations in their own group.

Until then nothing in this design depends on IC data, and the built-ins
cover the trial findings. The S-98 semantics have to be checked against the
Annex A text before the IC mapping is built (§6, Q3).

## 4. UI

Labels below are proposed; they become `Strings.resx` entries in the
implementation PR.

**Map toolbar.** A new pill before **Display: Standard ▾**, labelled with
the active combination: **View: Pilotage ▾**, or **View: Custom ▾** when
none is active. Its flyout lists built-ins, then user combinations, then IC
combinations. Each row shows its products as small spec chips, with
not-loaded products dimmed. The flyout ends with **Save current view as…**,
**Reapply**, and **Manage…**, which opens the panel.

**ECDIS Display Controls panel.** A **View** section at the top of the
panel, above **Display Category**, with the same list, the modified marker,
and rename and delete for user combinations. The rest of the panel is
unchanged; editing a control there marks the combination modified.

**Datasets and layer stack.** A dataset made inactive by a combination shows
the existing inactive state on its **Active** toggle, with a tooltip naming
the combination: "Not in Pilotage". Turning it back on marks the
combination modified.

**One product at a time.** The flyout groups the three ladder combinations
under a heading such as **Build up the picture**, in order. Each shows a
one-line note on what it adds ("Adds high-resolution bathymetry; replaces the
chart's depth shading where it covers"). This is the cheap end of the
trial's "introduce products one at a time" condition; guided scenarios are a
separate issue.

## 5. MCP tools

Viewer tools in `src/EncDotNet.S100.Viewer/McpTools/`, following the
`set_dataset_state` adapter pattern in `ViewerStateTools.cs`:

| Tool | Request | Result |
|---|---|---|
| `list_display_combinations` | — | Each combination's id, label, origin and products, which products are loaded, the active id and whether it's modified. |
| `set_display_combination` | `id`, or `reapply: true` | Active id before and after, the datasets whose Active flag changed, and the not-loaded products. |

There's no MCP save or delete. An agent can drive the picture but not edit the
mariner's saved views. `capture_app_screenshot` results gain the active
combination id, so screenshots record which picture they show.

The library-level `set_display_category` tool in
`src/EncDotNet.S100.Mcp.Tools/Mutable/` is unchanged. Combinations are a
viewer concept and don't go into `MapPresentationState`.

## 6. Open questions

- **Q1. Should a combination change what the route check uses?** This design
  says no (§1.3). The opposite case: a mariner who leaves S-104 out of the
  picture may expect the clearance to ignore it too. Keeping analysis on all
  loaded data and saying so in the route check and the depth basis indicator
  (#1191) seems safer than letting a display choice change the numbers.
- **Q2. S-124 in combinations.** Leaving S-124 out makes its datasets
  inactive, and so stops the R-101-124 interplay and warning picks on the
  map. Should built-ins always include S-124, and should a user combination
  be allowed to leave it out?
- **Q3. S-98 Predefined Combination semantics.** The S-98 note models a
  predefined combination as a product set plus suppressions, applied
  automatically when that set is loaded. Annex A §15.4 describes switching
  between them. Check the Annex A text for whether an IC combination is
  meant to be mariner-selectable (this design) or automatic (today's
  R-101-102-B), and whether it can carry display settings.
- **Q4. Current arrow density.** Should `pilotage` thin S-111 arrows more
  than Rmax = 0.5? That needs a new `RenderContext` setting and departs from
  the S-111 and S-98 recommendation, so it's a separate decision.
- **Q5. Per-dataset scope.** A product-level rule can't express "this S-102
  but not that one". Is that needed, or is the modified marker enough?
- **Q6. Startup.** Should a fresh install start in **Everything loaded**
  (today's behaviour) or **Charted only**, which matches the trial's
  one-product-at-a-time advice but hides data a new user just opened?

## 7. Tests

In `tests/EncDotNet.S100.Viewer.Tests/`:

- `DisplayCombinationTests`: built-ins are well formed. Every viewing-group
  id exists in the bundled catalogue, and every sub-layer key is one a
  processor emits (`s104.isolines`, `s104.stations`, `s111.arrows`,
  `s111.stations`).
- `DisplayCombinationServiceTests`:
  - applying sets the category, viewing groups and layer switches;
  - it never touches mariner settings, the warnings switch, display planes or
    the palette;
  - S-57 and S-401 cells match `S-101`.
- Active rather than visible: with S-101 and S-102 loaded, applying
  **Charted only** makes S-102 inactive and R-101-102-B stops suppressing
  S-101 depth areas. Extend the existing `S98DefaultRules` tests for this.
- Load hook: a dataset of an excluded product loads inactive and is never
  in a layer stack built while it is loading.
- Modified detection: each covered control marks modified, and each
  uncovered control doesn't. Per-dataset visibility doesn't.
- Persistence: user combinations and the active id round-trip through
  `ViewerSettings`. An older `settings.json` without them loads.
- MCP: `list_display_combinations` and `set_display_combination`, with the
  `RouteMcpToolsTests` pattern.
- Analysis: a route check gives the same result with S-104 inactive as with
  it active (§1.3).

## 8. Implementation plan

Each step is a PR that builds and passes on its own.

1. **Model and service.** Add `DisplayCombination`, `BuiltInDisplayCombinations` and
   `DisplayCombinationService` with apply and modified detection, plus the tests in §7. No UI.
2. **Load hook.** Add `SetActiveOnLoad` on `IDatasetLoaderService`, call it in
   `ApplyPostRegistrationPolicies` and apply sub-layer visibility there.
3. **Persistence.** Add `ViewerSettings.DisplayCombinations`,
   `ActiveDisplayCombinationId` and their `SettingsJsonContext` entries.
4. **Toolbar pill and panel section.** Add the strings, the flyout, the
   **View** section, the Active-toggle tooltip and the ladder group. Document
   them in the viewer README under "Change the display".
5. **MCP tools.** Add `list_display_combinations` and
   `set_display_combination`, and the combination id in `capture_app_screenshot`.
6. **S-98 note.** Move "Predefined Combinations UI" and "Pre-defined
   Combination switching UX" out of §3.5 and link here.
7. **Later, with the IC loader:** IC combinations (§3.5), after Q3.

## 9. As built

Where the build settled what the note left open:

- **Viewing groups.** A combination also lists the viewing groups it *shows*
  (`ShownViewingGroups`), so **Charted only** can turn Important Text on as
  well as Other Text off. Applying hides and shows only the groups it names;
  the rest keep their state. The S-101 ids are Important Text `11` and Other
  Text `21, 23–30, 32` (viewing-group layers 101 and 102).
- **Everything loaded** puts every product in and sets nothing else.
- **Toolbar.** The display pills were folded into the **Display Settings**
  button before this was built, so the **View ▾** pill sits on its own at the
  top right of the map, left of the map tools.
- **Sub-layers at load.** Sub-layers exist only after a dataset's first
  render, so `IDatasetLoaderService.SetSubLayerVisibilityOnLoad` decides each
  one the first time the dataset produces it, alongside `SetActiveOnLoad`.
  Both apply on a dataset's first load in the session only, so a lazy reload
  keeps what the mariner set since.
- **Save current view** is a form inline in the View list (pill flyout and
  panel), not a separate dialog. It lists what's saved and what isn't (D).
- **Open questions.** Q1: a combination doesn't change what the route check
  uses. Q2: the built-ins follow §2, so only **Pilotage**, **Passage
  planning** and **Everything loaded** include S-124, and a user combination
  may leave it out; the map-wide Navigational warnings switch is never
  touched. Q3 waits for the IC loader. Q4: S-111 thinning is unchanged. Q5:
  product-level rules only, with the modified marker. Q6: a fresh install
  starts in **Everything loaded**.
