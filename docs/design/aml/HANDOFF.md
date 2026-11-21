# NATO Additional Military Layers (AML) — design handoff

Repo: `philliphoff/EncDotNet.S100`. Scope: `src/EncDotNet.S100.Viewer`, plus the CLI and the MCP tools it hosts. Issues: #1236 (Part A, recognise AML instead of translating it) and upstream philliphoff/EncDotNet#42 (AML object model, needed for A3 and Part B).
Round: **1** · Issued: 2026-11-02. Input: [aml-brief.md](aml-brief.md).

Mock: **AML · design pass** artboard in the *SoundCharts S-100 Viewer* design canvas, <https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs> (page *Chart display*, `project/AML-Design-Pass.dc.html`). It sits on *Chart display* beside the Depth Basis Indicator and Per-product Legends passes, because the decisions that matter most (marking on the status bar, map outline, draw order, legend) are chart-display decisions. The Library parts would also fit *Library & data*, but splitting the pass across pages would separate A1 from A2.

The mock is a reference, not code to port. Build it with Avalonia + ShadUI and the theme resources. Cell names follow the public UKHO and Bundeswehr samples. Markings other than UNCLASSIFIED, titles, counts and attribute values are fixtures. Part B symbology is our own (plus shapes in the style of MIL-STD-2525C). Nothing is taken from the restricted official AML portrayal.

## Frames

Screenshots are in the `screenshots/` folder, one per artboard section.

| Frame | Brief | What it shows | Screenshot |
|---|---|---|---|
| **A0** | §4 | Shared pieces: the AML tag (`LibraryTagKind.Military`), the marking chip, vocabulary. Also the answers to §7. | [a0-vocabulary.png](screenshots/a0-vocabulary.png) |
| **A1a** | §5 A1 | Library rows in a mixed folder: AML group, AML filter chip with product menu, marking on rows. | [a1-library.png](screenshots/a1-library.png) |
| **A1b** | §5 A1 | Item details: "not a chart" note, *Additional Military Layers* group, *From the cell name* decode. | ″ |
| **A1c** | §5 A1 | Marking on the row, always vs only above UNCLASSIFIED (with RESTRICTED and NATO RESTRICTED). | ″ |
| **A2a** | §5 A2 | 1100×700 window: AML exchange-set header, recognised-not-drawn rows, coverage outlines with ticks, first-open notification, MARKING pill. | [a2-opening.png](screenshots/a2-opening.png) |
| **A2b** | §5 A2 | The four marking placements compared (banner, status bar, corner stamp, set header) and the highest-wins rule. | ″ |
| **A3a, A3b** | §5 A3 | Object Information for a standard class with AML attributes (LBO obstruction) and an AML-only class (SBO contact). | [a3-object-information.png](screenshots/a3-object-information.png) |
| **A4a, A4b** | §5 A4 | `describe_library_item` / dataset info JSON, and `s100 info` text output. | [a4-cli-mcp.png](screenshots/a4-cli-mcp.png) |
| **B1** | §6 B1 | Overlay treatment: four colour families in Day, Dusk and Night, line signature, rules. | [b1-overlay-treatment.png](screenshots/b1-overlay-treatment.png) |
| **B2a–d** | §6 B2 | One map per family over an ENC: air, operational, seabed (with a dense cluster), environmental. | [b2-b3-drawing.png](screenshots/b2-b3-drawing.png) |
| **B2e + B3** | §6 B2, B3 | CLB contours as an AML overlay, next to the same area as "view as chart". | ″ |
| **B2f** | §6 B2 | Air family in Day, Night and with the chart hidden. | ″ |
| **B4a–c** | §6 B4 | Display-panel section, layer-stack position, legend flyout, scale rule. | [b4-display-controls.png](screenshots/b4-display-controls.png) |

## Answers to §7

1. **Does Part A alone feel complete?** Yes, provided every surface says the same thing in a neutral tone: "Recognised, not drawn". The map outlines where the data is, and the notification says it on first open. It reads as broken only if it borrows the Failed look (red icon, "failed" status), so it mustn't.
2. **Where does the protective marking live?** Permanently while any AML is loaded, as a **MARKING** pill in the status bar showing the highest loaded marking. It also appears on the exchange-set header, on Library rows above UNCLASSIFIED, in item details, in the Object Information provenance strip and in the notification. A per-session reminder isn't enough, because the guidance asks for it to be prominent whenever the data is loaded.
3. **Code, full name or swatch?** The code: "AML · CLB". The full name is in the tooltip and in details. No colour swatch in Part A, because products don't map to one colour family (CLB and LBO are both "seabed"; RAL is air and operational).
4. **Is B3 "view as chart" worth offering?** No. Drawn with S-101 portrayal, CLB depth areas look exactly like the navigational chart and bring a second safety contour (B3 frame). That breaks B1's first principle. The "partial" chip is the only clue, and it's easy to miss.
5. **Most value for the effort if Part B were built?** RAL airspace, ATS routes and areas, then SBO contacts. This agrees with the brief's guess. RAL is the most common AML-only content in the samples. It's all lines and areas with labels, involves no depths, and is what exercise planning needs over a chart. SBO comes second: points only, but it needs clustering (B2c).

