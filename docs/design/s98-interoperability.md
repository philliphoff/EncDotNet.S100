# S-98 Interoperability — Design Note (PR-L0)

> Status: **Design / Research** — no production code in this PR. This
> document is the contract that PR-L1 (plumbing) and PR-L2 (rule
> evaluation) implement against. Open questions are collected in
> §8; every claim derived from the spec cites a section number, and
> every claim that could not be verified is tagged `**TBD**`.

## 0. Scope of this note

The viewer currently composes a multi-product display by stacking
each loaded dataset's Mapsui layers in dataset-list order (see
`DatasetLoaderService.FlattenLayerOrder`). That is "Level 0" in
S-98 terms (S-98 Annex A §4.4.1) — pure overlays, no cross-product
reasoning. This note describes the minimum redesign needed to:

1. Make the cross-dataset stacking order **explicit and
   spec-driven** (display planes, drawing priorities).
2. Carry **per-feature plane / priority metadata** out of each
   processor so pick and any future inter-product rule can consume
   it.
3. Land a **first vertical slice** of inter-product rules covering
   the two combinations users will see most often (S-101 + S-102,
   S-101 + S-124).
4. Define the **rule-table shape** that PR-L1 plumbs and PR-L2
   populates from a bundled Interoperability Catalogue.

Everything else (Level 3 hybridization, Level 4 spatial operations,
predefined-combination UI, alerts) is explicitly out of scope and
parked in §8.

---

## 1. Spec reference

### 1.1 Edition used

- **S-98 "IHO S-100 ECDIS and Interoperability Specification",
  Edition 2.0.0, October 2025** (IHO CL 41/2025).
- The publication is a four-document set:
  - `S-98 Main Document` (general ECDIS + portrayal framing).
  - `S-98 Annex A — Interoperability` (Interoperability Catalogue
    model and ECDIS interop levels).
  - `S-98 Part A` (Level 1 Application Schema details).
  - `S-98 Part B` (Level 2 Application Schema details).
- Downloaded as a ZIP from
  `https://iho.int/uploads/user/pubs/standards/S-98/S-98%20Ed%202.0.0.zip`
  during PR-L0 research. **The PDFs are not yet committed to
  `docs/specs/`.** A follow-up PR-L0a should drop them there (and
  in particular Annex A, which carries the bulk of the normative
  rules referenced below).
- Section numbers in this note are written in the form
  `S-98 Annex A §X.Y` or `S-98 Main §X.Y`. Where a section number
  appears with no PDF qualifier, it is from `S-98 Annex A`.

### 1.2 Spec gaps and verification status

- **Source available:** Main, Annex A, Part A, Part B — every
  section cited below was read directly out of the PDF.
- **Source referenced but not loaded:** S-100 Part 16 (the abstract
  interoperability model + XML schema) — Annex A §4.2.1 makes it
  the normative anchor for the Interoperability Catalogue XML
  schema. We have S-100 Ed 5.2.1 in `docs/specs/`. Anything in this
  note that says "per Part 16 schema" is asserted on the strength
  of Annex A's UML diagrams; the actual XSD has **not been
  validated against** during PR-L0 — see §8 TBD-1.
- **Source not available:** S-100 Part 16 5.0.0 XSD release notes;
  the IHO-published *Data Product Interoperability Validation
  Checks* (Edition 1.0.0, February 2025) — listed on the IHO
  page but not retrieved.

Anywhere in this note tagged `**TBD — verify against S-98 §X.Y**`
the reviewer should consult the PDF before PR-L1 finalises the
relevant API.

### 1.3 Other normative references touched by this design

- **S-100 Ed 5.2.1** (`docs/specs/S-100 Ed 5.2.1_FINAL.pdf`):
  - Part 9 §11.6 — display planes (the per-product, two-plane
    UnderRadar / OverRadar concept this codebase already
    implements).
  - Part 9 §10 — drawing instructions and their `drawingPriority`,
    which we already parse for S-101 and the GML/XSLT products.
  - Part 16 — the IC abstract specification (UML + XSD).
- **IMO MSC.530(106)/Rev.1** — the informative nine-tier "priority
  layers" list in S-98 Main §9.2.1 derives from §Appendix 2 of that
  Performance Standard. We adopt it as the default plane ordering
  for products that S-98 v2.0.0 does **not** cover.
- **S-52 Edition 6.1.1** (June 2015) — the legacy S-57 presentation
  library, normatively reused for colour set-aside and viewing
  group concepts (S-98 Main §10.3, §10.5).

### 1.4 Products in scope of S-98 Edition 2.0.0

S-98 Annex A §1.2 / Table 2-1:

| Spec | Edition cited by S-98 |
|---|---|
| S-101 ENC | Ed 1.1.0, April 2023 |
| S-102 Bathymetric Surface | Ed 2.2.0, April 2023 |
| S-104 Water Level | Ed 1.1.0, March 2023 |
| S-111 Surface Currents | Ed 1.2.0, March 2023 |
| S-129 UKC Management | Ed 1.0.0, June 2019 |

**Out of S-98 scope but supported in this codebase** (S-122,
S-124, S-125, S-127, S-128, S-131, S-201, S-411, S-421). Annex
A §4.1.1 says such "other similar products may also be covered
on a case-by-case basis" but the closed dictionary
`urn:mrn:iho:prod:s98:1:1:0:products` enumerates only the five
above. **Our design therefore treats them as Level 0 overlays**
with default-plane assignment derived from MSC.530(106)/Rev.1
priority layers — see §2.3.

---

## 2. Display Plane model

### 2.1 What S-100 / S-98 actually defines

Two **independent** stacking concepts collaborate:

