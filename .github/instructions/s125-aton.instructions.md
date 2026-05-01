---
applyTo: "src/EncDotNet.S100.Datasets.S125/**,tests/EncDotNet.S100.Datasets.S125.Tests/**"
---

# S-125 editing rules

When modifying S-125 code:

- Load the `s125-aton` skill before proposing non-trivial changes.
- The S-125 1.0.0 application schema target namespace is
  `http://www.iho.int/S125/1.0`; geometry uses the S-100 GML 5.0
  profile (`http://www.iho.int/s100gml/5.0`). Tolerate the older
  `http://www.iho.int/s100gml/1.0` and
  `http://www.iho.int/S100/profile/s100gml/1.0` profile namespaces
  still seen in IHO sample datasets — do not hard-code a single
  namespace.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326`. Do not assume lon,lat.
- Information bindings on AtoN features (e.g. `AtoNStatus`,
  `SpatialAccuracy`) are encoded via `xlink:href` references to
  `imember`-scoped information types. Preserve the role name and
  the trimmed identifier so the bundled XSLT rules can resolve them.
- Container objects (`AtonAggregation`, `AtonAssociation`) carry no
  geometry. Do not synthesise it.
- Portrayal flows through XSLT (no Lua). The top-level template is
  `main.xsl` and per-feature sub-templates are included via
  `xsl:include`. The bundled S-125 1.0.0 development PC ships only
  AtoN status indication, AtoN status information, and DataCoverage
  rules — keep this in mind when expanding portrayal coverage.
- The synthesised FeatureXML neutral form must include both
  `<Dataset><Features>` and `<Dataset><InformationTypes>` — the PC's
  AtoN status template walks `/Dataset/InformationTypes/...[@id=$ref]`.
- Cite the S-125 section number in XML doc comments when adding
  spec-derived constants, feature codes, or attribute names.
- Any new public API requires a matching xunit test; synthetic GML
  fixtures belong under `tests/datasets/S125/`.
