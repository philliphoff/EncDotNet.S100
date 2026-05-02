---
applyTo: 'src/EncDotNet.S100.Datasets.S128/**,tests/EncDotNet.S100.Datasets.S128.Tests/**,src/EncDotNet.S100.Datasets.Pipelines/S128DatasetProcessor.cs,src/EncDotNet.S100.Specifications/content/S128/**'
---

# S-128 editing rules

When modifying S-128 code or its bundled portrayal assets:

- GML parsing must accept **both** the canonical S-100 GML 5.0 namespace
  (`http://www.iho.int/s100gml/5.0`, used by the upstream 2.0.0 sample)
  and the legacy 1.0 profile namespaces
  (`http://www.iho.int/s100gml/1.0`,
  `http://www.iho.int/S100/profile/s100gml/1.0`). The S-128
  application namespace is `http://www.iho.int/S128/2.0`.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326` (S-100 Part 10b §6.2). Do not assume lon,lat.
- Feature recognition is namespace-driven: every `<S128:members>` /
  `<S128:member>` child whose namespace matches the dataset's
  application schema is surfaced as an `S128Feature`. Do not introduce
  a hard-coded feature-type allow-list — the upstream sample carries
  metadata records (e.g. `DistributorInformation`,
  `ProducerInformation`, `ContactDetails`, `CatalogueSectionHeader`)
  inside `<members>` and the bundled `main.xsl` walks
  `Dataset/Features/*` accordingly.
- `<imember>` / `<imembers>` plumbing is kept for forward
  compatibility but expected to be empty for 2.0.0 datasets.
- Container-style features (`DistributorInformation`,
  `CatalogueSectionHeader`, etc.) may have no geometry; renderers
  must tolerate geometry-less features.
- `S128ProductEntry` is the *only* place that should expose a
  strongly-typed view over product features. Keep the parser
  FC-faithful — do not promote/demote attributes inside the reader.
- The producer-bug compensations are inherited from the S-122
  reader: `s100gml` namespace tolerance, `<member>` and `<members>`
  wrappers, comma-and-whitespace `posList` tokens, and the lon-lat
  axis-order swap heuristic against `<gml:Envelope>`. Preserve all
  four behaviours.
- The bundled `content/S128/{fc,pc}/` tree is **byte-identical to
  upstream** ([`iho-ohi/S-128-Product-Specification-Development`
  @ `c266c43820ce…`](https://github.com/iho-ohi/S-128-Product-Specification-Development)).
  Do not edit those files directly. If the upstream `main.xsl`
  ever needs adapting for this codebase's display-list dialect,
  add a small adapter XSLT alongside it (mirror the S-411 pattern).
- Portrayal flows through XSLT (no Lua); keep transforms to features
  supported by .NET's `XslCompiledTransform`.
- `S128ProductEntry.Status` is a **heuristic** derived from
  `serviceStatus` / `distributionStatus`. Document changes to the
  heuristic in the project README's "Status heuristic" section.
- Cite the S-128 (Edition 2.0.0) section number in XML doc comments
  when adding spec-derived constants or element names.
- Any new public API requires a matching xunit test; synthetic GML
  fixtures belong under `tests/datasets/S128/`. The official
  upstream sample (`S128_TDS_sample.gml`) is checked in there for
  round-trip tests.