1. **Display Plane** (S-100 Part 9 §11.6; S-98 Main §7.2.9):
   *"Display planes are used to split the output of the portrayal
   functions into mutually exclusive lists. An example of this is
   the separation of chart information drawn under a radar image
   and chart information drawn over a radar image."*
   - S-100 ships only two canonical planes — **UnderRadar** and
     **OverRadar** — and our `DisplayPlane` enum
     (`src/EncDotNet.S100.Core/Pipelines/Vector/VectorPipeline.cs:315`)
     enumerates exactly those.
   - **S-98 widens this:** an Interoperability Catalogue may declare
     additional planes via `S100_IC_DisplayPlane` elements (Annex A
     §A-3.2.1.1, Part A). Per Part A §A-3.2.1.1 *"A feature type may
     be referenced in more than one S100_IC_DisplayPlane, but the
     entries in different display planes must be distinguished by
     different attribute-value combinations or spatial primitives
     so that the actual instances of features are partitioned
     unambiguously between different display planes."*
   - S-98 Main §9.2.2 anchors the radar pivot:
     *"the radar image should be written over Portrayal Catalogue
     display planes with a negative order attribute; and below
     display planes with a positive order attribute."* Our existing
     PC bundles (e.g. `content/S111/pc/portrayal_catalogue.xml`)
     declare `<displayPlane id="OverRadar" order="1">` and
     `<displayPlane id="UnderRadar" order="-1">` — consistent.

2. **Drawing Priority** (S-100 Part 9; S-98 Main §7.2.10):
   *"Display priorities control the order in which the output of
   the portrayal functions is processed by the rendering engine
   within a display plane. Priorities with smaller numerical values
   will be processed first. … If the display priority is equal
   among features, curve features have to be drawn on top of
   surface features whereas point features are drawn on top of
   both."*

The within-plane sort order we implement
(`VectorPipeline.SortByPriority`) is exactly that:
`DisplayPlane → DrawingPriority → Type (area → line → point →
text)`. **That code is correct for intra-dataset rendering and
needs no change.** What's missing is the cross-dataset extension.

### 2.2 The nine "priority layers" (informative)

S-98 Main §9.2.1 lifts the MSC.530(106)/Rev.1 *"priority of
information"* list:

```
1. ECDIS visual alerts/indications (caution, overscale, …)
2. Official data: points/curves and surfaces + official updates
3. Notices to Mariners, manual input and Navigational Warnings
4. Official-caution (ENC and other cautions)
5. Official colour-fill area data
6. Official on-demand data (water levels, currents, UKC, …)
7. Radar and AIS information
8. Mariner's data: points/lines and areas
9. Mariner's colour-fill area data
```

The footnote (S-98 Main §9.2.1, footnote 1) makes this
**informative** — the actual partitioning is what the Portrayal
Catalogues and the IC implement. But it gives us a reasonable
*default* plane mapping for products S-98 v2.0.0 doesn't yet cover.

### 2.3 Our extended plane enum

We add a third dimension to our intra-dataset enum **without
breaking it**, by introducing an outer enum `S98DisplayPlane` that
the cross-dataset layer-stack builder consumes; the per-instruction
`DisplayPlane` enum stays as-is for intra-product portrayal.

Proposed values (default ordering, lowest = drawn first / farthest
back):

| Value | Numeric | Source-of-truth | Maps to which products by default |
|---|---|---|---|
| `BaseChartUnder` | 0 | MSC.530 layer 5 + S-98 Main §9.2.1 layer 5 | S-101 area / colour-fill features; S-57 fallback |
| `Bathymetry` | 10 | S-98 Annex A §8.4.1 / A-6.9.1 ("gridded bathymetry replaces depth area and depth contours") | S-102 |
| `OnDemandSurface` | 20 | MSC.530 layer 6, S-98 Main §9.2.1 | S-104, S-111 (the colour-band coverage) |
| `BaseChartOver` | 30 | MSC.530 layer 2 + S-98 §9.2.1 layer 2 | S-101 curves/points/text |
| `OtherChartOverlays` | 40 | MSC.530 layer 6 (catch-all) | S-122 (MPAs), S-125 / S-201 (AtoN), S-127, S-128, S-131, S-411, S-421, S-129 UKC areas |
| `CautionsAndWarnings` | 50 | MSC.530 layer 3-4 + S-98 Main §9.2.1 layers 3-4 | S-124 navigational warnings |
| `DynamicArrows` | 60 | S-111 PC `OverRadar` overlay (`displayPlane id="OverRadar"`) | S-111 arrow sublayer; S-104 trend glyphs (PR-J) |
| `MarinerOverlay` | 70 | MSC.530 layers 8-9 | Mariner's own annotations (future) |
| `EcdisAlerts` | 80 | MSC.530 layer 1, S-98 Main §9.2.1 layer 1 | ECDIS overscale, caution, AIO (future) |

Notes:

- **The order is informative**, and reviewers should treat each
  numeric gap (10) as expansion room for IC-declared planes
  (`S100_IC_DisplayPlane`). The actual numbers are not normative —
  S-98 IC entries supply their own ordering and override these
  defaults.
- The intra-product `DisplayPlane` (UnderRadar / OverRadar) still
  applies *within* a plane. So an S-111 dataset emits two
  sub-layers; the colour-band one lands on `OnDemandSurface`,
  the arrow overlay on `DynamicArrows`. **The processor is
  responsible for this mapping** (see §4.2).
- Where the IC carries a `S100_IC_DisplayPlane` with a non-default
  order, the IC overrides the defaults above. **The defaults are
  the "Level 0 fallback" path required by Annex A §4.4.1.**

### 2.4 Within-plane priority — already exists

S-101 emits `<drawingPriority>` integers per S-100 Part 9 §10. Our
`Part9DisplayListReader` extracts them
(`DrawingInstruction.DrawingPriority`). Coverage products do not
emit drawing instructions and therefore don't carry an explicit
priority. For coverage layers, we adopt: **a single integer per
sub-layer**, defaulted at the processor (S-102 → 0, S-104 colour
band → 0, S-111 colour band → 0, S-111 arrow → 10). The IC may
override these via `S100_IC_DrawingInstruction.drawingPriority`.

For other GML products (S-124, S-125, …) the XSLT pipelines already
emit `<drawingPriority>` per drawing instruction via their per-rule
`with-param`s (see e.g. `S111/pc/Rules/SurfaceCurrent.xsl`). So
intra-product priority is already first-class for every vector
product.

---

## 3. Inter-product rules in v1

S-98 Annex A is structured around four progressively more powerful
*Levels* (§4.4):

