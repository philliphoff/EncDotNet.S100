# Symbol dictionary — Design Note

> Status: **Planned.** Decisions D1–D6 are made. Tracked in
> [#1633](https://github.com/philliphoff/EncDotNet.S100/issues/1633); Phase 1 is #1634–#1639. §7 slices the build: the Chart reference activity
> first, then the Reference tab, then Object information. A working
> prototype of the study page, generated from the catalogues bundled in
> `EncDotNet.S100.Specifications`, is at
> <https://claude.ai/artifact/Peujh6ZNQhhoJ4MsNAJT1i>. The viewer mocks are
> the **Chart Reference · design pass** artboard on the SoundCharts design
> canvas (<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>). It fits the
> accepted [Legends](legends.md) note, whose right dock already reserves the
> Reference tab (D2, D13, D22).

This note designs a searchable reference of every symbol the viewer can draw:
for each product specification, each feature type with an example of how it
looks, what it means, and when each of its symbols is used. It's the S-100
counterpart of INT 1 / Chart No. 1, built from the catalogues instead of
drawn by hand.

## 0. Problem

Mariners learn to read paper charts from INT 1, and S-52 ECDIS from the
ECDIS Chart 1 cell. S-100 makes this harder:

- **More products on one screen.** S-101 alone has 779 symbols, line styles
  and fills. S-201, S-124, S-411, S-421, S-122, S-127, S-129 and S-131 each add
  their own, and S-102, S-104 and S-111 add colour schemes and arrows. Most of
  them have no printed reference.
- **New and changed symbols.** S-101 adds symbols that S-52 never had and
  draws others differently.
- **One feature type, many symbols.** The portrayal rules choose a symbol
  from the feature's attributes and the mariner's settings. A lateral beacon
  is one of about a dozen symbols, depending on its shape, its colours and
  whether simplified symbols are on.
- **Three palettes.** A symbol looks different at Day, Dusk and Night.

SoundCharts already answers "what is this?" for one feature at a time
(Object information). Nothing answers "what does a … look like?" or "what
symbols might I see?", and the S-164 audit lists the missing cross-product
legend as a gap (`LegendItems`, S-98 9.1.5, in
[S-164 conformance](s164-conformance.md)).

## 1. What the catalogues already give us

Everything the dictionary needs is in the feature and portrayal catalogues
the library already loads. Nothing has to be written by hand.

| Need | Source | Notes |
|---|---|---|
| Glyph | Portrayal catalogue `symbol`, `lineStyle`, `areaFill` and their SVG/XML | Drawn with `SvgProcessor.Process` for the chosen palette, as `LegendBuilder` does. |
| Symbol description | Each item's `<description>` | E.g. `BCNLAT15`: "major lateral beacon, red, simplified". |
| Feature name, definition, S-57 acronym | Feature catalogue `S100_FC_FeatureType` `name`, `definition`, `alias` | Lets a mariner search `BOYLAT` and find *Lateral Buoy*. |
| Attribute value labels | Feature catalogue `listedValues` | Turns `colour[1] == 3` into "colour = red". |
| Feature → symbols, with conditions | Portrayal rules (Lua and XSLT) | See §2. |
| Display category | Viewing group → viewing group layer → display mode | Display Base, Standard Display or Other Information. |
| Chart 1 section | The viewing group's description | S-101's viewing groups name the INT 1 sections, e.g. 27020 "Standard: P, Q, R, S - Buoys and Beacons, Lights, Fog signals, RADAR". |
| Colour fills and plain lines | `ColorFill:` and `LineStyle:_simple_` instructions in the rules | E.g. Depth Area: DEPIT, DEPVS, DEPMS, DEPMD, DEPDW, NODTA. |

## 2. Linking feature types to symbols

The prototype reads the rules statically. For each rule file it finds the
catalogue items the file names, and for each one it walks up the enclosing
`if`/`elseif`/`else` blocks to build the condition, then decodes the
condition through the feature catalogue. Shared rule files (`require
'DEPARE03'`) are followed, and their items are marked "via DEPARE03". Item
families a rule composes from a prefix (`'SOUNDG' .. digit`, `SAFCON`) are
linked to that rule.

What this gets from the bundled catalogues:

| Spec | Feature types | With a symbol, fill or line | Items | Items linked to a feature |
|---|---|---|---|---|
| S-101 | 190 | 152 | 779 | 434 |
| S-401 | 178 | 140 | 926 | 622 |
| S-201 | 55 | 23 | 149 | 99 |
| S-131 | 35 | 29 | 6 | 4 |
| S-127 | 24 | 20 | 6 | 5 |

The S-101 feature types left without a link are mostly aggregates and
associations (`C_AGGR`: Fairway System, Traffic Separation Scheme), meta
features (Quality of Survey) and features drawn by generic rules. Most of the 345 S-101 items left unlinked are sounding and safety contour digits (SOUNDG, SOUNDS and SAFCON families, 218 items), which rules compose from numbers.
S-411's rules use generic templates keyed by file name, and the prototype
doesn't link them yet.

Static reading is good enough to browse, and its conditions are readable
("drawn as point; simplified symbols off; beacon shape = pile beacon,
colour = red"). It isn't exact: it can't evaluate computed values, and
`else` branches read as "none of the cases above". The exact version is
probing (§7, R13).

## 3. The library piece

The index is built at runtime from the catalogues the viewer has loaded, so
a catalogue the user adds or replaces is described as it draws. It lives in
`EncDotNet.S100.Datasets.Pipelines`, the lowest project that references
both `EncDotNet.S100.Features` and `EncDotNet.S100.Portrayals` (and the
Skia glyph renderer and the built-in specifications); the viewer, the MCP
tools and the CLI already reference it.

### 3.1 Sources

Everything comes from types that exist today:

| Need | API |
|---|---|
| Feature types | `FeatureCatalogue.FeatureTypes`: `FeatureType.Code`, `Name`, `Alias` (S-57 acronym), `Definition`, `IsAbstract`, `PermittedPrimitives`, `AttributeBindings` |
| Attributes and values | `FeatureCatalogue.SimpleAttributes`: `SimpleAttribute.Code`, `Name`, `Alias`, `Definition`, `ValueType`, `ListedValues` (`Label`, `Code`, `Definition`) |
| Symbols, line styles, fills | `PortrayalCatalogue.Symbols`, `LineStyles`, `AreaFills` (`CatalogItem.Id`, `Description.Name`, `Description.DescriptionText`) |
| Display category, Chart 1 section | `ViewingGroups`, `ViewingGroupLayers`, `DisplayModes`; `DisplayModeMembership.Resolve` |
| Display settings | `PortrayalCatalogue.ContextParameters`: `Id`, `Description`, `Type`, `Default` |
| Rule sources | `PortrayalCatalogue.RuleFiles` and `PortrayalCatalogueProvider.FetchAssetAsync(RuleFile)` (Lua or XSLT text) |
| Glyphs | `CatalogueLegendBuilder.Symbol` and `.Line` by name, drawn by `LegendGlyphRenderer` |
| Cache key | `FeatureCatalogueManager.GetCatalogueHashAsync(SpecRef)` and `PortrayalCatalogueManager.GetCatalogueHashAsync(SpecRef)` |

### 3.2 Types

```csharp
namespace EncDotNet.S100.Datasets.Pipelines.Reference;

public sealed class ChartReferenceIndex
{
    public static ValueTask<ChartReferenceIndex> BuildAsync(
        SpecRef spec, FeatureCatalogue? features, PortrayalCatalogueProvider portrayal,
        CancellationToken cancellationToken = default);

    public SpecRef Spec { get; }
    public IReadOnlyList<FeatureTypeEntry> FeatureTypes { get; }
    public IReadOnlyList<PortrayalItemEntry> Items { get; }          // symbols, line styles, area fills
    public IReadOnlyList<AttributeEntry> Attributes { get; }
    public IReadOnlyList<DisplaySettingEntry> DisplaySettings { get; }

    public bool TryGet(ReferenceKey key, out ReferenceEntry entry);
    public IReadOnlyList<ReferenceHit> Search(string query, ReferenceKind? kind = null, int limit = 200);
}

public readonly record struct ReferenceKey(SpecRef Spec, ReferenceKind Kind, string Code);
public enum ReferenceKind { FeatureType, Symbol, LineStyle, AreaFill, Attribute, DisplaySetting }

public sealed record FeatureTypeEntry(...)   // code, name, S-57 acronym, definition, primitives,
                                             // viewing group, DisplayCategory?, Chart1Section?,
                                             // Uses (ItemId + conditions), ColorFills, Pens
public sealed record SymbolUse(string ItemId, IReadOnlyList<RuleCondition> Conditions, string? ViaRule);
public sealed record RuleCondition(...)      // attribute code, index, operator, value code,
                                             // or a context parameter and its value
```

Conditions are kept as data, not text. The viewer turns
`colour[1] == 3` into "Colour is red" with the feature catalogue's labels,
so the words can be translated and the conditions can be matched against a
picked feature later (§7, R11).

### 3.3 Reading the rules

The rule reader ports the prototype's extractor (§2):

- **Lua** (S-101, S-131, S-401): item names in each rule file, the
  enclosing `if`/`elseif`/`else` conditions, `require` followed
  transitively, prefix families (`'SOUNDG' ..`), `ColorFill:` and simple
  `LineStyle:` instructions, and `local viewingGroup =`.
- **XSLT** (S-201, S-124, S-411, S-421 and the other GML products):
  `<symbol reference=…>`, symbol file paths, the template's `match` and
  its `<viewingGroup>`.

The reader records each condition's line number, so a later trace of a
rule run (R12) can say which conditions held for one feature.

### 3.4 Building, caching, changes

`ChartReferenceIndex.BuildAsync` reads one spec on a worker thread. The
prototype read 13 specs in a few seconds in Python; R1 measures the C# build
and sets a budget (S-101 under 300 ms). The index is held in memory per
spec, keyed by the two catalogue hashes and an index format version. A disk
cache waits until the expensive step, running the rules (R13), exists.

Neither catalogue manager raises an event when a catalogue is added or
replaced. R2 adds `CataloguesChanged` to both, raised by `SetPath` and
`SetSource`, rather than having the index watch the viewer's catalogue
view models.

Glyphs are not part of the index. A caller draws them for its palette with
`CatalogueLegendBuilder`. That builder draws symbols and line styles by name
but has no area fill row; R3 adds one, which the Legend tab can use too.

## 4. The viewer

The mocks are the **Chart Reference · design pass** artboard on the *Chart
display* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>), sections A–G. In the
viewer the dictionary is called **Chart reference**, because it holds
definitions and display settings as well as symbols.

### 4.1 How it fits with the Legend tab

The [Legends](legends.md) note is accepted and built. The right dock
has a tab strip, OBJECT INFORMATION · LEGEND · REFERENCE, and the Reference
tab is always in it (D2, D13, D22). The Legend tab lists what is drawn in
view, and for catalogue products it lists the symbols drawn in view, up to
12, in `CatalogueSymbolKey` (D7). Object information shows a one-line key
of the picked feature, with **In legend** (D8, `PickLegendKeyLine`).

The jobs divide like this:

- The **Legend tab** answers "what is on this chart?": only what is in
  view, by product, with labels.
- **Chart reference** answers "what does this mean, and why does it look
  like that?": everything in the loaded catalogues, with definitions, the
  forms a symbol can take, and what chooses them.

Each catalogue key row in the Legend tab becomes a term. The "{0} of {1}
symbols drawn in view" caption gets the link D7 promised: **Show all in
Chart reference**, which opens the activity at that product's symbols with
In view on. Round 3's full keys live in the study page (§4.5).

### 4.2 Four depths, one set of entries

There are four kinds of entry: **feature type**, **symbol** (point symbol,
line style or area fill), **attribute** (with its listed values) and
**display setting** (a portrayal context parameter such as Simplified
symbols). Every way into the reference lands on one of these entries. The
four depths differ only in how much of the entry they show (artboard A):

| Depth | Where | Opens on | Shows |
|---|---|---|---|
| Glance | The Legend tab; the pick's key line in Object information | The Legend button, a pick | What is drawn in view; the picked feature's symbol and one line (legends D7, D8) |
| Peek | A popover over any term | Hover or focus after 500 ms; Space pins it | The definition, the glyph, Open in reference |
| Read | A **Reference** tab in the right dock, beside Object information | Clicking a term, F1 on a pick | The whole entry |
| Study | **Chart reference** activity (left dock); **Open as page** (full-content panel) | Activity bar, Help › Chart reference, Ctrl+Shift+K | Every entry of a product |

### 4.3 Object information (artboard B)

Object information today shows a generic Fluent icon per feature type
(`FeatureGlyphs`) and links names to the S-100 eXaminer website, which needs
a connection a ship may not have. The proposal:

- **The drawn symbol** replaces the generic icon: the symbol the renderer
  actually drew for this feature, in the current palette. The stack's
  display list already holds every instruction a feature emitted, keyed by
  its feature reference (`CatalogueSymbolKey` and `CatalogueLinework` read
  it for the Legend), so the pick reads its parts from there: no renderer
  change and no second portrayal. Areas show a patch, lines a short sample.
  Products that aren't catalogue-portrayed (S-102, weather fields) keep
  their key line (`PickLegendKeyLine`), which doesn't cover catalogue
  portrayal.
- **Drawn as** names each drawn part as a term, with a **Why this symbol**
  folded by default: the rule's conditions that held for this feature, in
  words. For example: "Simplified symbols is on · Colour is red · Beacon
  shape is a stake, pole, perch or post". A setting in the reason links to
  that switch in Display settings. When the symbol is the catalogue's
  fallback (the magenta ? of BCNDEF13), say so plainly.
- **Attributes** use the feature catalogue's names, not camel-case codes,
  and every name and value is a term. Codes and S-57 acronyms move into the
  entry and Copy identity.
- **S-57 cells** are drawn with the S-101 catalogue, so their picks map to
  S-101 entries through the feature catalogue's S-57 aliases (`BCNLAT` →
  Lateral Beacon, `COLOUR` → Colour). A code with no alias stays plain text.
