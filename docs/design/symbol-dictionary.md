# Symbol dictionary — Design Note

> Status: **Proposed.** No issue yet. A working prototype of the study page,
> generated from the catalogues bundled in `EncDotNet.S100.Specifications`,
> is at <https://claude.ai/artifact/Peujh6ZNQhhoJ4MsNAJT1i>. The viewer mocks
> are the **Chart Reference · design pass** artboard on the SoundCharts design
> canvas (<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>).

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
§5's *probing*.

## 3. The library piece

The index belongs in the portrayal toolkit, so the viewer, the MCP server
and the CLI share it:

```csharp
namespace EncDotNet.S100.Portrayals.Dictionary;

public sealed class SymbolDictionary
{
    public static SymbolDictionary Build(FeatureCatalogue fc, PortrayalCatalogue pc);

    public IReadOnlyList<FeatureTypeEntry> FeatureTypes { get; }
    public IReadOnlyList<PortrayalItemEntry> Items { get; }   // symbols, line styles, area fills
    public IEnumerable<DictionaryHit> Search(string query);
}

public sealed record FeatureTypeEntry(
    string Code, string Name, string? S57Acronym, string? Definition,
    IReadOnlyList<string> Primitives, string? ViewingGroup, DisplayCategory? Category,
    string? Chart1Section, IReadOnlyList<FeatureSymbolUse> Uses,
    IReadOnlyList<string> ColorFills);

public sealed record FeatureSymbolUse(string ItemId, IReadOnlyList<string> Conditions, bool ViaSharedRule);

public sealed record PortrayalItemEntry(
    string Id, PortrayalItemKind Kind, string Description, IReadOnlyList<string> UsedBy);
```

Glyphs aren't part of the entry. A caller draws one with a `LegendBuilder`
for the palette it shows, so the dictionary reuses the legend rows
(`SymbolLegendEntry`, `LineLegendEntry`, `SwatchLegendEntry`) and
`LegendGlyphRenderer` instead of adding a third way to draw a symbol.

Code-defined portrayals (weather, tropical cyclones, NDBC, CAP, AIS) already
publish `LegendEntry` rows. They join the dictionary as a **SoundCharts
overlays** group, so a mariner can look up a cold front or a hurricane
symbol the same way.

## 4. The viewer

The mocks are the **Chart Reference · design pass** artboard on the *Chart
display* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>), sections A–G. In the
viewer the dictionary is called **Chart reference**, because it holds
definitions and display settings as well as symbols.

### 4.1 How it fits with the Key

[Per-product legends](https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs)
(round 3) gives each product a Key: a key at the cursor by default (its option
C), and a Key button in the map's overlay column for what is in view (B4c).
The two have different jobs:

- The **Key** answers "what is on this chart?": only what is in view,
  compact, with labels.
- The **reference** answers "what does this mean, and why does it look like
  that?": everything in the loaded catalogues, with definitions, every form
  a symbol can take, and the attributes and settings that choose it.

The Key stays as designed. Each Key row becomes a term that links into the
reference. The legends pass's option D (full keys in the Display controls
tab) moves into the reference's study page (§4.5), so complete keys have one
home.

### 4.2 Four depths, one set of entries

There are four kinds of entry: **feature type**, **symbol** (point symbol,
line style or area fill), **attribute** (with its listed values) and
**display setting** (a portrayal context parameter such as Simplified
symbols). Every way into the reference lands on one of these entries. The
four depths differ only in how much of the entry they show (artboard A):

| Depth | Where | Opens on | Shows |
|---|---|---|---|
| Glance | At the cursor; Object information's Key section | Hover, a pick | The symbol and one line (legends option C) |
| Peek | A popover over any term | Hover or focus after 500 ms; Space pins it | The definition, the glyph, Open in reference |
| Read | A **Reference** tab in the right dock, beside Object information | Clicking a term, F1 on a pick | The whole entry |
| Study | **Chart reference** activity (left dock); **Open as page** (full-content panel) | Activity bar, Help › Chart reference, Ctrl+Shift+K | Every entry of a product |

### 4.3 Object information (artboard B)

