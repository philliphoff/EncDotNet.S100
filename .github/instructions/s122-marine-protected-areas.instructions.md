---
applyTo: "src/EncDotNet.S100.Datasets.S122/**,tests/EncDotNet.S100.Datasets.S122.Tests/**,src/EncDotNet.S100.Specifications/content/S122/**"
---

# S-122 editing rules

When modifying S-122 code or its bundled portrayal assets:

- GML parsing uses the S-122 application schema namespaces; verify
  against the FC 2.0.0 (and its published XSD) before adding attributes.
- The official 2.0.0 sample dataset is mis-labelled with the **S-123**
  namespace prefix on the dataset root
  (`xmlns:S123="http://www.iho.int/S123/gml/1.0"`). Detection therefore
  must not rely on namespace alone — the S-100
  `<productIdentifier>S-122…</productIdentifier>` element is the source
  of truth. Keep that fallback in `DatasetPipelineFactory`.
- Tolerate the s100gml namespace variants found across S-122 sample
  releases (`http://www.iho.int/s100gml/1.0`,
  `http://www.iho.int/S100/profile/s100gml/1.0`,
  `http://www.iho.int/s100gml/5.0`); do not hard-code one. Use the
  detection helper in `S122DatasetReader.DetectS100Namespace`.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326` (S-100 Part 10b convention).
- **Producer-bug compensation already in `S122DatasetReader`.** Two
  real-world quirks are auto-corrected; new geometry parsing code must
  preserve both:
  1. *lon-lat posList vs. lat-lon envelope.* The UKHO trial dataset
     emits `<gml:posList>` in lon-lat order while keeping the
     `<gml:Envelope>` corners correctly in lat-lon. The reader samples
     parsed coords against the declared envelope and swaps every
     feature when the as-parsed interpretation clearly falls outside
     but the swapped one fits. Keep the heuristic conservative
     (≤25 % as-is fit, ≥75 % swapped fit) so spec-conformant samples
     are untouched.
  2. *Comma-separated tuples in `posList`.* Some producers emit
     `lon,lat lon,lat …` tokens (the `gml:coordinates` convention)
     instead of all-whitespace. `ParsePos` / `ParsePosList` treat both
     whitespace and commas as separators — do not regress that.
- Both the standard `<member>`/`<imember>` wrappers and the inline
  `<members>`/`<imembers>` containers must be supported. Iterate
  descendants of the dataset root and match by feature/info type code.
- The bundled `content/S122/pc/` tree is intended to be byte-identical
  to upstream
  ([iho-ohi/S-122-Product-Specification-Development](https://github.com/iho-ohi/S-122-Product-Specification-Development)
  PC 2.0.0). Do not edit those files. If the upstream `main.xsl` ever
  needs adapting for this codebase's display-list dialect, add a small
  adapter XSLT alongside it (mirror the S-411 pattern).
- Portrayal flows through XSLT, not Lua; keep transforms to features
  supported by .NET's `XslCompiledTransform`.
- Renderers must tolerate geometry-less features (e.g. `TextPlacement`
  may attach to a referenced feature instead of carrying its own
  geometry).
- Cite the S-122 section number (or upstream catalogue path) in XML
  doc comments when adding spec-derived constants or element names.
- Any new public API requires a matching xunit test using the official
  sample under `tests/datasets/S122/`.
- The bundled v2.0.0 PC ships only a `Day` `<palette>` block in
  `colorProfile.xml`, so palette switching to Dusk/Night is a no-op for
  S-122 today. Do not modify the upstream `colorProfile.xml`; if local
  Dusk/Night palettes are needed before upstream publishes them, add a
  synthesised palette in code and document it explicitly.