- The **eXaminer link** stays, in the peek and the entry, but is never the
  only way to a definition. The per-attribute eXaminer buttons go.

### 4.4 Entries, in the right dock (artboard C)

Entries are 360 px wide and open in a **Reference** tab of the right dock,
next to **Object information**, so following links never loses the pick. A
breadcrumb and back/forward (Alt+←, Alt+→) keep the trail. There is never a
second window.

- **Feature type** (C1): the glyph large and at chart size, the definition,
  display category, Chart 1 section, a count in view with **Find**, and
  **How it is drawn** as a variant table. The table's rows and columns are
  the attributes that choose the symbol (for Lateral Beacon: beacon shape ×
  colour). A **Simplified / Paper chart** switch shows the other style
  without changing the chart, and the variant you see now is ringed. Parts
  drawn by other rules (topmark, light, name) are links, not rows. The
  fallback symbol has its own line.
- **Symbol** (C2): Day, Dusk and Night side by side, its meaning in a
  sentence, the setting it depends on, its counterpart in the other style,
  its family, where it came from (S-52 Presentation Library or new in
  S-101), and the feature types that use it.
- **Attribute** (C3): the definition and each listed value's definition.
  Opened from a feature, each value shows the symbol it gives that feature
  in both styles, and the picked feature's value is highlighted. It also
  lists the feature types whose symbol it changes.
