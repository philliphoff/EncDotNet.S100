# S-127 editing rules

When modifying S-127 code:

- Load the `s127-marine-services` skill before proposing non-trivial
  changes.
- GML parsing must accept **both** the canonical S-100 GML 5.0
  namespace (`http://www.iho.int/s100gml/5.0`) and the legacy 1.0
  profile (`http://www.iho.int/S100/profile/s100gml/1.0`). The S-127
  application namespace is `http://www.iho.int/S127/2.0`.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326` (S-100 Part 10b §6.2).
- Feature recognition is namespace-driven (any `<member>` child whose
  namespace matches the dataset's application schema is a feature) —
  do not introduce a hard-coded feature-type allow-list.
- S-127 Edition 2.0.0 has **no information types**; `imember`
  parsing is kept for forward compatibility but expected to be empty.
- Container-style features (e.g. `Authority`) may have no geometry;
  renderers must tolerate geometry-less features.
- Portrayal flows through XSLT (not Lua); keep transforms to features
  supported by .NET's `XslCompiledTransform`.
- The bundled `content/S127/pc/` tree is byte-identical to upstream
  `iho-ohi/S-127-Product-Specification-Development`. Do not edit
  those files directly; if upstream needs adapting for this codebase,
  add an adapter analogous to S-411's `Adapter/main.xsl`.
- Cite the S-127 (Edition 2.0.0) section number in XML doc comments
  when adding spec-derived constants or element names.
- Any new public API requires a matching xunit test; synthetic GML
  fixtures belong under `tests/datasets/S127/`.
