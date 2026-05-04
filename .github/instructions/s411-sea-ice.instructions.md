---
applyTo: "src/EncDotNet.S100.Datasets.S411/**,tests/**/S411*.cs,src/EncDotNet.S100.Specifications/content/S411/**"
---

# S-411 editing rules

When modifying S-411 code or its bundled portrayal assets:

- Load the `s411-sea-ice` skill before proposing non-trivial changes.
- Accept **both** GML shapes (JCOMM `<ice:IceDataSet>` operational
  shape and IHO 1.2.1 `<Dataset>` sample shape); do not normalise
  one to the other before XSLT runs.
- The bundled `content/S411/pc/` tree is **byte-identical to upstream**
  ([iho-ohi/S-411-Product-Specification](https://github.com/iho-ohi/S-411-Product-Specification)).
  Do not edit those files. If the upstream `mainRule` needs adapting
  for this codebase's display-list dialect, change the embedded
  `Adapter/main.xsl` only.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326` (S-100 Part 10b convention).
- S-411 has no information types (`<imember>`); only feature
  wrappers — do not add information-type plumbing.
- Preserve the original `XDocument` on `S411Dataset.SourceDocument`
  and pass it unchanged to the XSLT portrayal pipeline so upstream
  element names and namespaces are honoured.
- Portrayal flows through XSLT, not Lua; keep transforms to features
  supported by .NET's `XslCompiledTransform`.
- Renderers must tolerate geometry-less features.
- Cite the S-411 section number (or upstream catalogue path) in XML
  doc comments when adding spec-derived constants or element names.
- Any new public API requires a matching xunit test; synthetic GML
  fixtures belong under `tests/datasets/`.
