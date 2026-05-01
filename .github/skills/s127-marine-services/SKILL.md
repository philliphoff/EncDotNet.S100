---
name: s127-marine-services
description: |
  Expert knowledge of IHO S-127 Marine Resources and Services Product
  Specification (Edition 2.0.0). Covers GML encoding (S-100 Part 10b)
  in the application namespace `http://www.iho.int/S127/2.0` over the
  S-100 GML 5.0 base, the S-127 feature catalogue (pilot boarding
  places, routeing measures, restricted areas, vessel traffic
  services, signal stations, etc.), and XSLT-based portrayal.
  USE FOR: S-127 datasets, marine traffic management features, pilot
  services, routeing measures, restricted/military/caution areas,
  vessel traffic services, GML parsing for S-127, XSLT portrayal of
  S-127, vector pipeline changes affecting S-127, S-127 reader/source
  code, S-127 tests, edits to bundled `content/S127/**` assets.
  DO NOT USE FOR: S-124 nav warnings (use s124-nav-warnings),
  S-129 UKC (use s129-ukc), S-101 ENC (use s101-enc), generic GML
  / framework concerns (use s100-framework).
---

# S-127 Marine Resources and Services expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S127/**`,
  `tests/EncDotNet.S100.Datasets.S127.Tests/**`, or
  `src/EncDotNet.S100.Specifications/content/S127/**`
- GML/XSLT portrayal changes for S-127 features
- Vector pipeline changes (`IVectorSource`, `VectorPipeline`)
  affecting S-127

## Spec anchors
- Canonical: **S-127 Edition 2.0.0** Marine Resources and Services PS
- S-100 Part 10b: GML encoding (uses the **S-100 GML 5.0** namespace
  `http://www.iho.int/s100gml/5.0`, NOT the older `1.0` profile used
  by S-124)
- S-100 Part 9: Portrayal (XSLT path; `main.xsl` + per-feature
  sub-templates including a `Default.xsl` fallback)
- S-127 application namespace: `http://www.iho.int/S127/2.0`

## Review checklist
1. GML parsing accepts both `s100gml/5.0` (canonical for S-127) and
   `s100gml/1.0` for forward/backward compatibility.
2. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for `EPSG:4326` (S-100 Part 10b §6.2). Do not assume lon,lat.
3. Feature recognition uses the application-schema namespace match,
   not a hard-coded feature-type allow-list — keeps the parser
   resilient when the FC adds new feature classes.
4. S-127 Edition 2.0.0 has **no information types**; `imember`
   parsing is preserved for forward compatibility but the array is
   expected to be empty in real data.
5. Container-style features (e.g. `Authority`) may have no geometry;
   renderer must tolerate geometry-less features.
6. Portrayal flows through XSLT (not Lua); keep transforms to features
   supported by .NET's `XslCompiledTransform`.
7. The bundled portrayal catalogue under `content/S127/pc/` is
   byte-identical to upstream
   `iho-ohi/S-127-Product-Specification-Development`. If upstream
   needs adapting for this codebase, prefer adding an adapter (as
   done for S-411) rather than editing the catalogue files directly.
8. Public API changes have xunit tests; synthetic GML fixtures belong
   under `tests/datasets/S127/`.

## Known pitfalls in this repo
- Upstream `main.xsl` references some sub-templates with case-mismatched
  filenames. The catalogue's `AssetSourceXmlResolver` matches
  case-insensitively, but Linux CI may need explicit handling on
  case-sensitive filesystems.
- No real sample datasets are published upstream — tests rely on
  synthetic GML fixtures.
- The S-100 GML namespace differs from S-124 (5.0 vs 1.0); when
  copying patterns from `EncDotNet.S100.Datasets.S124`, be careful to
  flip the namespace constants.