| Level | Operations | Status in S-98 v2.0.0 |
|---|---|---|
| 0 — Overlays | None; pass-through. | Fully specified (Annex A §4.4.1). |
| 1 — Interleaving | Cross-product plane / drawing-priority reassignment. | Fully specified (Part A). |
| 2 — Type selectivity + class replacement | Suppress all instances of a feature type in product A when product B is loaded; substitute drawing instructions; predefined combinations. | Fully specified (Part B). |
| 3 — Feature hybridization | Per-instance suppression and attribute merging. | Annex A §4.4.4 notes *"Support for this level is not fully elaborated in this version of the Interoperability Catalogue Specification and it should therefore not be implemented in Interoperability Catalogues created from this Specification"*. |
| 4 — Spatial operations | Spatial queries + geometry combination. | Annex A §4.4.5 notes *"Support for this level is not fully elaborated in this version of the Interoperability Catalogue Annex"*. |

**Our v1 commits to Levels 0 and 1, with a single Level 2 rule
piloted for the most-visible S-101 / S-102 case.** Levels 3 and 4
are deferred (§3.5) and gated on the IHO finishing the spec.

### 3.1 Rule R-101-102-A: S-102 supersedes S-101 depth-area shading

- **S-98 cite:** Annex A §A-6.9.1 *"High definition gridded
  bathymetry replaces (overwrites) depth area and depth contours,
  but soundings, aids to navigation, and obstructions are over the
  high definition bathymetry (interoperability Level 1)."*
- **Level:** Level 1 (plane reassignment); a Level 2 variant
  *"suppress all S-101 `DepthArea` and `DepthContour` features when
  an S-102 dataset is loaded covering the same area"* is the
  natural follow-up — Annex A §8.1.1 *"duplicated features, same
  model"* covers it.
- **Semantics:** when an S-101 dataset *and* an S-102 dataset are
  both loaded and their coverage overlaps, the S-102 coverage
  layer is drawn on plane `Bathymetry` (between `BaseChartUnder`
  and `BaseChartOver`). S-101 line/point/symbol layers (the
  `BaseChartOver` plane) remain on top of S-102. **At Level 1 we
  do not suppress** the underlying S-101 depth areas — they merely
  end up obscured under the (typically opaque) S-102 colour shade.
- **Codebase mapping:**
  - S-101 processor declares its layers as `BaseChartUnder`
    (areas/fills) **and** `BaseChartOver` (lines, points, text) —
    today these collapse into a single Mapsui layer; PR-L1 must
    split them along the `DrawingPriority`'s area-or-not boundary,
    or carry the split through `DrawingInstruction.TypeSortOrder`.
    See §4.1.
  - S-102 processor declares its single coverage layer as
    `Bathymetry`.
  - `LayerStackBuilder` orders by `(plane, withinPlanePriority,
    datasetOrder)` — the S-102 layer lands between the two S-101
    layers.