- **Display setting** (C4): what it does, its current state with **Change
  in Display settings**, and before/after pairs from the rules that branch
  on it (in S-101, Simplified symbols switches between 87 paper-chart forms
  and 113 simplified ones).

The variant table, the value-to-symbol columns and the before/after pairs
come from running each rule over the values of the attributes it reads
(§7, R13). Until then, C1 falls back to the prototype's list of
symbols with their conditions.

### 4.5 Browse and study (artboards D, E)

**Chart reference** is a left-dock activity, after Feature search (id
`ChartReference`, order 52, icon `BookCompass`, D1). Its list reads like Chart 1: grouped by section,
glyph first. Its controls:

- a **product** combo: loaded products first, then every bundled
  catalogue, S-401 included (D4);
- **search**;
- **Features / Symbols**, to list feature types or individual symbols;
- **In view**, which limits the list to what is drawn in the current
  extent. It reuses the display lists the Legend tab's catalogue key reads,
  so the two agree. It is on when the activity opens from the Legend tab,
  and off from the Help menu.

Hovering a row rings its instances on the chart through `LegendFindService`.
Until the Reference tab exists (R7), selecting a row expands it in place to
show a summary of the entry. Afterwards, selecting a row opens the entry in
the Reference tab.

**Open as page** shows the reference in the full-content panel, over the
map, as Settings does (#845). It uses the prototype's layout: a tile grid
by Chart 1 section, a chip per product, Day/Dusk/Night and Simplified/Paper
chart switches, and the entry on the right. The switches preview only; they
don't change the chart. **Export key…** writes the current product and
palette as a PDF.

### 4.6 Cross-referencing rules (artboard F)

- **A term looks like a term.** Terms have a dotted underline in the text
  colour. Blue is kept for actions (Find, Change in Display settings).
- **Peek on hover or focus, read on click.**
- **Every link is two-way**, generated from one index built from the
  catalogues, never kept by hand.
- **Entries reflect the chart as it is**: the current palette, the variant
  you see now ringed, and a note with a link when the display category hides
  the feature.
- **Say what is new.** About 135 S-101 symbols don't come from the S-52
  Presentation Library (from each SVG's `iho:source`). They get a **New in
  S-101** badge and filter.
- **Offline first.** Everything comes from the catalogues on disk.
- **Entry points**: Object information (names, values, Drawn as, F1), the
  map's context menu (Look up in reference), the Legend tab's catalogue key rows, Display
  settings (setting names), Layer stack legend rows, Feature search hits,
  the Help menu, and `soundcharts://reference/<spec>/<kind>/<code>` links
  for MCP and docs.
- **Automation ids**: `ActivityBar.ChartReference`,
  `ChartReference.Product`, `ChartReference.Query`, `ChartReference.Mode`,
  `ChartReference.InView`, `ChartReference.List`, `ChartReference.Row`;
  `Dock.Tab.Reference`, `Reference.Entry`, `Reference.Back`,
  `Reference.Forward`, `Reference.Term`, `Reference.Peek`.

## 5. Later

- **Chart 1 cells.** When the IHO S-101 Chart 1 test cells are loaded, link
  each entry to its place in them and show a rendered crop.
- **Export key… to PDF** from the study page, through SkiaSharp's PDF
  backend (`SKDocument.CreatePdf`), after the HTML export (R14) settles
  what a printed key holds.
- **Localisation.** Catalogue descriptions carry a language; show the
  mariner's language where a catalogue has it.
- **Overlay entries.** Weather, AIS, NDBC and CAP have legend rows but no
  feature catalogue; they need short written definitions to be entries.

## 6. Decisions

**D1 — Icons.** The activity uses `BookCompass` and the Reference tab
`BookInformation`. Feature catalogues keeps `BookOpen`. This amends legends
D13, which gave the Reference tab `BookOpen`.

**D2 — The name is Chart reference**: the activity title, the Help menu
item, and the Legend tab's **Show all in Chart reference** link. "Chart 1"
names the paper publication and S-101's test cells.

**D3 — Unreached items are shown, grouped and marked.** Families a rule
assembles from numbers (`SOUNDG`, `SOUNDS`, `SAFCON`) are one entry each,
linked to the rule that assembles them. Items no rule names are listed under
**Not used by a rule**, last in Symbols mode. A symbol that a line style
places along its line, or an area fill tiles as its pattern, is reached when
a rule names that line style or fill (#1654): its entry lists them under
**Used inside**. In view hides them, since they
are never drawn.

**D4 — Every bundled catalogue is in the product combo**, S-401 included:
loaded products first, then the rest.

**D5 — Why this symbol explains every drawn part.** The main symbol gets
its full reason. Each other part (topmark, light, text) gets one line
naming it, with a link to its entry and its own reason folded underneath.

**D6 — Every display category is shown.** A feature type whose rule sets
different viewing groups by geometry or case lists one line per distinct
category ("Point: Display base · Area: Standard"). The list's category tag
shows the most visible one: Base, then Standard, then Other.

## 7. Slices

Three phases, in the order asked: the activity first, then the Reference
tab, then Object information. A fourth phase makes the variants exact.
Each slice ships on its own, with tests and a whats-new bullet.

### Phase 1: the Chart reference activity

1. [#1634](https://github.com/philliphoff/EncDotNet.S100/issues/1634) **R1 Index.** `ChartReferenceIndex` in
   `EncDotNet.S100.Datasets.Pipelines/Reference` (§3): entries for feature
   types, items, attributes and display settings; the Lua and XSLT rule
   readers; search with ranking (exact code or acronym, then name prefix,
   then text). Tests in `EncDotNet.S100.Pipelines.Tests` against the
   built-in catalogues through `Specification.CreatePortrayalCatalogueSource`
   and `Specification.TryOpenFeatureCatalogue`. They assert the prototype's
   numbers as floors: S-101 links at least 152 of 190 feature types; for
   example, `BCNLAT21` is used by `LateralBeacon` when Simplified symbols is
   on and the colour is red. The build time is measured against the budget.
   Also records each symbol's `iho:source`, for the New in S-101 badge;
   every viewing group a rule sets, by geometry or case, for D6; and the
   assembled families and unreached items for D3. No UI.
2. [#1635](https://github.com/philliphoff/EncDotNet.S100/issues/1635) **R2 Service and changes.** `CataloguesChanged` on both catalogue
   managers. A viewer `ChartReferenceService` builds indexes lazily per
   spec off the UI thread, keyed by the two hashes, and drops them on
   change. It lists the available products with the loaded ones first. MCP
   gets `search_chart_reference` and `get_chart_reference_entry`, reading
   the service, so they work headless.
3. [#1636](https://github.com/philliphoff/EncDotNet.S100/issues/1636) **R3 Glyphs.** An area fill row (`CatalogueLegendBuilder.Fill` and a
   `LegendGlyphRenderer` case). A fit-to-box render for reference sizes
   (40 px tiles, the 14 px legend sizes are too small). The rule for a
   feature type's lead glyph: its own point symbol, never the default `?`;
   then its fill colours; then its line; else none. An in-memory cache per
   spec, item and palette, rebuilt on `ViewerSettingsService.PaletteChanged`,
   on the palette's chart background (`LegendViewModel.TileOf`).
4. [#1637](https://github.com/philliphoff/EncDotNet.S100/issues/1637) **R4 The activity.** `ChartReferenceViewModel` and `ChartReferenceView`,
   modelled on Feature search (250 ms debounce, synchronous in tests). It
   has the product combo, search, Features / Symbols, and a virtualised list
   grouped by Chart 1 section. S-401 has 926 items, so the list must
   virtualise. A row shows the glyph, the name, the S-57 acronym or code,
   and the most visible display category (D6). Unreached items come last
   under **Not used by a rule** (D3). Selecting a row expands it in place: the
   definition, display category, Chart 1 section, and Drawn with, the
   symbols and their conditions in words. Also **Help › Chart reference**
   and a shortcut, strings in `Strings.ChartReference.cs`, and automation
   ids (§4.6). Tests: view-model tests with a stub service, and a headless
   view test (`ViewHost.Show(view, 320, 800)`).
5. [#1638](https://github.com/philliphoff/EncDotNet.S100/issues/1638) **R5 In view and the Legend link.** The In view toggle, from the display
   lists `CatalogueSymbolKey` already reads. Symbol rows match by name;
   feature rows match by the feature class in the processor's feature list.
   Hover to ring through `LegendFindService`. The Legend tab's **Show all in
   Chart reference** opens the activity at that product's symbols with In
   view on (legends D7).
6. [#1639](https://github.com/philliphoff/EncDotNet.S100/issues/1639) **R6 Study page.** **Open as page** in the full-content panel: a second
   tab registration with `fillsContent: true` that only code opens, sharing
   the view model. It has the tile grid, product chips, and the palette and
   symbol-style preview switches. PDF export waits for later.

### Phase 2: the Reference tab

7. **R7 The tab.** A `TabDock.Right` tab with id `Reference`, order 30 and
   icon `BookInformation` (D1),
   in the existing strip, with its empty state (legends D22).
   `ReferenceNavigator` holds the history: `Open(ReferenceKey)`, back,
   forward and a breadcrumb. There is one entry view per kind: feature
   type, symbol, attribute and display setting. R4's in-place summary moves
   here, and selecting an activity row now opens the tab. Also
   `soundcharts://reference/{spec}/{kind}/{code}` links, and an MCP
   `open_chart_reference`.
8. **R8 Terms and peeks.** A `ReferenceTerm` control: the dotted underline,
   a peek after 500 ms on hover or focus, Space to pin, Escape to close, and
   a click to read. Terms go first in entries and in the Legend tab's
   catalogue key rows (legends D7), then on Display settings' names.

### Phase 3: Object information

9. **R9 Terms in the pick.** The feature type's name and attribute names and
   values come from the feature catalogue and become terms. An S-57 pick
   maps to S-101 entries through the catalogue's S-57 aliases. The raw codes
   move to Copy identity. F1 and **Open in reference** read the feature
   type. The per-attribute eXaminer buttons go; the eXaminer link lives in
   the peek and the entry.
10. **R10 Drawn as.** The picked feature's parts are read from the stack's
    display list by feature reference: point symbols, line styles, fills
    and text. The pick header shows the main part's glyph (a symbol, a line
    sample or an area patch, on the palette's tile) instead of the generic
    `FeatureGlyphs` icon. **Drawn as** lists each part as a term that opens
    its entry, and says plainly when a part is the catalogue's fallback.
    Products that aren't catalogue-portrayed keep `PickLegendKeyLine`.
11. **R11 Why this symbol, from attributes.** The picked feature's
    attributes and the current display settings are matched against the
    drawn symbol's conditions (R1, as put in words for R7 and R8), giving
    the conditions that hold. This is a match, not a trace: it can't see
    computed values, so it says "likely" when more than one case matches.
    It explains every drawn part (D5): the main symbol in full, the
    topmark, light and text one line each. Reasons are folded by default,
    and a setting in a reason links to Display settings. R12 keeps that
    layout.

**R9b Other entry points**, added in planning: the rest of §4.6's entry
points, that is **Look up in reference** on the map's context menu, Feature
search hits, and Layer stack legend rows.

R9, R10 and R9b can run side by side. R11 needs R10's drawn parts.

### Phase 4: exact variants

Tracing and probing always run on the LuaCSharp engine, in their own
session, whichever engine draws the map: both engines run the same rules
over the same features, so the results don't depend on the engine. Rules
written in XSLT keep R11's match.

12. **R12a Trace hook.** An optional line hook on `ILuaContext`, implemented
    for LuaCSharp (`state.SetHook(hook, "l")`) and absent for MoonSharp. It
    must say which rule file each line is in; if the hook can't, run an
    instrumented copy of the rule source instead. Library only.
13. **R12b Rule trace in the pick.** Run the picked feature's rule once with
    the hook. With the line numbers R1 recorded, that gives the exact
    conditions taken, including values the rule works out. Why this symbol
    (R11) drops "Likely" and its note for Lua products and keeps its layout.
14. **R13a Probing.** Run each feature type's rule over the values of the
    attributes it reads, using synthetic features through a custom
    `ILuaDataProviderFactory`, with the trace recording each variant's
    conditions. At most 512 combinations per feature type, taking the
    attributes the rule's conditions test first; numeric attributes are
    sampled either side of the thresholds the rule compares them with and
    of the display settings it reads. A feature type that hits the cap is
    marked partial.
    - `s100 reference probe --spec S-101` writes the results to a file
      committed next to the catalogue (`content/S101/pc/`), keyed by the two
      catalogue hashes. It's run when a bundled catalogue changes, not on
      every build; a test fails when the committed file's hashes don't match.
    - A user-added or changed catalogue misses the committed file and is
      probed in the background, cached on disk under its hashes.
15. **R13b Variant table.** The feature type entry's **How it is drawn**
    becomes the variant table (C1), with the **Simplified / Paper chart**
    switch, the current variant ringed when opened from a pick, and
    "partial" when the probe was capped.
16. **R13c Value columns and before/after pairs.** Attribute entries opened
    from a feature show each listed value's symbol in both styles (C3);
    display setting entries show before/after pairs (C4).
17. **R14 HTML export.** `s100 reference export --spec S-101 --palette
    day --out <dir>` writes a static HTML reference: every entry, with the
    variant tables when the probe file is present. PDF from the study page
    comes later (§5). Built ([#1723](https://github.com/philliphoff/EncDotNet.S100/issues/1723)):
    to share the Reference tab's words and glyphs, `RuleConditionText`,
    `TermText`, `LeadGlyph` and `ChartReferenceArtwork` moved from the viewer
    to `EncDotNet.S100.Datasets.Pipelines.Reference`.

R12a comes first. R12b and R13a can then run side by side, then R13b and
R13c side by side. R14 can follow R13a.