Object information today shows a generic Fluent icon per feature type
(`FeatureGlyphs`) and links names to the S-100 eXaminer website, which needs
a connection a ship may not have. The proposal:

- **The drawn symbol** replaces the generic icon: the symbol the renderer
  actually drew for this feature, in the current palette. This needs the
  pick hit to carry the portrayal instructions emitted for the feature
  (symbol, line style or fill names). Areas show a fill swatch, lines a
  short sample.
- **Drawn as** names the symbol, with a folded **Why this symbol**: the
  rule's conditions that held for this feature, in words. For example:
  "Simplified symbols is on · Colour is red · Beacon shape is a stake, pole,
  perch or post". A setting in the reason links to that switch in Display
  settings. When the symbol is the catalogue's fallback (the magenta ? of
  BCNDEF13), say so plainly.
- **Attributes** use the feature catalogue's names, not camel-case codes,
  and every name and value is a term. Codes and S-57 acronyms move into the
  entry and Copy identity.
- The **eXaminer link** stays, in the peek and the entry, but is never the
  only way to a definition.

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
(§5, *Probing*). Until then, C1 falls back to the prototype's list of
symbols with their conditions.

### 4.5 Browse and study (artboards D, E)

**Chart reference** is a left-dock activity, after Feature search. Its list
reads like Chart 1: grouped by section, glyph first. It has a product combo
(loaded products first, then every catalogue the viewer has), search, and
Features / Symbols. **In view** limits it to what is drawn in the current
extent, which makes it the cross-product legend S-98 9.1.5 asks for. It is
on when the activity opens from the map or the Key, and off from the Help
menu. Hovering a row rings that feature type's instances on the chart, and
selecting a row opens its entry in the right dock.

**Open as page** shows the reference in the full-content panel, over the
map, as Settings does (#845). It uses the prototype's layout: a tile grid
by Chart 1 section, a chip per product, Day/Dusk/Night and Simplified/Paper
chart switches, and the entry on the right. **Export key…** writes the
current product and palette as a PDF.

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
  map's context menu (Look up in reference), the Key flyout, Display
  settings (setting names), Layer stack legend rows, Feature search hits,
  the Help menu, and `soundcharts://reference/<spec>/<kind>/<code>` links
  for MCP and docs.
- **Automation ids**: `Reference.Entry`, `Reference.Back`,
  `Reference.Term`, `Reference.Peek`, `ActivityBar.ChartReference`.

## 5. Later

- **Probing.** Run each rule against synthetic features, one per combination
  of the enumerated attributes it reads, through the scripting host the
  pipeline already uses. That gives the exact attribute → symbol table, and
  an example feature for every row.
- **Chart 1 cells.** When the IHO S-101 Chart 1 test cells are loaded, link
  each entry to its place in them and show a rendered crop.
- **MCP tool.** `lookup_chart_symbol` (by code, acronym or description), so
  an assistant can answer "what does this symbol mean?".
- **CLI export.** `encdotnet symbols export --spec S101 --palette day` writes
  a static HTML reference, which is how the prototype was made.
- **Localisation.** Catalogue descriptions carry a language; show the
  mariner's language where a catalogue has it.

## 6. Open questions

1. Is "Chart reference" the name? Mariners say "Chart 1", but that names the
   paper publication and S-101's test cells.
2. Should the right dock grow a Reference tab, or should an entry replace
   Object information's content with a Back to pick? Tabs keep both one click
   apart; replacing keeps one thing on screen at 1280 × 720.
3. How far should Why this symbol go? The main symbol's conditions are
   clear; text, light sectors and topmarks come from other rules.
4. Weather, AIS, NDBC and CAP overlays have legend rows but no feature
   catalogue definitions; they need short written ones to be first-class
   entries.
5. Should the dictionary show every symbol in the catalogue, or only the ones
   a rule can reach? The prototype shows all of them; unreached ones say so.
6. Is S-401 (Inland ENC) in scope for SoundCharts users?
7. The display category comes from the viewing group a rule sets first.
   Some rules set different groups per geometry or per case; showing all of
   them is more correct and harder to read.
