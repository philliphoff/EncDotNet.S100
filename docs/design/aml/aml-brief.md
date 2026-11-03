# Design brief: NATO Additional Military Layers (AML) in SoundCharts

Status: pre-design (2026-11-02). This is input for a design pass. The pass should produce mocks in the SoundCharts design canvas (<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>), on a new **AML · design pass** artboard, with alternatives where this brief asks for them.

Related issues: [#1236](https://github.com/philliphoff/EncDotNet.S100/issues/1236) (recognise AML cells instead of translating them to S-101) and upstream [philliphoff/EncDotNet#42](https://github.com/philliphoff/EncDotNet/issues/42) (AML object model).

The brief has two parts:

- **Part A: indicating AML.** How the viewer shows that a cell is AML, which product and version it is, and its protective marking. #1236 will build this.
- **Part B: drawing AML.** How the AML-only feature types *could* look, if we choose to draw them. Nobody has committed to building this. It would be **our own representation**, because the official AML portrayal is restricted (§1). Treat Part B as exploration: it shows what drawing AML would take and how it would look, so we can decide whether to do it.

## 1. What AML is

Additional Military Layers (AML) is a NATO family of geospatial products, defined in STANAG 7170 and produced mainly by UKHO. Each product is a layer meant to sit **on top of** the navigational chart:

| Product | Content (in plain words) |
|---|---|
| CLB, Contour Line Bathymetry | Depth areas, many more contours than an ENC, soundings. For planning, not navigation. |
| ESB, Environment, Seabed & Beach | Seabed sediment layers, bottom features, scours, beach exits, landing and drop zones, ice. |
| LBO, Large Bottom Objects | Wrecks, obstructions and rocks for anti-submarine and mine-countermeasure work, plus sensor anomalies. |
| SBO, Small Bottom Objects | Contacts smaller than 5 m, including mines and mine-like contacts. |
| MFF, Maritime Foundation & Facilities | Coastline, land, lights, pipelines, cables, platforms and other infrastructure. |
| RAL, Routes, Areas & Limits | Military practice areas, airspace restrictions, exercise airspace, Q-routes, ATS routes, patrol areas. |
| AML 3.0 | One product specification covering all of the above, plus land (LND), nautical-chart background (NCD), flight information (FAI, MFI) and others. |

An AML cell is an S-57 file. It uses the same ISO 8211 records as an ENC, with extra object classes and attributes that have lowercase acronyms. There are three versions in use, 1.0, 2.1 and 3.0, each with its own catalogue.

The **official AML portrayal** is an extension of the S-52 Presentation Library. It is released only to organisations that hold that library, so **we can't use or copy it**. The public AML Implementation Guidance Document does describe its colour convention, and Part B may use that convention:

- environmental: green
- operational: red
- air: blue
- seabed: brown

It also names its symbol sources: S-52 first, then Geosym (MIL-89045A), then MIL-STD-2525C / APP-6(C), then its own.

Sources (all public): the [Admiralty AML page](https://www.admiralty.co.uk/defence/additional-military-layers), which has the product specifications, the Implementation Guidance Document, the Handbook, the catalogues and test data. Local copies are in `~/SynologyDrive/Charts/AML/` on the maintainer's Mac. They are not in the repo.

## 2. What we measured

Measured from 76 public sample cells (UKHO AML 1.0 and the Bundeswehr AML 3.0 trials):

- **Every cell parses** with the current S-57 reader.
- **Identifying a cell is reliable.** The dataset header declares the product:

  | | Product specification | Edition |
  |---|---|---|
  | AML 1.0/2.1 | MFF 51, RAL 52, LBO 53, CLB 54, ESB 55, SBO 56 | 1.0 or 2.1 |
  | AML 3.0 | 57 | 3.0 |

- **Protective marking is in the header.** The 1.0 cells carry text like `title=North Atlantic Ocean - … ; IDO Status = NATO: North Atlantic Treaty Organisation; Protective Marking = UNCLASSIFIED; Owner Authority = UK`. The guidance says systems should display the protective marking prominently when a cell is loaded. It also suggests using the title for catalogue display.
- **Cell names carry information too.** In 1.0/2.1 a name like `NOC6RA3B.000` decodes to:
  - producer nation `NO`
  - product `C` (CLB)
  - scale band `6`
  - classification `R` (Restricted)

  The AML 3.0 sample names are long and descriptive, for example `AMLDEU7U_NKKK4824_CLB_SB7_TrialData.000`.
- **AML has its own scale bands.** Bands 0 (unscaled) to 9 (about 1:1,500). They are not the ENC usage bands.
- **How much of each product is AML-only** (the rest uses standard S-57 classes):

  | Product | Standard S-57 features | AML-only features |
  |---|---|---|
  | CLB | ~240,000 (depth areas and contours) | 94 |
  | MFF | 8,188 | 27 |
  | LBO | 8,095 | 146 |
  | RAL | 1,686 (mostly military practice areas) | 107 |
  | ESB | 101 | 2,687 |
  | SBO | 0 | 96 |
  | AML 3.0 trials | 3,436 | 14,882 |

- **The most common AML-only classes in the samples** are the ones Part B should mock first:

  | Class (acronym) | Product | Count in samples |
  |---|---|---|
  | ATS route centreline (`atsctl`) | RAL | ~6,000 |
  | Air waypoint (`awaypt`, 3.0) | RAL | ~3,900 |
  | Airspace restriction (`airres`) | RAL | ~3,000 |
  | Geological layer (`sedlay`) | ESB | ~2,600 |
  | Controlled airspace (`ctlasp`) | RAL | ~1,200 |
  | Navigation system / NAVAID (`navaid`) | RAL | ~470 |
  | Sensor anomaly (`senanm`) | LBO | ~140 |
  | Military exercise airspace (`mexasp`) | RAL | ~130 |
  | Internal waters area (`intwtr`) | RAL | ~70 |
  | Contact (`contct`) | SBO | ~60 |
  | Bottom feature (`botmft`) | ESB | ~40 |
  | Turning point, Q-route leg (`turnpt`, `qroute`) | RAL | ~70 together |
  | Mine (`mindev`), small bottom object (`smalbo`) | SBO | ~30 together |
  | Trawl scours, impact scours, iceberg, ice lead, ice movement, landing zone, drop zone, beach exit, diving location | ESB, LBO | a few each |

- **Standard classes carry AML attributes.** An LBO obstruction is a seabed contact whose useful details are AML attributes.

## 3. What exists today

| Area | Today | Where |
|---|---|---|
| Opening an AML cell | It is treated as an ENC and translated to S-101. AML-only classes are dropped as unmapped. Standard classes are drawn with S-101 chart portrayal. Nothing says the cell is AML. #1236 changes this to "recognised, not translated". | `S57Dataset.TranslationTargetFor`, `S57DatasetProcessor` |
| Library | Rows with tags (`LibraryTagKind`: Update, Loaded, On pan, Queued, Failed, Expired, Stale, Neutral) and item details with properties. | `LibraryPanelView`, `LibraryItemText` |
| Datasets panel | One header per exchange set (name, dataset count, visibility, signature badge) with dataset rows below it. | `DatasetsView`, `ExchangeSetHeader` |
| Object information | The pick report lists the features under the cursor with their attributes. | `PickReportView` |
| Display controls | The display panel has display combination, category, display planes, overlays (navigational warnings, weather) and the layer stack. | `EcdisDisplayPanelView`, `LayerStackView` |
| Status bar | Depth basis indicator, scale bar, time HUD. | `DepthBasisIndicatorView` |
| Not-for-navigation framing | The footer reads "Not for navigation" where data is marked that way, plus the route-check verdict footers. | `NavigationalWarningsTools`, `PassageCheckTools` |
| Depth answers and route checks | Use the finest covering chart cell for depth (pick depth, route UKC). | `CellScaleRanking`, #938/#1031 |

## 4. Vocabulary to settle

| Term | What the user needs to know | Candidate UI words |
|---|---|---|
| AML | Military overlay data, not a chart | "AML", "Military layer", "Additional Military Layers" |
| Product (CLB, ESB, …) | Which kind of layer | The three-letter code, the full name, or both |
| Version (1.0, 2.1, 3.0) | Which catalogue applies | "AML 3.0" |
| Protective marking | How the data must be handled | "Protective marking", "Classification", "Marking" |
| IDO status, owner authority, caveat | Who owns it and how it may be shared | Shown in details only? |
| Scale band | AML's own display-scale grouping | "Band 6 (1:50,000)" |

## 5. Part A: indicating AML

Mock each of these.

### A1. Library

- An **AML tag** on rows: product and version, for example "AML · CLB" or "AML 3.0". Is it a new `LibraryTagKind` with its own colour, or a Neutral tag?
- The **protective marking** on the row. Always shown, or only when it isn't UNCLASSIFIED? Mock both, with RESTRICTED and NATO RESTRICTED examples.
- **Item details:** an "AML" group with product (code and name), version, title, protective marking, IDO status, owner authority, caveat and scale band. Include what the cell name decodes to, for 1.0/2.1 names.
- **Filters:** "AML" and "by product".
- **Mixed folders**, where AML cells sit next to ENCs of the same area: do they group separately?

### A2. Opening AML (Datasets panel and map)

- The **exchange-set header** for an AML set: an AML badge next to the signature badge, with the marking. If cells in one set have different markings, show the highest one.
- **Dataset rows:**
  - **Recognised, not drawn** (#1236's first step): the row says what the cell is and why nothing is drawn. The "probably broken" look of a failed load must not be used here.
  - **Drawn**, if Part B or option B3 is built: an AML glyph and the product.
- **Map:** the coverage outline (from the meta features) for a recognised AML cell that isn't drawn, so the user can see where the data is. It must look different from the protected-data hatch and the Library coverage overlay.
- **Protective marking while AML is loaded.** The guidance asks for it to be prominent. Mock and compare:
  1. a banner along the top edge of the map
  2. a status-bar item
  3. a corner stamp in the style of the "Display Not Real Time" stamp
  4. the marking in the exchange-set header only

  When several markings are loaded at once, show the highest.
- **Notification on first open:** one per exchange set, for example "This is AML (CLB) data. It isn't drawn. [Details]".

### A3. Object information (once the object model exists)

- Picking an AML feature shows its **AML class name** and its attributes with **catalogue names and decoded enumerations**, not raw codes.
- Mark AML-only attributes on standard classes, for example on an LBO obstruction.
- The pick report header shows the product and the marking.

### A4. CLI and MCP

These aren't visual, but say what `describe_library_item` and the dataset info should report: product, version, title, marking and scale band.

## 6. Part B: drawing AML (our own representation, if built)

### B1. Principles

- **It must look like an overlay, not a chart.** A user should never mistake AML for ENC content. Consider a distinctive line treatment, a fixed AML tint or halo, or an AML glyph on point symbols.
- **Use the public colour families** (§1): environmental green, operational red, air blue, seabed brown. They must be readable in Day, Dusk and Night palettes and must not clash with chart colours. For example, red is already used for danger, so operational red has to stay distinguishable from it.
- **Use public symbol sources only.** That means our own designs, or MIL-STD-2525, which is publicly released. Check APP-6's terms before using it. Nothing from the restricted AML `.dai`.
- **AML must never feed depth answers or route checks.** Even when CLB depth areas are drawn, the pick depth and route UKC must keep using charts. Show this visibly, for example "AML depths aren't used for checks".

### B2. Representative mocks

Mock one example from each colour family, using the most common classes (§2):

- **Air (blue):** ATS route centrelines with air waypoints and NAVAIDs. Airspace restriction, controlled airspace and military exercise airspace as areas. Decide how area boundaries and labels show the vertical limits.
- **Operational (red):** military practice areas (standard MIPARE, but in AML styling), Q-route legs with turning points, patrol areas.
- **Seabed (brown):** small bottom objects, contacts and mines as SBO points, sensor anomalies. LBO wrecks and obstructions with their AML details. Mock dense clusters, since SBO data can be very dense.
- **Environmental (green):** geological layers (sediment) as an area fill with a legend, bottom features, scours, ice features, landing and drop zones, beach exits.
- **CLB bathymetry:** dense contours over an ENC. How do AML contours differ from chart contours, and do we keep the ENC safety contour visually dominant?

For each, show it **over an ENC** at a realistic scale, in Day and Night palettes, and with and without the chart below.

### B3. A middle option to compare

For AML 1.0 CLB, MFF, LBO and RAL, which are mostly standard S-57 classes (§2), the viewer could show them as an S-101 "view as chart" and skip the AML-only classes. Mock this **next to** the B2 approach for the same area, so the difference is visible. It has to be clearly labelled as partial, for example "AML shown as chart · 94 AML features not drawn".

### B4. Display controls

- An **AML** section in the display panel, or a group under Overlays: on/off per product (CLB, ESB, LBO, SBO, MFF, RAL, and the 3.0 types), plus perhaps per colour family.
- **Draw order** against the chart, S-102 and the weather overlays: in the layer stack, where does AML go?
- **Scale:** do AML scale bands drive visibility, or is everything shown at every scale?
- **Legend:** our own symbology needs a legend. Should it be a panel, or a flyout from the AML section?

### B5. Non-goals for the mocks

- Matching the official AML look.
- Gridded AML (AMC, IWC, gridded sediment).
- AML+ / S-501, the S-100 successor, which is still a draft. If it publishes a portrayal catalogue, it replaces anything from Part B.

## 7. Questions for the design pass

1. Does Part A alone, recognising and reporting AML without drawing it, feel complete, or does it read as broken?
2. Where should the protective marking live: once per session, per set, or permanently while loaded?
3. Should the AML tag show the product code, the full name, or a colour family swatch?
4. Is the B3 "view as chart" worth offering, or does it just confuse?
5. If Part B were built, which single product gives the most value for the effort? Our guess is RAL airspace and areas, followed by SBO contacts.

## 8. Deliverables

- Artboard **AML · design pass** with frames A1–A4 and B2–B4. B3 sits next to B2 for comparison.
- A short handoff (decisions, open questions, a frame list) in `docs/design/aml/HANDOFF.md`, plus screenshots, as for the route-checks design rounds.