- **Level 2 extension (R-101-102-B):** when both products are
  loaded, suppress every S-101 feature whose code is in
  `{DepthArea, DepthContour}`. Implemented as
  `S100_IC_SuppressedFeatureLayer` inside an
  `S100_IC_PredefinedCombination` keyed on the product set
  `{S-101, S-102}` (Annex A §8.4.1 *"skin-of-the-earth feature
  replacement"* + Part B §B-3.1.2).

### 3.2 Rule R-101-124-A: S-124 warnings render above all ENC base data

- **S-98 cite:** Annex A §4.4.1 (Level 0) + MSC.530(106)/Rev.1
  priority layer 3 *(via S-98 Main §9.2.1)* — *"Notices to
  Mariners, manual input and Navigational Warnings"* sit above
  *"Official-data: Points/Curves and Surfaces"*. **TBD — verify
  against S-98 §X.Y for an explicit Annex A rule (S-124 is not in
  Annex A Table 1-1, so this rule is currently a viewer-side
  derivation from MSC.530, not an IC clause).**
- **Level:** Level 0 only — S-124 is outside the IC's product
  list (Annex A §4.1.1 / closed dictionary
  `urn:mrn:iho:prod:s98:1:1:0:products`), so it stays an overlay.
  We treat the rule as a **default-plane assignment**, not an IC
  rule.
- **Semantics:** S-124 features render on plane
  `CautionsAndWarnings` (50). They appear above every base-chart
  and on-demand-coverage plane, and below mariner data + ECDIS
  alerts.
- **Codebase mapping:**
  - `S124DatasetProcessor.Render` declares its single emitted
    layer's plane as `CautionsAndWarnings`.
  - No IC involvement. The rule is the default plane assignment.

### 3.3 Rule R-104-A / R-111-A: Water level & current overlays sit above bathymetry, below ENC line work

- **S-98 cite:** Annex A §A-6.9.1 *"Gridded data will generally
  go over ENC and obscure ENC features, either all
  (interoperability Level 0) or specific features
  (interoperability Level 1) depending on interoperability Level
  chosen…"* — and S-98 Main §9.2.1 layer 6 *"Official on demand
  data (for example, water levels, surface currents, under keel
  clearance)"*.
- **Level:** Level 1.
- **Semantics:**
  - S-104 colour band → plane `OnDemandSurface` (20).
  - S-111 colour band → plane `OnDemandSurface` (20). S-111 arrows
    → plane `DynamicArrows` (60).
  - S-101 line/point work on `BaseChartOver` (30) sits *above* the
    on-demand surface but *below* the dynamic arrows. **Verify
    intent of this stacking against Annex A §A-6.9.1 — TBD-2.** It
    is not entirely clear that S-101 line work must come above an
    S-104 surface; some implementations render the surface on top
    with alpha. The current proposal follows the *"obscure ENC
    features"* reading literally: the surface goes under the base
    chart's vector work.
- **Codebase mapping:** S-104 and S-111 processors already split
  into multiple sub-layers (`DatasetResult.Layers` +
  `LayerNames`); the processor sets a per-sub-layer plane.

### 3.4 Summary table for v1

| Rule | Level | Products | Operation | S-98 cite |
|---|---|---|---|---|
| R-101-102-A | 1 | S-101, S-102 | S-102 coverage on plane `Bathymetry`, between S-101 areas and S-101 line work | Annex A §A-6.9.1 |
| R-101-102-B | 2 | S-101, S-102 | Suppress S-101 `DepthArea`, `DepthContour` when S-102 covers same area | Annex A §8.4.1 + Part B §B-3.1.2 |
| R-101-124-A | 0 | S-101, S-124 | S-124 warnings on plane `CautionsAndWarnings` | S-98 Main §9.2.1 + MSC.530(106)/Rev.1 §Appendix 2 (S-124 not in IC) |
| R-104-A | 1 | S-101, S-104 | S-104 colour band on `OnDemandSurface`, below S-101 line work | Annex A §A-6.9.1 + S-98 Main §9.2.1 layer 6 |
| R-111-A | 1 | S-101, S-111 | S-111 colour band on `OnDemandSurface`; arrows on `DynamicArrows` | Annex A §A-6.9.1 + existing PC `displayPlane id="OverRadar"` |

That's **five proposed v1 rules** spanning four product pairs.
Three of them (R-101-102-A, R-104-A, R-111-A) are pure default
plane assignments and need no IC payload at all; PR-L1 ships them.
R-101-102-B and the dynamic IC-driven overrides need PR-L2.

### 3.5 Deferred — explicit non-goals for v1

| Rule family | Why deferred |
|---|---|
| Predefined Combinations UI (`S100_IC_PredefinedCombination`) | Annex A §11 / Part B §B-3.1.2 — large UX surface; needs a layer-controls panel (gated on PR-L3). |
| Hybrid Feature / Portrayal Catalogues | Annex A §4.4.4 — *"should not be implemented in Interoperability Catalogues created from this Specification"*. |
| Spatial operations (Level 4) | Annex A §4.4.5 — *"Support for this level is not fully elaborated"*. |
| Skin-of-the-earth adjusting (geometry edits) | Annex A §8.4.2 — Level 3/4 only. |
| Safety-contour synthesis from S-102 + S-104 | Annex A §A-6.9.1 NOTE — *"OEMs are permitted to add additional safety contour functions"* — explicitly optional; significant new pipeline. |
| Pre-defined Combination switching UX | Annex A §15.4. Tied to layer-controls UI PR-L3. |
| Alerts triggered by interop result | Annex A §7.3 (Main) + Annex A §11.7. Needs alert engine. |
| Colour set-aside arbitration | S-98 Main §10.3 + Part 16 — needs palette-aware rule input. |

---

## 4. Codebase impact map

### 4.1 `src/EncDotNet.S100.Core/`

Additions (new types — no behaviour yet, PR-L1 wires them up):

- `EncDotNet.S100.Interoperability.S98DisplayPlane` (enum) — the
  values from §2.3.
- `EncDotNet.S100.Interoperability.LayerStackEntry` (record):
  ```
  public sealed record LayerStackEntry(
      Mapsui.Layers.ILayer Layer,
      S98DisplayPlane Plane,
      int WithinPlanePriority,
      DatasetEntry SourceDataset,
      string? SourceSubLayerName,
      string? SourceFeatureType);    // null for whole-layer entries
  ```
- `EncDotNet.S100.Interoperability.IInteroperabilityAuthority`
  (interface):
  ```
  public interface IInteroperabilityAuthority
  {
      IReadOnlyList<LayerStackEntry> Apply(IReadOnlyList<LayerStackEntry> raw);
  }
  ```
  The authority is pure — `(in) → (out)` — so it can be unit-tested
  without the viewer. PR-L1 ships the default
  `Level0Authority : IInteroperabilityAuthority` that just sorts
  by `(Plane, WithinPlanePriority, DatasetOrder)`. PR-L2 layers a
  catalogue-driven implementation on top.

The existing `DisplayPlane` enum (UnderRadar / OverRadar) in
`Core.Pipelines.Vector.VectorPipeline.cs:315` **does not change**
— it remains the intra-product portrayal concept. The two enums
exist side-by-side. Doc the relationship in the new
`S98DisplayPlane`'s XML summary.

### 4.2 `src/EncDotNet.S100.Datasets.Pipelines/`

`DatasetResult` (in `DatasetPipelineFactory.cs:16`) grows an
optional sibling to the existing `LayerNames`:

```
public IReadOnlyList<S98DisplayPlane>? LayerPlanes { get; init; }
public IReadOnlyList<int>? LayerWithinPlanePriorities { get; init; }
```

Both must be the same length as `Layers` when supplied. Null
defaults to `(BaseChartOver, 0)` for vector products and
`(OnDemandSurface, 0)` for coverage products — chosen by the
factory based on `Spec.Name` so that every existing processor
remains source-compatible until it opts into plane assignment.

Per-processor plane defaults (PR-L1 fills these in):

| Processor | Layer(s) | Plane | Within-plane priority |
|---|---|---|---|
| `S101DatasetProcessor` | (today) single vector | `BaseChartOver` | inherits from `DrawingPriority` ordering already inside the layer |
| `S101DatasetProcessor` *(post-split, see below)* | areas | `BaseChartUnder` | 0 |
| `S101DatasetProcessor` *(post-split)* | lines + points + text | `BaseChartOver` | 0 |
| `S102DatasetProcessor` | coverage | `Bathymetry` | 0 |
| `S104DatasetProcessor` | colour band | `OnDemandSurface` | 0 |
| `S111DatasetProcessor` | colour band | `OnDemandSurface` | 0 |
| `S111DatasetProcessor` | arrow overlay | `DynamicArrows` | 10 |
| `S122DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S124DatasetProcessor` | vector | `CautionsAndWarnings` | 0 |
| `S125DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S127DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S128DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S129DatasetProcessor` | vector | `OtherChartOverlays` | 0 (S-129 is in the IC; PR-L2 may move it) |
| `S131DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S201DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S411DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S421DatasetProcessor` | vector | `OtherChartOverlays` | 0 |
| `S57DatasetProcessor` | vector | `BaseChartOver` | 0 |

#### 4.2.1 The S-101 split problem

Today `S101DatasetProcessor.Render`
(`src/EncDotNet.S100.Datasets.Pipelines/S101DatasetProcessor.cs:83`)
produces a **single** Mapsui vector layer that internally is sorted
by `(DisplayPlane, DrawingPriority, Type)` via
`VectorPipeline.SortByPriority`. To honour R-101-102-A
(S-102 between S-101 areas and lines), we must split it.

Two options are open for PR-L1:

- **Option A — split inside the renderer.** Have
  `MapsuiDisplayListRenderer.Render` return *two* `ILayer`s
  (areas, then "lines+points+text"). Cleanest, but the renderer
  becomes coupled to the S-98 partition.
- **Option B — split in the processor.** Run the pipeline twice
  with a pre-filter that selects only area-typed
  `DrawingInstruction`s the first time and the rest the second
  time. Doubles symbol-cache reuse cost but isolates the change.

**Recommendation: Option B.** It keeps the renderer ignorant of
S-98 and is fully reverted by toggling the split off. PR-L1
implements it. Cost is one extra style sort + render pass per
S-101 dataset; benchmarks expected to be < 5% per Mapsui's measured
inner-loop costs.

The split applies only to S-101 (and S-57). Coverage products and
GML overlays do not have a fill-vs-linework split.

### 4.3 `src/EncDotNet.S100.Viewer/`

#### 4.3.1 Where cross-dataset assembly lives today

`DatasetLoaderService.FlattenLayerOrder`
(`src/EncDotNet.S100.Viewer/Services/DatasetLoaderService.cs:473`)
is the *only* place layers from different datasets are combined.
It walks `_entryOrder` (user-controlled dataset stacking from the
Datasets panel) bottom-up, concatenates each entry's
`_entryLayers[entry]` in dataset-iteration order, and hands the
result to `MapsuiMapHost.ReorderDatasetLayers`.

That means today every layer of dataset N sits below every layer
of dataset N+1, regardless of plane. This is **Level 0** in S-98
terms but with a *user-defined* stacking criterion — closer to
"naive overlay" than the §9.2.1 priority-layer list.

#### 4.3.2 Where it moves to

PR-L1 inserts a `LayerStackBuilder` between the per-entry render
result and the map host:

```
            ┌───────────────────────────────────┐
            │ DatasetLoaderService              │
            │   _entryLayers : entry → ILayer[] │
            │   _entryPlanes : entry → plane[]  │  (NEW)
            └────────────┬──────────────────────┘
                         │
                         ▼
            ┌───────────────────────────────────┐
            │ LayerStackBuilder                 │
            │   raw : LayerStackEntry[]         │
            │   authority.Apply(raw) → ordered  │
            └────────────┬──────────────────────┘
                         │
                         ▼
            ┌───────────────────────────────────┐
            │ MapsuiMapHost.ReorderDatasetLayers│
            └───────────────────────────────────┘
```

The builder is owned by `DatasetLoaderService` (no new singleton).
`IInteroperabilityAuthority` is injected; the default Level-0
implementation is registered in
`Program.cs` / DI bootstrapping. PR-L2 swaps the implementation
without touching the builder.

The user's Datasets-panel ordering remains the *tie-breaker*
inside a plane — implemented by passing `entryOrderIndex` as the
last sort key inside the authority. **No regression to the existing
"drag-to-reorder" UX.** This must be tested with a fixture in
`tests/` that loads two S-101 datasets and confirms drag-reordering
still works.

#### 4.3.3 `PickService` consumption

`PickService.ResolveHits`
(`src/EncDotNet.S100.Viewer/Services/PickService.cs:135`) walks
`mapInfo.MapInfoRecords` in Mapsui's hit-test order. PR-L1 keeps
that contract but adds a stack-order sort key:

```
hits.OrderByDescending(h => layerStackOrder[h.Layer]).ThenBy(h => …)
```

…where `layerStackOrder` is the dictionary produced by the builder.
That delivers §6 ("pick should be stack-ordered, topmost first") in
PR-L1 without waiting on PR-L2. Mapsui still does the geometric
hit test; we only reorder the dedup-survivors.

#### 4.3.4 Other viewer files touched in PR-L1

- `ViewerDatasetCatalog.cs` — none expected.
- `MainViewModel.cs` — none expected (it never assembles layers).
- `MapsuiMapHost.cs` (i.e. `IMapHost.ReorderDatasetLayers`) — none;
  it already takes a flat ordered list.
- `DatasetsViewModel.cs` — no changes; user-perceived ordering
  still flows through this VM.

### 4.4 `src/EncDotNet.S100.Specifications/content/S98/`

S-98 distributes the Interoperability Catalogue as an XML document
conforming to the S-100 Part 16 schema (Annex A §4.2.1). When IHO
publishes a normative IC we follow the bundled-spec convention
already in use for S-127 / S-131 / S-411:

```
src/EncDotNet.S100.Specifications/
  content/
    S98/
      catalogue/
        S98-Interoperability-Catalogue-1.0.0.xml      (eventual normative)
        S98-Interoperability-Catalogue-1.0.0.xsd
      Adapter/
        defaults.xml                                  (our PR-L1 default
                                                       plane assignments
                                                       in IC-shaped XML
                                                       so the same loader
                                                       can read both)
      README.md
```

`defaults.xml` is **our** content (not byte-identical to upstream) —
it encodes the default plane table from §2.3 as a Level-1 IC so the
PR-L2 loader works against a single shape. When the IHO publishes
the normative IC, `catalogue/` is dropped in byte-for-byte and
`Adapter/defaults.xml` becomes the fallback when no normative IC
covers a particular product pair (e.g. for S-122 / S-124 /
non-Annex-A products).

`EncDotNet.S100.Specifications.Specification` gains:

```
public static IInteroperabilityCatalogueSource CreateInteroperabilityCatalogueSource();
```

PR-L1 publishes the type; PR-L2 wires it into the authority.

### 4.5 `src/EncDotNet.S100.Renderers.Mapsui/`

No PR-L1 changes if we adopt Option B (§4.2.1). If we ever adopt
Option A, `MapsuiDisplayListRenderer.Render` returns
`IReadOnlyList<ILayer>` and the area/line+text split moves here.

### 4.6 Tests

New project (no existing one matches):

```
tests/EncDotNet.S100.Interoperability.Tests/
  LayerStackBuilderTests.cs
  Level0AuthorityTests.cs
  CatalogueLoaderTests.cs              (PR-L2)
  PredefinedCombinationTests.cs        (PR-L2)
```

Existing pipeline tests under `tests/EncDotNet.S100.Pipelines.Tests/`
need new cases:

- `S101DatasetProcessor` emits **two** layers with correct planes
  when Option B split is on.
- `S102DatasetProcessor` declares its coverage layer's plane as
  `Bathymetry`.
- `S111DatasetProcessor` declares both sub-layer planes correctly.

---

## 5. The rule-table shape

### 5.1 Wire format — XML, IC-shaped, mirroring Part 16

The catalogue must be XML conforming to the S-100 Part 16 IC
schema (Annex A §4.2.1, §A-3.2.1.1, §B-3.2.1). **TBD-1: we have
not yet validated against the real Part 16 XSD; PR-L1 should drop
the IHO XSD into `content/S98/catalogue/` and validate during
build.** The element names below are taken from Annex A — they
*should* match the XSD's element names but the exact case /
qualification is unconfirmed.

Example Level-1 IC fragment (our `Adapter/defaults.xml`):

```xml
<S100_IC_InteroperabilityCatalogue
    xmlns="http://www.iho.int/s100ic/1.0"
    interoperabilityLevel="1">

  <productCovered>S-101</productCovered>
  <productCovered>S-102</productCovered>
  <productCovered>S-104</productCovered>
  <productCovered>S-111</productCovered>
  <productCovered>S-129</productCovered>

  <S100_IC_DisplayPlane id="BaseChartUnder" order="0"
                        interoperabilityLevel="1">
    <S100_IC_Feature product="S-101" featureCode="DepthArea"/>
    <S100_IC_Feature product="S-101" featureCode="LandArea"/>
    <S100_IC_Feature product="S-101" featureCode="SeaArea"/>
    <!-- … -->
  </S100_IC_DisplayPlane>

  <S100_IC_DisplayPlane id="Bathymetry" order="10"
                        interoperabilityLevel="1">
    <S100_IC_Feature product="S-102" featureCode="BathymetryCoverage"/>
  </S100_IC_DisplayPlane>

  <S100_IC_DisplayPlane id="BaseChartOver" order="30"
                        interoperabilityLevel="1">
    <S100_IC_Feature product="S-101" featureCode="*"/>
    <!-- everything not bound elsewhere -->
  </S100_IC_DisplayPlane>

  <!-- … -->
</S100_IC_InteroperabilityCatalogue>
```

Level-2 additions (e.g. for R-101-102-B) introduce
`S100_IC_PredefinedCombination` + `S100_IC_SuppressedFeatureLayer`
(Annex A §B-3.1.2, §B-3.1.4):

```xml
<S100_IC_PredefinedCombination
    id="S101+S102-bathymetry"
    interoperabilityLevel="2">
  <productSet>S-101,S-102</productSet>
  <S100_IC_SuppressedFeatureLayer product="S-101" featureCode="DepthArea"/>
  <S100_IC_SuppressedFeatureLayer product="S-101" featureCode="DepthContour"/>
</S100_IC_PredefinedCombination>
```

### 5.2 In-memory model

```
public sealed record S98Catalogue(
    int InteroperabilityLevel,
    IReadOnlyList<string> ProductsCovered,
    IReadOnlyList<S98DisplayPlaneEntry> DisplayPlanes,
    IReadOnlyList<S98PredefinedCombination> PredefinedCombinations);

public sealed record S98DisplayPlaneEntry(
    string Id,
    int Order,
    int InteroperabilityLevel,
    IReadOnlyList<S98FeatureBinding> Features);

public sealed record S98FeatureBinding(
    string Product,
    string FeatureCode,           // "*" or a Feature Catalogue code
    string? Filter,               // S-98 §4.3 filter expression, or null
    int? OverrideDrawingPriority);

public sealed record S98PredefinedCombination(
    string Id,
    int InteroperabilityLevel,
    IReadOnlyList<string> ProductSet,
    IReadOnlyList<S98SuppressedFeatureLayer> Suppressed);

public sealed record S98SuppressedFeatureLayer(
    string Product,
    string FeatureCode,
    string? Filter);
```

The XML loader (`S98CatalogueLoader.LoadAsync`) returns
`S98Catalogue`. The authority consumes it.

### 5.3 Evaluation contract

**Pure function**, as called out in §4.1:

```
IReadOnlyList<LayerStackEntry> Apply(
    IReadOnlyList<LayerStackEntry> raw);
```

The default `CatalogueDrivenAuthority` algorithm:

1. For each entry in `raw`, find the first `S98DisplayPlaneEntry`
   whose `Features` matches the entry's
   `(SourceProcessor.Spec.Name, SourceFeatureType)`. The match
   semantics:
   - exact product + code, then product + `*`, then no match (fall
     through to the entry's `Plane` default from the processor).
   - **filters are not evaluated** in v1 because we operate at the
     layer granularity, not the per-feature level (Annex A §4.3
     filters need Level 3+ to act per-instance).
2. Override `entry.Plane` with the matched plane's `Order`-derived
   `S98DisplayPlane`. (`Order → S98DisplayPlane` is a fixed map for
   the canonical planes; non-canonical IC-declared planes get
   slotted into the numeric gap closest to their `order`.)
3. Apply any per-`S100_IC_DrawingInstruction.drawingPriority`
   override (none in v1).
4. For each `S98PredefinedCombination` whose `ProductSet` is a
   subset of the currently-loaded products, drop every entry whose
   `(Product, FeatureCode)` matches a `Suppressed` row.
5. Sort by `(Plane.Order, WithinPlanePriority, DatasetOrderIndex)`.

This algorithm is pure and trivially unit-testable.

### 5.4 Where the catalogue lives

- **Bundled** in `EncDotNet.S100.Specifications`'s embedded
  resources (same pattern as `S127/pc/` etc.).
- **Loaded once** at startup by
  `Specification.CreateInteroperabilityCatalogueSource()`.
- **Selected** by interop-level: the user picks Level 0 / 1 / 2 in
  the layer-controls UI (PR-L3). PR-L1 hard-wires Level 1.

---

## 6. Pick semantics

### 6.1 Today

`PickService.ResolveHits` enumerates `mapInfo.MapInfoRecords` in
Mapsui's order (which is *roughly* topmost-first within a single
hit, but unspecified across layers), dedupes by
`(processor, featureRef)`, and presents the survivors to the panel.
Status text follows `hits[0]`, with a *"+N more"* suffix.

### 6.2 Proposal

S-98 Annex A §A-6.12 (Part A) and §10.12 (Main) note **"The Pick
Report functionality specification in S-98 is still under
development."** So pick stack ordering is **viewer-side
derivation**, not a normative cite.

We adopt the natural rule: **pick is ordered by the same stack
the renderer uses**, top-of-stack first.

Concretely in `PickService.ResolveHits`:

1. Compute `stackOrder : ILayer → int` once per
   `IDatasetLoaderService.EntryLayers` change (cached in
   `LayerStackBuilder`).
2. Sort `MapInfoRecords` by descending `stackOrder[record.Layer]`
   *before* the existing dedup walk.
3. Existing dedup-by-`(processor, featureRef)` then keeps the
   topmost hit per logical feature (since the topmost record
   wins by being first into `seen.Add`).

Status-text logic is unchanged — `hits[0]` is now guaranteed to be
the topmost hit, which matches user expectation
("the pick panel describes what I clicked on, not what was hidden
underneath").

Coverage pick (`TryCoveragePick`) is unaffected — it triggers only
when *no* vector hit existed at all.

### 6.3 Future: Pick filtered by IC

Annex A §A-6.12 hints at future "pick report" features tied to the
IC. Out of scope for v1 — once the spec lands, we can add a
`SourceFeatureType`-aware filter in the same place.

---

## 7. Visual regression and rollout plan

### 7.1 Snapshot impact

Re-ordering layers will change **most rendered snapshots**.
Specifically:

- Any test that loads an S-101 + S-102 pair (RenderS102 outputs)
  will change as S-102 moves between S-101 area fills and S-101
  line work.
- S-104 / S-111 + S-101 snapshots will shift if any S-101 line
  work was previously below an on-demand surface.
- Single-product snapshots **should not change**, because the
  intra-product sort
  (`VectorPipeline.SortByPriority`) is unchanged.

### 7.2 Rebaseline approach

1. PR-L1 lands the plumbing but **defaults the authority to
   `Level0Authority`** (no plane reordering) so behaviour is
   identical to today. Snapshots do not change.
2. PR-L1.5 (or PR-L2 opener) flips the default to
   `CatalogueDrivenAuthority(Level=1)` and explicitly rebaselines
   the affected snapshots. Each rebaselined image gets a short
   commit-message note ("S-98 R-101-102-A: S-102 moves between
   S-101 areas and S-101 line work").
3. The PR includes a **before/after collage** image bundle so
   reviewers can diff visually.
4. Anything that's *not* one of the five v1 rules should produce
   identical output. If a snapshot changes that we did not expect,
   the change needs a clause cite or a TBD entry — no silent
   visual shifts.

### 7.3 PR sequence

| PR | Scope | Files-changed budget |
|---|---|---|
| **PR-L0** (this) | Design note only. | `docs/design/s98-interoperability.md` + `docs/toc.yml` |
| **PR-L1** plumbing | `S98DisplayPlane` enum, `LayerStackEntry`, `IInteroperabilityAuthority`, `Level0Authority`, processor plane defaults, `LayerStackBuilder`, S-101 area/line split, pick stack-ordering, tests. **No** snapshot rebaseline (authority is L0). | ~25 files; new project `EncDotNet.S100.Interoperability` likely warranted |
| **PR-L1.5** | Flip default authority to L1 catalogue-driven; load `Adapter/defaults.xml`; rebaseline snapshots. | ~5 src files + ~20 snapshot files |
| **PR-L2** rules | `S98CatalogueLoader`, `CatalogueDrivenAuthority`, R-101-102-B suppression rule, predefined-combination plumbing, tests. Still no UI. | ~15 files |
| **PR-L3** UI | Layer-controls panel: interop-level selector, predefined-combination picker, per-plane visibility toggles. | viewer-only |
| **PR-L4** S-98 IC | Drop normative S-100 Part 16 schema + IHO-published IC into `content/S98/catalogue/`; deprecate `Adapter/defaults.xml`. | content-only |

Each PR is **independently mergeable**. PR-L1's plumbing is dead
code (authority is L0, behaves like today) but it gives us the API
surface to land tests against ahead of L2.

---

## 8. Open questions / TBDs

The reviewer should resolve these before PR-L1 is opened. Each
item is actionable as a focused follow-up session.

- **TBD-1.** Validate the proposed XML element names
  (`S100_IC_InteroperabilityCatalogue`, `S100_IC_DisplayPlane`,
  `S100_IC_Feature`, `S100_IC_DrawingInstruction`,
  `S100_IC_PredefinedCombination`,
  `S100_IC_SuppressedFeatureLayer`) against the actual S-100 Part
  16 XSD. Drop the XSD into `content/S98/catalogue/` and validate
  `Adapter/defaults.xml` during build. (Annex A §4.2.1.)

- **TBD-2.** Confirm S-104 / S-111 *colour-band* stacking against
  Annex A §A-6.9.1. The clause is explicit that *"Gridded data
  will generally go over ENC and obscure ENC features"*, but
  practical implementations sometimes render S-101 line work on
  top so soundings, isolated dangers, AtoN remain legible. Our
  proposal in §3.3 places the colour band **under** S-101 lines.
  The reviewer should decide whether to keep that or to follow the
  literal "obscure" reading.

- **TBD-3.** Decide between S-101 split Option A (renderer) vs
  Option B (processor double-pass) — §4.2.1. Recommendation is
  Option B but a microbenchmark on a large ENC cell should confirm
  the < 5% expectation.

- **TBD-4.** Where does S-129 UKC sit by default? S-129 is in
  Annex A Table 1-1 but Annex A doesn't pin a default plane for
  it — Section 9.2.1 layer 6 covers it implicitly under "on-demand
  data". Our table puts S-129 on `OtherChartOverlays`; arguably it
  belongs on `OnDemandSurface` instead. Verify in the eventual
  IHO-published IC.

- **TBD-5.** Filter-expression evaluation (§4.3 of Annex A — strings
  like `"depth>10"`). We've punted at the layer granularity for v1.
  When the layer-controls panel needs to honour
  `S100_IC_PredefinedCombination` filters that *do* discriminate
  by attribute, we'll need to either re-emit per-feature plane
  metadata into `LayerStackEntry` or push filtering into the
  per-product pipelines. Pick the design and write it up before
  PR-L2.

- **TBD-6.** Skin-of-the-earth handling. R-101-102-B suppresses
  `DepthArea` / `DepthContour` when S-102 is loaded. S-98 Annex A
  §8.4.1 + §A-6.9.1 NOTE caution that the safety contour is an IMO
  requirement (MSC.232(82) §5.8) that must continue to be drawn
  even when S-102 replaces depth shading. Decide whether we
  preserve the safety contour as an exception to the suppression
  rule, and where the exception is encoded (in our default IC, or
  by a "safety-contour synthesiser" later).

- **TBD-7.** Pre-defined combination UX: how does the user pick
  one? S-98 Annex A §15.4 says the user *"must be able to
  select"* a PdC. PR-L3 has to design this picker. For now, PR-L2
  hard-codes "use the IC's first PdC whose product set is a
  subset of the loaded products" and ships without UX.

- **TBD-8.** Out-of-S-98 products. We use MSC.530(106)/Rev.1 §App.2
  as informative default-plane derivation for S-122 / S-124 /
  S-125 / S-127 / S-128 / S-131 / S-201 / S-411 / S-421. That is
  **not** a normative S-98 reading. Decide whether the project
  wants to (a) keep these on default planes derived from MSC.530,
  (b) commit to specific planes per product spec, (c) wait for
  S-98 v3.0.0 to enumerate them.

- **TBD-9.** Should `S98DisplayPlane` be an enum or an open
  string-id? An enum bakes in our nine canonical values; the IC
  schema (Annex A §A-3.2.1.1) lets a catalogue declare *any*
  identifier. Recommendation: enum for the canonical planes, with
  an escape hatch `string ExtensionId` carried on `LayerStackEntry`
  for IC-declared custom planes. Decide before PR-L1.

- **TBD-10.** Pick across processors. Today
  `PickService.ResolveHits` dedupes by `(processor, featureRef)`.
  Under S-98 Level 3 hybridization, a single visible feature may
  span two processors (S-101 depth area + S-102 hi-def patch). We
  don't need to solve this for v1, but the dedup key should be
  reviewed before PR-L3 lands hybrid-feature pick.

- **TBD-11.** Should the design note also commit our reading of
  Annex A §15.5 *"Priority overrides for user-specified settings"*?
  We currently let the user drag-reorder datasets in the panel,
  which is broadly equivalent to manual plane override. PR-L3 must
  decide whether that UX survives, lives alongside the plane
  controls, or is replaced by a single ordered list with grouping.

- **TBD-12.** S-98 Annex A §13 metadata: the IC carries
  `S100_ExchangeCatalogue` + `S100_CataloguePointofContact` +
  signature blocks. Our bundled `Adapter/defaults.xml` is *not* a
  signed IHO publication; clarify in the README that this is a
  viewer-internal default, not a forged IHO catalogue.

---

## Appendix A — Code-citation index

For convenience to PR-L1's implementer, the locations referenced
above:

- `src/EncDotNet.S100.Core/Pipelines/Vector/VectorPipeline.cs:315`
  — `enum DisplayPlane { UnderRadar, OverRadar }`.
- `src/EncDotNet.S100.Core/Pipelines/Vector/VectorPipeline.cs:287`
  — `SortByPriority`, the intra-product sort.
- `src/EncDotNet.S100.Core/Pipelines/Vector/DrawingInstruction.cs:34`
  — `DrawingPriority` per instruction.
- `src/EncDotNet.S100.Core/Pipelines/Vector/Part9DisplayListReader.cs:62-355`
  — Part 9 drawing-instruction parser (already extracts plane and
  priority for every GML/XSLT product).
- `src/EncDotNet.S100.Renderers.Mapsui/MapsuiDisplayListRenderer.cs:156-167`
  — area / pattern fill ordering by `DrawingPriority`.
- `src/EncDotNet.S100.Datasets.Pipelines/IDatasetProcessor.cs`
  — `IDatasetProcessor` contract.
- `src/EncDotNet.S100.Datasets.Pipelines/DatasetPipelineFactory.cs:16-34`
  — `DatasetResult` (extension point for `LayerPlanes` /
  `LayerWithinPlanePriorities`).
- `src/EncDotNet.S100.Datasets.Pipelines/S101DatasetProcessor.cs:83`
  — `S101DatasetProcessor.Render`; the split point for §4.2.1.
- `src/EncDotNet.S100.Viewer/Services/DatasetLoaderService.cs:473`
  — `FlattenLayerOrder`; the only place cross-dataset stacking
  exists today.
- `src/EncDotNet.S100.Viewer/Services/DatasetLoaderService.cs:117-118`
  — `EntryLayers` (read-only view of `entry → ILayer[]`); the
  builder needs to read this.
- `src/EncDotNet.S100.Viewer/Services/PickService.cs:135-166`
  — `ResolveHits`; where stack-order sorting is injected.
- `src/EncDotNet.S100.Specifications/content/S111/pc/portrayal_catalogue.xml:181-196`
  — example `<displayPlanes>` block already in a bundled PC.
- `src/EncDotNet.S100.Specifications/content/S111/pc/Rules/SurfaceCurrent.xsl:11-12`
  — example of a portrayal rule emitting
  `<displayPlane>UnderRadar</displayPlane>` and
  `<drawingPriority>10</drawingPriority>`.
