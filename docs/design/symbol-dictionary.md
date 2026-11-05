# Symbol dictionary — Design Note

> Status: **Proposed.** No issue yet. A working prototype, generated from the
> catalogues bundled in `EncDotNet.S100.Specifications`, is at
> <https://claude.ai/artifact/Peujh6ZNQhhoJ4MsNAJT1i>.

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

### Where it lives

- **Help → Chart symbols** opens the dictionary as its own window, so it can
  sit beside the chart.
- **Object information** gets **Show in chart symbols** on each feature, which
  opens the dictionary at that feature type.
- The **Layer stack** legend rows link to their entries.

### Layout

The prototype's layout, which works at window and phone widths:

- **Header**: search, the ECDIS palette (Day, Dusk, Night; defaults to the
  chart's current palette), and one chip per product specification with its
  item count.
- **Toolbar**: browse by *Feature types* or *Symbols*; filter by geometry
  (Point, Line, Area) and display category (Base, Standard, Other); group by
  Chart 1 section, display category, geometry, A–Z or nothing; sort by name,
  code, S-57 acronym or viewing group.
- **Grid**: one tile per entry, with the glyph on the palette's deep-water
  colour (DEPDW), the name, the S-57 acronym or symbol code, and a display
  category badge. Feature types with no symbol of their own sort last in
  their group and have a plain background.
- **Detail pane**: the glyph large and at chart size (S-100 symbols are
  sized in millimetres, so chart size is exact), the definition, the display
  category in plain words, the Chart 1 section and viewing group, then
  **Drawn with**: each symbol with the conditions that select it. A symbol's
  detail lists **Used by** instead. Every row links to its entry.

### Search

Search matches the feature name, code, S-57 acronym and definition, the
symbol codes and descriptions, and the decoded conditions, so "red beacon",
"BCNLAT", "wreck" and "anchorage" all work. Exact code and name matches rank
first.

### In view

A **Shown on the chart** filter limits the dictionary to the feature types
in the current view, which makes it the cross-product legend S-98 9.1.5
asks for. The viewer already knows which features each layer drew.

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

1. Should the dictionary show every symbol in the catalogue, or only the ones
   a rule can reach? The prototype shows all of them; unreached ones say so.
2. Is S-401 (Inland ENC) in scope for SoundCharts users?
3. The display category comes from the viewing group a rule sets first.
   Some rules set different groups per geometry or per case; showing all of
   them is more correct and harder to read.
