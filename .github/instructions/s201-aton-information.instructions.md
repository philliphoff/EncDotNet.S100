---
applyTo: "src/EncDotNet.S100.Datasets.S201/**,tests/EncDotNet.S100.Datasets.S201.Tests/**,src/EncDotNet.S100.Datasets.Pipelines/S201DatasetProcessor.cs,src/EncDotNet.S100.Specifications/content/S201/**"
---

# S-201 editing rules

When modifying S-201 code:

- Load the `s201-aton-information` skill before proposing non-trivial
  changes.
- S-201 is the **IALA** AtoN-authority spec and is **not** for ECDIS
  display. S-125 remains the right product for ECDIS-facing AtoN. The
  two specs cover overlapping physical objects but live as fully
  independent dataset projects with their own FC, XSD, and PC. Do not
  attempt to share code paths beyond the framework abstractions.
- The S-201 Edition 2.0.0 application schema appears in three
  forms in real-world data, and the reader must tolerate **all
  three**:
  - `http://www.iho.int/S-201/gml/cs0/1.0` — the XSD-canonical form
    declared by Annex B.
  - `http://www.iho.int/S-201/gml/cs0/2.0` — the form used by
    current real-world published datasets (NLB, TH, CIL, IALA
    sample 0227).
  - `http://www.iho.int/201/gml/1.0` — a legacy form (no `S-`
    prefix, no `/cs0/` segment) used by some encoders.
  Geometry uses the S-100 GML 5.0 profile
  (`http://www.iho.int/s100gml/5.0`). Tolerate the older
  `http://www.iho.int/s100gml/1.0` and
  `http://www.iho.int/S100/profile/s100gml/1.0` profile namespaces
  for forward compatibility — do not hard-code a single namespace.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326` (S-100 Part 10b §6.2). Do not assume lon,lat.
- Real S-201 datasets use **two** container shapes and the reader
  must tolerate both:
  - **XSD-canonical shape**: root `<Dataset>` with per-feature
    `<member>` wrappers and per-information `<imember>` wrappers
    (the same shape S-125/S-127 use).
  - **Real-world shape**: root `<DataSet>` (note camelCase) with a
    single unified `<members>` container holding both features
    and information types as direct children (the same shape S-131
    uses). Feature elements inside `<members>` typically appear in
    the empty/default namespace rather than the application-schema
    prefix.
- Feature recognition is **namespace-driven** — any child of a
  `<member>` or `<members>` container whose namespace is not GML
  or S-100 base infrastructure is treated as a feature (the empty
  default namespace counts as application schema for this purpose).
  Information types are discriminated by matching the element
  local name against the closed set of 4 FC information type codes
  (`AtoNFixingMethod`, `AtonStatusInformation`,
  `PositioningInformation`, `SpatialQuality`). Do not introduce a
  hard-coded allow-list for the 62 concrete S-201 feature types.
- S-201 has **two** kinds of cross-reference and they must be
  preserved separately:
  - **Information references** — `xlink:href` to one of the 4
    information types (e.g. `AtoNStatus` → `AtonStatusInformation`,
    `Positioning` → `PositioningInformation`,
    `AtonFixingMethodUsed` → `AtoNFixingMethod`,
    `SpatialAccuracy` → `SpatialQuality`).
  - **Feature references** — `xlink:href` to another feature. The
    real-world `Structure/Equipment` aggregation uses `<child>` and
    `<parent>` element names with `xlink:title="StructureEquipment"`;
    other aggregations use `peer`. **Preserve role names verbatim**
    — do not normalise to the older `theParentFeature` /
    `theSubordinateFeature` names that appeared in earlier drafts.
  The reader splits references after parsing using the set of known
  information-type ids (unknown targets default to feature refs,
  because not every dataset declares info types), then emits info
  refs as `<RoleName informationRef="…"/>` and feature refs as
  `<RoleName featureRef="…"/>` so XSLT rules can distinguish them.
- Container objects (`AtonAggregation`, `AtonAssociation`) and
  abstract supertypes (`AidsToNavigation`, `StructureObject`,
  `Equipment`) may carry no geometry. Renderer must tolerate
  geometry-less features.
- Portrayal flows through **XSLT** (no Lua). The top-level template
  is `main_PaperChart.xsl`; per-feature sub-templates load via
  `xsl:include` (resolved by the catalogue's `XmlResolver`). The
  bundled PC ships **Day-only** color profile.
- The synthesised FeatureXML neutral form must include both
  `<Dataset><Features>` and `<Dataset><InformationTypes>` blocks.
- Bundled `content/S201/pc/` files are byte-identical to the IALA-IGO
  upstream after the wrapper-folder strip; no in-place edits. If the
  upstream catalogue ever needs adapting for our XSLT engine, follow
  the S-411 pattern (`Adapter/main.xsl` wrapping the upstream
  catalogue) and document it loudly in the bundled README.
- Cite the S-201 Edition 2.0.0 section number in XML doc comments
  when adding spec-derived constants, feature codes, or attribute
  names. Cite S-100 Part 10b § for GML-encoding concerns.
- Time validity (`fixedDateRange`, `periodicDateRange`) is UTC; do
  not coerce to local time at the source boundary.
- Any new public API requires a matching xunit test; synthetic GML
  fixtures belong under `tests/datasets/S201/`. **Do not** copy
  sample data from third-party tools (e.g. `s-201tool.gla-rad.org`)
  without verifying licensing first; default position is "don't".