## Decisions

### Part A — indicating AML (#1236)

1. **Vocabulary.** "AML" in chips, "Additional Military Layers" in headings and tooltips, "Protective marking" in details, "MARKING" on the status bar. The state is "Recognised, not drawn". Never "Classification" and never "military chart".
2. **AML tag = new `LibraryTagKind.Military`.** Outlined ink (`#52525B` border, `#27272A` text) with a layers glyph, so it can't be read as a state colour or as Neutral. Text: "AML · {code}" for 1.0/2.1; "AML 3.0 · {code}" when a 3.0 cell name carries a product token (`…_CLB_SB7…`), otherwise "AML 3.0". The version is in details for 1.0/2.1. Tooltip: "Additional Military Layers · {product name} · {version}".
3. **Marking chip.** The header's marking text as given, upper case, never shortened. UNCLASSIFIED is outlined and muted; anything above is solid ink (`#27272A`, white text). Never red: a marking is a handling rule, not a fault.
4. **Library rows** show the marking only when it's above UNCLASSIFIED (A1c), placed before the AML tag so it survives a narrow row. All 76 public samples are UNCLASSIFIED; showing it always would bury the one that matters.
5. **Library grouping and filter.** AML cells get their own group header within a source, below the charts. An **AML** chip next to the state chips (All · Local · Online · Updates) opens a product menu (All products, CLB, ESB, LBO, SBO, MFF, RAL, 3.0). It's shown only when the library holds AML. Typing "AML" or a product code in the text filter also matches.
6. **Item details** add an **Additional Military Layers** group: Product (code · name), Version, Protective marking, IDO status, Owner authority, Caveat, Scale band. For 1.0/2.1 names they also add **From the cell name**: the decoded producer, product, band and marking. If the name and the header disagree, the header wins and the row says so. A one-line note at the top: "Military overlay data, not a chart. SoundCharts shows where it is but doesn't draw it."
7. **Exchange-set header.** An **AML** badge in the signature badge's place (both show when a set is signed), followed by the highest marking in the set and "highest of N".
8. **Dataset rows (not drawn).** Three lines: the cell, "AML · {code}, {product name}", and "Recognised, not drawn". The first row in a set adds "Its coverage is outlined on the map." The row icon is a dashed frame in ink, not the failure triangle. The eye toggles the coverage outline.
9. **Map coverage outline.** A 1.4 px ink frame with inward ticks every 9 px. A label card in the top-left corner reads "AML · {code}  {cell}" / "Not drawn · {marking}". No hatch (that means protected data) and no tint (that means Library coverage). It isn't pickable, and it's drawn in *Other chart overlays*.
10. **Marking while loaded = status-bar pill (option 2).** A **MARKING** pill after DEPTHS and before MCP: solid ink with a shield glyph. It's outlined when everything loaded is UNCLASSIFIED and disappears when no AML is loaded. Its flyout lists each loaded marking with its sets. It never truncates; when space is short it drops the "MARKING" label and keeps the marking. The banner is rejected because it costs map space and collides with the time HUD. The corner stamp is rejected because it takes the "Display Not Real Time" slot, and the overlay rule allows only one stamp at a time. Header-only is kept as well as the pill, not instead of it.
11. **Highest wins.** UNCLASSIFIED < RESTRICTED < NATO RESTRICTED < any other text, which is shown as written and ranks highest. A tie between different markings at the top level shows the first plus "+1".
12. **First-open notification.** One per exchange set, Information severity: "{set} is military data" + "{n} Additional Military Layers cells ({codes}). SoundCharts shows where they are but doesn't draw them." + "Marked {marking}." when above UNCLASSIFIED. *Details* opens the Library item. It isn't shown again for that set.
13. **Object Information (needs EncDotNet#42).** Each AML feature starts with a provenance strip: AML tag, cell and marking. This replaces "Spec: …". Class and attribute names come from the catalogue, values are decoded, and the acronym is the tooltip. AML-only attributes on standard classes get a small outlined "AML" mark and sort after the standard ones. AML features are pickable even when not drawn ("Not drawn. Picked from its position."). No Depth card ever comes from AML.
14. **CLI and MCP.** Dataset info and `describe_library_item` get `drawn: false`, `status: "recognised-not-drawn"` and an `aml` object: `product`, `productName`, `productSpecification`, `version`, `title`, `protectiveMarking`, `idoStatus`, `ownerAuthority`, `caveat`, `scaleBand`, plus `fromName` for 1.0/2.1. `list_datasets` and `get_map_state` add `aml.product` and a top-level `highestMarking`. `s100 info` prints "Additional Military Layers, not a chart" first and the marking second.

### Part B — drawing AML (exploration, not committed)

15. **Overlay signature.** Every AML line is dash-dot (14·3·2·3) on a 5 px halo in the palette background (white Day, black Night). Areas have no fill (except sediment) and inward ticks in the family colour. Points sit in a rounded-square frame with a notch at the top right. No chart symbol uses any of these.
16. **Colour families** (Day / Dusk / Night): air `#2350B5` / `#3E66C2` / `#33508F`; operational `#B2381B` / `#B44A2C` / `#8A3B27`; seabed `#7A4B1F` / `#8A5A2E` / `#6B4A2B`; environmental `#2B7547` / `#3A7E52` / `#2E5C40`. Operational leans brick red and is never solid, so it stays apart from the chart's danger magenta and light red. Labels are italic, in the family colour, with a halo.
17. **Symbols.** Air waypoint: framed open triangle. NAVAID: framed hexagon. Turning point: framed ringed dot. Contact: framed open circle. Mine or mine-like: framed filled circle with four spikes, after the MIL-STD-2525C sea-mine shape. Sensor anomaly: framed dashed diamond. Bottom feature: framed chevron. Landing and drop zones: framed L and D. Beach exit: an arrow off the beach. Scours: short parallel strokes. APP-6 isn't used until its terms are checked.
18. **Airspace limits** appear as a stacked fraction under the area name, upper over lower ("FL245 / SFC").
19. **Standard classes keep chart portrayal.** A practice area that's also a chart MIPARE keeps the chart symbol, and AML adds its boundary. LBO wrecks keep the chart symbol and gain a dash-dot ring meaning "AML details on pick".
20. **Dense SBO** clusters within 24 px into a counted bubble. The label names mine-like contacts ("38 contacts · 2 mine-like").
21. **Sediment** is a pattern (sand dots, mud dashes, gravel rings) over a 7 % green wash, never a colour ramp. A small sediment legend is on the map while ESB is drawn.
22. **CLB** contours are 0.7 px seabed brown at 55 % with italic labels and no depth-area fill. The ENC safety contour keeps its weight and gains a halo, so it stays dominant without splitting the chart's draw order.
23. **AML never feeds depth answers or route checks.** A fixed line in the display section and the legend reads "AML depths aren't used for depth answers or route checks." When CLB is drawn, a corner chip reads "AML · CLB not used for depth checks".
24. **Display controls.** An **Additional Military Layers** section in the display panel below Display Planes, shown only when AML is loaded. It has one checkbox per product (with product swatch, name and cell count; loaded products first), **Follow AML scale bands** (on by default), the no-checks note, and a **Legend** link that opens a flyout. There are no per-family or per-class switches yet, and Display Category doesn't apply.
25. **Draw order.** AML gets its own plane, **Additional military layers**: above the chart, S-102 and the weather overlays (*Other chart overlays*), and below *Cautions and warnings*, routes and alerts.
26. **Scale.** AML scale bands drive visibility like the S-57 cell display window (#621): each band shows from its scale down to a few steps smaller, and the finer band wins where bands overlap. Turning the option off shows everything at every scale.
27. **B3 is not offered** (§7 Q4).

## Open questions

- **Marking order beyond NATO RESTRICTED.** The samples only use UNCLASSIFIED (1.0) and the brief's RESTRICTED example. We don't know the full set of strings producers write (national vs NATO, caveats such as "UK EYES ONLY"), so the ranking in decision 11 is provisional. Confirm it against the Implementation Guidance Document before #1236 ships the pill.
- **Where the marking text comes from in 3.0.** The 1.0 cells carry it in the title-like header string. Check where the 3.0 trial cells put it. If it's missing, does the pill fall back to the cell-name letter (U/R)?
- **Coverage for the outline.** Decision 9 assumes meta features (M_COVR) give a usable polygon for every product. Cells without it would need the bounding box. Is that acceptable, or should the outline be hidden?
- **Mixed AML + ENC exchange sets.** Do any exist? If not, the "AML badge on a mixed set" rule in decision 7 can go.
- **Scale-band display window.** We know band 9 is about 1:1,500 and band 6 is used for 1:50,000 work. We need the full band-to-scale table from the product specifications before decision 26 can be implemented.
- **A3's attribute order and units** depend on the EncDotNet#42 object model. For example, does magnetic intensity come with nT from the catalogue?
- **Part B go/no-go.** If Part B goes ahead, start with RAL and then SBO, and check that APP-6 isn't needed. If it doesn't, decisions 15–27 stay on this branch as a record.

## Not done in this round

- Dark theme for Part A (the frames are Light/Day; Night is shown only for B2f).
- An interactive artboard (the frames are static).
- The Dusk palette on the map (the colours are in the B1 table, but no Dusk map is drawn).
