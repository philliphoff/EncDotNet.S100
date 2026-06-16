---
name: s122-marine-protected-areas
description: |
  Expert knowledge of IHO S-122 Marine Protected Areas Product
  Specification (Edition 2.0.0). Covers GML encoding (S-100 Part 10b)
  over the S-100 GML base, the S-122 application schema (marine
  protected areas, restricted areas, regulated areas, and associated
  text placement), and the XSLT-based portrayal pipeline. USE FOR:
  S-122 datasets, marine protected areas, restricted/regulated areas,
  GML parsing for S-122, XSLT portrayal of S-122, vector pipeline
  changes affecting S-122, S122DatasetReader / S-122 reader/source
  code, S-122 tests, edits to bundled `content/S122/**` assets.
  DO NOT USE FOR: S-127 marine resources and services (use
  s127-marine-services), S-124 nav warnings (use s124-nav-warnings),
  S-125 AtoN (use s125-aton), S-101 ENC (use s101-enc), generic GML /
  framework concerns (use s100-framework).
---

# S-122 Marine Protected Areas expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S122/**` or
  `tests/EncDotNet.S100.Datasets.S122.Tests/**`
- Edits to bundled portrayal assets under
  `src/EncDotNet.S100.Specifications/content/S122/**`
- GML/XSLT portrayal changes for marine protected areas
- Vector pipeline changes (`IVectorSource`, `VectorPipeline`)
  affecting S-122

## Spec anchors
- Canonical: **S-122 Edition 2.0.0** Marine Protected Areas PS
- S-100 Part 10b: GML encoding
- S-100 Part 9: Portrayal (XSLT path)
- S-122 Feature/Information Catalogue (FC 2.0.0) + published XSD
- Upstream PC:
  [iho-ohi/S-122-Product-Specification-Development](https://github.com/iho-ohi/S-122-Product-Specification-Development)

## Review checklist
1. **Product detection is identifier-driven, not namespace-driven.**
   The official 2.0.0 sample is mis-labelled with the **S-123**
   namespace prefix on the dataset root
   (`xmlns:S123="http://www.iho.int/S123/gml/1.0"`). Detection must
   key on the S-100 `<productIdentifier>S-122…</productIdentifier>`
   element — keep that fallback in `DatasetPipelineFactory`.
2. **Tolerate every s100gml namespace variant** seen across releases
   (`http://www.iho.int/s100gml/1.0`,
   `http://www.iho.int/S100/profile/s100gml/1.0`,
   `http://www.iho.int/s100gml/5.0`) via
   `S122DatasetReader.DetectS100Namespace` — do not hard-code one.
3. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for `EPSG:4326` (S-100 Part 10b §6.2).
4. Support **both** the standard `<member>`/`<imember>` wrappers and
   the inline `<members>`/`<imembers>` containers. Iterate descendants
   of the dataset root and match by feature/information type code.
5. Portrayal goes through XSLT (not Lua); inputs to the transform must
   be the canonical GML, and transforms must stay within features
   supported by .NET's `XslCompiledTransform`.
6. Renderers must tolerate **geometry-less features** (e.g.
   `TextPlacement` may attach to a referenced feature rather than carry
   its own geometry).
7. Public API changes have xunit tests using the official sample under
   `tests/datasets/S122/`.

## Known pitfalls in this repo (producer-bug compensation)
`S122DatasetReader` already auto-corrects two real-world quirks; new
geometry parsing code must preserve both:

1. **lon-lat `posList` vs. lat-lon envelope.** The UKHO trial dataset
   emits `<gml:posList>` in lon-lat order while keeping the
   `<gml:Envelope>` corners correctly in lat-lon. The reader samples
   parsed coords against the declared envelope and swaps every feature
   when the as-parsed interpretation clearly falls outside but the
   swapped one fits. Keep the heuristic conservative (≤25 % as-is fit,
   ≥75 % swapped fit) so spec-conformant samples are untouched.
2. **Comma-separated tuples in `posList`.** Some producers emit
   `lon,lat lon,lat …` tokens (the `gml:coordinates` convention)
   instead of all-whitespace. `ParsePos` / `ParsePosList` treat both
   whitespace and commas as separators — do not regress that.

## Portrayal / palette caveats
- The bundled `content/S122/pc/` tree is intended to be
  **byte-identical to upstream** (PC 2.0.0). Do not edit those files.
  If the upstream `main.xsl` ever needs adapting for this codebase's
  display-list dialect, add a small adapter XSLT alongside it (mirror
  the S-411 `Adapter/main.xsl` pattern).
- The v2.0.0 PC ships only a `Day` `<palette>` block in
  `colorProfile.xml`, so palette switching to Dusk/Night is currently a
  no-op for S-122. Do not modify the upstream `colorProfile.xml`; if
  local Dusk/Night palettes are needed before upstream publishes them,
  synthesise them in code and document it explicitly.

## Cite your sources
Cite the S-122 section number (or upstream catalogue path) in XML doc
comments when adding spec-derived constants or element names.
