# Legends — Design Note

> Status: **Accepted.** All questions are answered (D13–D22). Tracked in
> [#1402](https://github.com/philliphoff/EncDotNet.S100/issues/1402). The mocks are the **Legends ·
> placement (round 4)** artboard on the *Chart display* page of the
> SoundCharts design canvas
> (<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>). It builds on
> **Per-product Legends · design pass** (round 3) and **Chart Reference ·
> design pass** on the same page, and on the
> [Symbol dictionary](https://github.com/philliphoff/EncDotNet.S100/blob/docs/symbol-dictionary-design/docs/design/symbol-dictionary.md)
> note (branch `docs/symbol-dictionary-design`).

This note decides where SoundCharts shows the legend of what it draws, when
the legend appears, and how it scales from one product to a dozen without
covering the chart.

## 0. Problem

Each feature that needed a legend added its own, wherever it was being built.
There are now five homes, each with its own look, rules and automation:

- weather fills and vector weather in the Layer stack;
- AML in a flyout from the display panel;
- route check verdicts in a card on the map;
- Library coverage outlines in the Library rows;
- one picked feature in Object information.

S-102, S-104, S-111, S-124 and S-411 have no legend at all, though S-102
depth shades and S-111 speed bands can't be read without one.

The number of products keeps growing: S-101, S-102, S-104, S-111, S-122,
S-124, S-129, S-411, S-412, AML and the weather overlays (wind, waves, SST,
currents, visibility, air temperature, tropical cyclones, fronts), plus the
route check and Library outlines. Any design that adds one thing per product
to the map stops working at about four products on a 1280 × 720 screen.

Round 3 recommended a **Key** button in the map's overlay column that opens a
one-product flyout, with the key at the cursor by default and full keys in
the left dock. None of it was built. The user has since proposed a
**Legends panel in the right dock**, where Object information is. This
note weighs that against round 3 and the other options.

## 1. Audit of today's surfaces

| # | Surface | What it shows | How it's shown | Code |
|---|---|---|---|---|
| A1 | Layer stack entry legend | Band swatches of a weather fill (#1072); symbol, line and outlined-area rows of vector weather (#1293) and AML | Under the entry, always, when the sub-layer carries `Legend`; left dock Layer stack tab | `LayerStackView.axaml` (`LayerStack.Entry.Legend`), `LayerStackViewModel.LegendOf`, `LayerStackLegendItemViewModel` |
| A2 | Legend model | `SwatchLegendEntry`, `SymbolLegendEntry`, `LineLegendEntry`, resolved for one palette; built by `LegendBuilder` and drawn by `LegendGlyphRenderer` | Carried on `GridCoverageSubLayer.Legend` and `VectorSubLayer.Legend`; S-98 rules copy it through | `Core/.../Portrayal/LegendEntry.cs`, `CoveragePortrayalResult.cs`, `VectorSubLayer.cs`, `Portrayals/Application/LegendBuilder.cs`, `Renderers.Skia/LegendGlyphRenderer.cs`, `S98DefaultRules.cs` |
| A3 | Weather producers | Wave, wind, SST, visibility, air temperature, freezing spray band ramps; TC symbols and wind areas; fronts | `LegendEntry.FromScheme`, `TropicalCycloneStyle`, `SurfaceAnalysisStyle.BuildLegend` | `Datasets.Weather/*Style.cs`, `WeatherPortrayal.cs`, `Datasets.Pipelines/SurfaceAnalysisDatasetProcessor.cs` |
| A4 | AML legend | One group per family (`AmlStyleCatalog.BuildLegend`), including the ESB sediment legend | Per sub-layer (A1), and in PR #1390 (#1374) a **Legend** flyout from the AML section of the display panel | `Datasets.Aml/AmlStyleCatalog.cs`, `AmlDrawing.cs`, `Families/EsbSediment.cs`; PR #1390 `EcdisDisplayPanelView` |
| A5 | Route check map legend | UKC AT ETA (meets, tight, below, dashed lower confidence, no data), FINDINGS (must review, check, info), stretch and corridor, WEATHER (storm at ETA) | A card at the map's bottom left while Check route is on, 60 px up when the Time HUD shows; folds to a 28 px **Legend** chip; fold persisted; the map reserves its height | `RouteCheckLegendView.axaml`, `RouteCheckLegendViewModel`, `RouteCheckLegendState`, `ViewerSettings.IsRouteCheckLegendOpen`, `MainWindow.axaml.cs` (`PlaceLegend`, `LegendReserve`), MCP `get_map_legend`/`set_map_legend` |
| A6 | Clearance profile key | The same UKC states as A5, in the bottom dock's Clearance tab | Inline in the profile | `ClearanceViewModel` (`Check_Legend*`) |
| A7 | Library outline swatches | Local, Online, Listed, Missing, Outdated: drawn like the map outline, "so the list doubles as the map's legend" | Row swatch in the Library panel | `LibraryOutlineStyles.cs`, `LibrarySwatch.cs`, `LibraryPanelView.axaml`, `LibraryCoverageOverlayController.cs`; `StormGlyphs.cs` for NHC rows |
| A8 | Object information | One feature: category glyph, portrayal note ("these symbols are ours"), AML not-drawn note, colour attribute swatches, the S-411 egg code | In the pick report, right dock | `PickReportView.axaml`, `PickReportViewModel`, `PickHit` |
| A9 | On-map notices | AML "CLB not used for depth checks" chip (bottom right), overscale, warning note | Chips on the map | PR #1390, `MainWindow.axaml` (`Map.Overscale`, `Map.WarningNote`) |
| A10 | Missing | No legend for S-102, S-104, S-111, S-124, S-411, S-122, S-129; no S-98 position legend (`LegendItems`, #1133) | — | `CoverageColorScheme` bands can carry labels, but these products don't set them |

The right dock already hosts activity tabs with `Dock = TabDock.Right`
(only `PickReport` today), and the bottom dock already shows a header tab
strip (TIMELINE · CLEARANCE · LOGS, `Dock.Tab.{Id}`). Check passage folds
the right dock to a 26 px edge tab (`IsRightDockFolded`).

## 2. What kind of legend is it?

| Kind | Examples | Changes with | Size | Home |
|---|---|---|---|---|
| **Value ramp** | S-102 depth shades, S-104 levels, S-111 speed bands, wind, waves, SST, visibility | Mariner settings, palette, sometimes the data and the view time | 4–15 rows | Legend tab |
| **Product key** | AML families, TC symbols, fronts, S-124 areas, S-411 colours, S-122/S-129 symbols | Palette | 3–40 rows | Legend tab, symbols drawn in view only |
| **Catalogue key** | S-101 (779 symbols, line styles and fills) | Palette, display settings | Hundreds | Chart reference (Reference tab); the Legend tab lists what is drawn in view and links out |
| **State legend** | Route check verdicts, Library coverage outlines | App state | 3–10 rows | Legend tab; a mode may also show a compact card on the map (D6) |
| **Feature explanation** | The picked feature's glyph, egg code, why it's drawn this way | The pick | One feature | Object information, linked to the Legend tab |
| **Position legend** | Units, depth datum, safety contour, compilation scale at the view centre (S-98 `LegendItems`) | Position, settings | 1–3 rows | Fixed first row of the Legend tab (D17) |

## 3. Options

Measurements are round 3's: activity bar 48, left dock 320 + 4, right dock
360 + 4, Timeline dock 260 + 4, status bar 30. With the left dock and the
Timeline open the map is 1068 × 550 on a 1440 × 900 window and 908 × 370 on
1280 × 720. Opening the right dock takes 364 px of width: 704 × 550 and
544 × 370.

### O1 Today: five homes (A1–A8)

Costs little map space, but every legend has its own place and rules.
Nothing links them, and most products have no legend.

### O2 Round 3: Key flyout from the overlay column

A Key button after Display settings opens a 260 px flyout over the map, one
product at a time, capped at 40 % of the map height. Key at the cursor by
default; full keys in Display controls (later moved to Chart reference).

- For: no map cost while closed; next to the map; the flyout pattern is
  Display settings'.
- Against: one product at a time, so a stack of six products is six round
  trips through the combo. Open, it covers 11 % of a 908 × 370 map, beside
  the zoom buttons, and closes on a map click, so it can't stay up while
  you work. Round 3 measured the column with four mode buttons (to y 318);
  Range & bearing (#1130) has since made it five, so it already reaches
  y 352 of a 370 px map, and a Key button would push it to y 386, past the
  Timeline dock. The cursor key needs a hover hit-test across every
  coverage layer.

### O3 Legend inline in the left dock (Datasets rows)

Each Datasets row expands to its legend, as GIS tables of contents do.

- For: the legend sits with the eye and the opacity; no new place.
- Against: the left dock is the Library, Routes or Search as often as
  Datasets, so the legend disappears when you switch. Rows are per dataset
  (seven S-102 tiles show seven identical ramps) and the panel is about
  control, not reading.

### O4 Legend tab in the bottom dock

A LEGEND tab beside TIMELINE, CLEARANCE and LOGS, with one column per
product.

- For: wide and short suits many products side by side at 1440 × 900.
- Against: the Timeline is what you need next to a time-varying legend,
  and choosing LEGEND hides it, which puts the Time HUD back on the map.
  Hidden below 900 px window width. 264 px of a 634 px map height.

### O5 The user's idea: a Legend tab in the right dock

A LEGEND tab beside OBJECT INFORMATION in the right dock, one collapsible
section per product, scrolling.

- For: off the map, never covers it; one place for every kind of legend;
  scrolls as products grow; the right dock is already the "what am I
  looking at?" dock (Object information, and Chart reference's Reference
  entries); the dock and its tab strip exist. When Object information is
  open it costs no extra width. It stays up while you pan and scrub time.
- Against: opened from closed, it takes 364 px: 40 % of the map's width at
  1280 × 720. Legend and pick can't be read side by side, only one tab
  apart. Check passage folds this dock, so the route-check legend can't
  only live here.

## 4. Recommendation

Adopt **O5**, the right-dock Legend tab, as the single home of legends, with
three rules from round 3 and two exceptions. Q1–Q7 of the first draft are
answered; their answers are D13–D19.

1. **One Legend tab in the right dock**, between Object information and
   Reference, with the bottom dock's tab strip: OBJECT INFORMATION · LEGEND ·
   REFERENCE. Tabs only; the tabs never stack (D16).
2. **In view by default.** Only products drawn in the current extent at the
   view time, in draw order, top of the plane first (round 3's E). Loaded
   but not in view is one muted line; **All loaded** shows them. Above the
   sections, a fixed row gives the S-98 position legend (D17).
3. **Collapsible sections, constant header height.** Collapsed, a product is
   one 30 px row; eight products are 240 px, which fits a 370 px map's
   height with room to expand one.
4. **Exception: mode legends.** A mode that already folds the right dock
   (Check passage) keeps a compact card on the map while the Legend tab
   isn't shown. It's the same view model as the tab's section, never both
   at once.
5. **Exception: the pick.** Object information keeps the one-line key of
   the picked feature (round 3's C, in the report), with **In legend** to
   jump to the row.

What this changes from round 3:

- Round 3's Key button stays in the overlay column, renamed **Legend**, but
  it opens the right dock's Legend tab instead of a flyout over the map.
  On maps too short for one column, the column wraps (D15).
- The key at the cursor drops from default to later and optional. The
  Legend tab answers "what does this colour mean?" without a hover, and
  the pick answers it for one place.
- Round 3's full keys (D), already moved to Chart reference's study page,
  stay there. The Legend tab links to them; it doesn't hold catalogues.
- Kept from round 3: in view only, constant collapsed size, never colour
  alone, values from the catalogues and Mariner settings.

Why the right dock, not the flyout: legends are read for a while, not
glanced at. With several products in view the mariner wants all of them up
while panning and scrubbing time, and a flyout that closes on a map click
and shows one product can't do that. The right dock's cost is width, not
obstruction, and the map redraws into what's left rather than being covered.

## 5. Decisions

**D1 — One home.** Every legend row in the viewer comes from one viewer
service (`LegendService`) as `LegendSection`s of `LegendEntry` rows. The
Legend tab, the route-check map card and MCP all read it. The Layer stack,
the AML flyout and `RouteCheckLegendViewModel` stop building their own.

**D2 — The Legend tab.** A `TabDock.Right` activity tab, id `Legend`, after
`PickReport` and before Chart reference's `Reference` (D13). The right dock
header gets the bottom dock's tab strip, with automation ids
`Dock.Tab.PickReport`, `Dock.Tab.Legend` and `Dock.Tab.Reference`. The
selected tab persists (`LastSelectedRightTab`). A pick switches to Object
information, as it opens the dock today.

**D3 — Opening it.** Two ways, plus links. Nothing opens it automatically:

- the **Legend** button in the map's overlay column (D14, D15);
- **View** > **Legend** (Ctrl+Shift+L);
- links: **In legend** on a pick's key line, **Show legend** on a Datasets
  row's menu, **Legend** in the AML display section.

**D4 — Sections.** One per product or overlay, not per dataset: seven S-102
tiles are one S-102 section. Each header has the product id chip, its name,
a source and time summary, and a chevron. The body shows the rows, a
one-line caption where the unit or rule needs it ("Speed, kn · longer arrow
= faster"), and the Mariner settings the ramp follows ("Safety contour
10 m · Change…"). Expanded state is remembered per product. Toolbar:
**In view | All loaded**, a filter field, **Collapse all**.

**D5 — Order and scope.** Draw order, top first, matching the Layer stack
and `AmlStyleCatalog.BuildLegend`. State sections (Route check, Library
coverage) go on top when shown. In view uses the same extent test the
Datasets panel uses; the list updates on pan end, never under the pointer.
A time-varying product with no data at the view time shows "No data at
14:20" in its header.

**D6 — Mode cards on the map.** The route-check card stays at the bottom
left while Check route is on and the Legend tab isn't shown. It's the
same section, drawn compact (today's `RouteCheckLegendView`, 168 px), with a
**Legend** chip fold. When the Legend tab is shown, the card hides and the
map's legend reserve drops to 0. No other on-map legends are added.

**D7 — Catalogue keys.** S-101 and other catalogue products list the
symbols drawn in view, up to 12, then "Show all 54 in Chart reference".
Each row is a Chart reference term and opens in the Reference tab.

**D8 — What moves out.**

- Layer stack entries replace their legend with a one-line **Legend** link
  to the section.
- PR #1390's AML flyout becomes the AML section in a later slice (D18).
- Datasets rows keep their single product swatch (identity, not legend).
- Library row swatches stay: the Library panel is where outlines are read.
  A Library coverage section mirrors them while outlines are on the map.
- Object information keeps the feature explanation.

**D9 — Narrow windows.** When opening the right dock would leave the map
narrower than 480 px, it opens folded: the existing 26 px edge tab, labelled
LEGEND. Clicking the edge tab shows the dock over the map's right edge
without resizing it; Escape or a map click folds it again.

**D10 — Palettes.** Rows are rebuilt on a palette change, as `LegendBuilder`
requires. Swatches sit on a tile of the palette's chart background, so a
Night swatch is seen against the Night chart, not the panel's grey.

**D11 — Accessibility.** Sections are expanders with the name "S-111
Surface currents, 9 entries"; rows are list items named by their label
(`LegendEntry.Label`). Never colour alone: every row has a label and, where
it has one, a shape. Automation ids: `Map.Legend`, `Legend.Toolbar.InView`,
`Legend.Filter`, `Legend.Position`, `Legend.Section.{productId}`,
`Legend.Row`, and `Legend.Row.InLegend` on the pick's link.

**D12 — MCP.** `get_legend` returns the position row, the sections (id,
title, in view, expanded, rows with kind and label) and whether the tab is
shown. `set_legend` opens or closes the tab, sets In view, and expands or
collapses a section. They replace `get_map_legend` and `set_map_legend`
(preview packages: a clean break, noted in the PR). `get_legend` works
headless: it reads the service, not the view.

**D13 (was Q1) — Reference is a third tab.** The strip is OBJECT
INFORMATION · LEGEND · REFERENCE. At the 11 px, 1 px-tracked caps the
bottom dock uses, the three labels measure about 150, 52 and 76 px; with
two 14 px gaps that's about 306 px, inside the 318 px left by the 12 px
inset, the close button and its padding at the default 360 px width. When
the dock is narrowed so the labels no longer fit, each tab shows its icon
only (Cursor, the legend glyph, BookOpen), with the full title as its
tooltip and accessible name. Labels are never truncated and the strip
never scrolls.

**D14 (was Q2) — Never automatic.** Loading a product never opens the
Legend tab, and there's no first-run notification. The overlay column's
Legend button is how it's found.

**D15 (was Q3) — The Legend button and the column wrap.** The **Legend**
button (`Map.Legend`) is the sixth mode button, after Display settings. It's
pressed (accent fill, `aria-pressed`) while the Legend tab is showing, and
pressing it again closes the dock (or returns it to the tab it showed
before, when Object information was open). With six mode buttons the column
is 374 px:

| Window | Map height | Column |
|---|---|---|
| 1440 × 900, Timeline open | 550 px | One column, to y 386 |
| 1280 × 720, Timeline closed | 634 px | One column, to y 386; the Time HUD is at the bottom centre, clear of it |
| 1280 × 720, Timeline open | 370 px | Wraps |

The column wraps when it plus a 12 px margin top and bottom (398 px) is
taller than the map. Wrapped, the six mode buttons stay in the right-hand
column (y 12–214) and the zoom buttons and compass move to a second column
to their left, below the View pill (y 48–202). The pair is 84 px wide and
ends at y 214, leaving the lower 156 px of a 370 px map clear. The two
groups can't stack in one column there: they need 356 px and the map has
346 between its margins.

**D16 (was Q4) — Tabs only.** The Legend and Object information never show
together. A pick's key lines link to their rows with **In legend**, which
switches tab, expands the section and rings the row.

**D17 (was Q5) — Position legend first.** The S-98 position legend (#1133)
is the Legend tab's first row, fixed above the product sections, under the
toolbar: units, depth datum and safety contour, then the cell at the view
centre with its edition and compilation scale against the display scale.
It doesn't scroll or collapse. It updates on pan end, and over no chart
says "No chart at the view centre".

**D18 (was Q6) — #1390 lands as is.** The AML flyout shipped in PR #1390
(merged). A later slice moves its content into the AML section and points the display
section's **Legend** link at the tab.

**D19 (was Q7) — Hover to find, later.** Hovering a legend row rings the
matching features on the map, as Chart reference's In view list does. It
gets its own later slice, not the first build.

Built in #1412. Chart reference has no In view list, so the rings follow the
pick highlight: an accent stroke with a light casing (grey on Dusk and
Night), a screen-constant ring around a point, the outline of a curve or an
area, with no fill so overlapping areas don't darken. A row maps back to its
features from the display lists the stack already holds; nothing is
portrayed again:

- A catalogue key row (S-101, S-57, D7) rings the features that draw its
  symbol in view at the view scale, as the key counts them.
- A vector layer's row (S-124, S-411, AML, fronts, tropical cyclones) rings
  each drawn feature whose **In legend** row it is (the rule #1410 uses for a
  pick): the layer's only row, or the line or area row named like the
  feature's class, which the processor's feature list gives.
- A station or isoline layer with one row rings what it draws.
- A band of a colour or arrow ramp (S-102 depths, a weather field, S-111
  speeds) tints its area instead
  ([#1433](https://github.com/philliphoff/EncDotNet.S100/issues/1433)): an
  accent wash, firmer along its edge, over the view's cells whose drawn value
  is in the band. The tint is a mask of one cell per three screen pixels, at
  most 256 on the long side. Each cell's centre goes into the grid's own CRS
  (the inverse of the renderer's node projection), so a UTM S-102 cell or a
  grid in a 0–360° frame tints where it's drawn. The top fill decides a cell,
  as it hides the fills under it; arrows tint every grid cell in the band, not
  only the thinned arrows drawn. A fill clipped to water isn't clipped for the
  tint.

The find waits until the pointer has rested on a row for 150 ms, and rings at
most 100 features, top of the stack first. Keyboard focus on a row does the
same as hover. The rings clear when the row is left, when the tab closes, and
as soon as the map moves; while the row is still hovered they come back once
the legend has followed the new view. `set_legend`'s `findRow` rings a row
from MCP and its `find` reads what is ringed, and for a band, what's tinted
(`find.tint`: share of the view, area, bounds).

**D20 (was Q8) — Zoom moves when the column wraps.** When the column wraps,
the mode buttons keep their places on the right edge and the zoom buttons
and compass move to the second column, as D15 describes.

**D21 (was Q9) — Icon tabs are allowed.** The right dock has no minimum
width tied to its tab labels. Below the width where the labels fit (about
330 px), the tabs show icons only, as D13 describes.

**D22 (was Q10) — Reference is always in the strip.** The strip always
shows all three tabs, so their positions never change. Before any term is
opened, the Reference tab shows its empty state.

## 6. Open questions

None at the moment.

## 7. Slices

1. [#1403](https://github.com/philliphoff/EncDotNet.S100/issues/1403) **Model and service.** `LegendSection`, `LegendService` gathering the
   sub-layer legends that A1 shows today, in-view tracking and draw order.
   MCP `get_legend`. No UI change.
2. [#1404](https://github.com/philliphoff/EncDotNet.S100/issues/1404) **Right dock tab strip and Legend tab.** Generalise the bottom dock's tab
   strip to the right dock, with the icon-only fallback (D13); the Legend
   tab with weather and AML sections; View menu and keyboard. Layer stack
   entries link out (D8).
3. [#1405](https://github.com/philliphoff/EncDotNet.S100/issues/1405) **Legend button and column wrap.** The overlay column's Legend button
   with its pressed state, and the wrap rule for short maps (D15).
4. [#1406](https://github.com/philliphoff/EncDotNet.S100/issues/1406) **Value ramps for S-100 products.** S-102 from Mariner settings, S-104,
   S-111 bands, S-411, S-124 areas: label their `CoverageColorScheme` bands
   so `LegendEntry.FromScheme` returns rows, or build them with
   `LegendBuilder`.
5. [#1407](https://github.com/philliphoff/EncDotNet.S100/issues/1407) **Position legend.** The fixed first row (D17), in `get_legend` too.
6. [#1408](https://github.com/philliphoff/EncDotNet.S100/issues/1408) **Route check section.** `RouteCheckLegendViewModel` reads the service;
   the D6 card rule; `set_legend` replaces `get_map_legend` and
   `set_map_legend`.
7. [#1409](https://github.com/philliphoff/EncDotNet.S100/issues/1409) **AML.** #1390's flyout becomes the AML section (D18).
8. [#1410](https://github.com/philliphoff/EncDotNet.S100/issues/1410) **Links.** **In legend** from Object information, **Show legend** from
   Datasets rows, Chart reference terms on rows (opening in the Reference
   tab once it exists).
9. [#1411](https://github.com/philliphoff/EncDotNet.S100/issues/1411) **Library coverage section, catalogue keys and narrow windows.** The
   Library section (D8), S-101 in-view symbols (D7), and the D9 fold.
10. [#1412](https://github.com/philliphoff/EncDotNet.S100/issues/1412) **Hover to find** (D19).
