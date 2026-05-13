---
name: s131-marine-harbour
description: |
  Expert knowledge of IHO S-131 Marine Harbour Infrastructure Product
  Specification (FC Edition 1.0.0, PC Edition 2.0.0). Covers GML
  encoding (S-100 Part 10b) in the application namespace
  `http://www.iho.int/S131/1.0` over the S-100 GML 5.0 base, the
  S-131 feature catalogue (berths, bollards, mooring buoys, dolphins,
  locks, dry docks, terminals, anchorage areas, harbour basins,
  authorities, etc.), and **Lua-based portrayal** (S-100 Part 9A) —
  making S-131 the first GML+Lua hybrid in this codebase.
  USE FOR: S-131 datasets, marine harbour infrastructure features,
  berths, bollards, mooring buoys, GML parsing for S-131, Lua
  portrayal of S-131, GML-to-Lua data provider bridge, vector
  pipeline changes affecting S-131, S-131 reader/source code, S-131
  tests, edits to bundled `content/S131/**` assets.
  DO NOT USE FOR: S-101 ENC ISO 8211 features (use s101-enc), S-127
  marine services (use s127-marine-services), S-124 nav warnings
  (use s124-nav-warnings), generic GML / framework concerns (use
  s100-framework).
---

# S-131 Marine Harbour Infrastructure expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S131/**`,
  `tests/EncDotNet.S100.Datasets.S131.Tests/**`, or
  `src/EncDotNet.S100.Specifications/content/S131/**`
- GML+Lua portrayal changes for S-131 features
- Vector pipeline changes (`IVectorSource`, `VectorPipeline`,
  `ILuaRuleExecutor`) affecting S-131
- Changes to the GML-to-Lua bridge (`S131LuaDataProvider`)

## Spec anchors
- Canonical: **S-131 FC Edition 1.0.0 / PC Edition 2.0.0** Marine
  Harbour Infrastructure PS
- S-100 Part 10b: GML encoding (uses the **S-100 GML 5.0** namespace
  `http://www.iho.int/s100gml/5.0`)
- S-100 Part 9A: Lua portrayal (same engine as S-101)
- S-131 application namespace: `http://www.iho.int/S131/1.0`

## Architecture — GML + Lua hybrid

S-131 is unique in this codebase: its data is GML-encoded (like S-124,
S-125, S-127) but its portrayal catalogue uses Lua rules (like S-101).
The bridge is:

1. `S131DatasetReader` — parses GML into `S131Dataset`
2. `S131LuaDataProvider` — adapts GML features to the Lua Host API
   (numeric IDs, synthetic spatial records, attribute path navigation)
3. `S131LuaRuleExecutor` — runs S-131's `main.lua` under MoonSharp
4. `DrawingInstructionParser` — reused from S-101

### S-131 GML shape
- Root: `<S131:Dataset>`
- **Single `<S131:members>` container** wraps ALL features AND info types
  (no `<member>`/`<imember>` split like S-125/S-127)
- Feature vs. info type discrimination via hardcoded info type set from FC

## Review checklist
1. GML parsing accepts both `s100gml/5.0` (canonical for S-131) and
   `s100gml/1.0` for forward/backward compatibility.
2. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for `EPSG:4326` (S-100 Part 10b §6.2). Do not assume lon,lat.
3. Feature recognition uses the unified `<S131:members>` container
   with FC-driven discrimination — no `<member>`/`<imember>` split.
4. Container-style features (e.g. `Authority`, `ContactDetails`) may
   have no geometry; renderer must tolerate geometry-less features.
5. The Lua Host API contract (`HostGetFeatureIDs`, `HostFeatureGetCode`,
   `HostFeatureGetSimpleAttribute`, etc.) matches S-101's contract
   exactly — the bridge just sources from GML instead of ISO 8211.
6. `DrawingInstructionParser` is reused from S-101; if S-131 emits new
   instruction forms, extend the parser rather than forking it.
7. The Lua engine is MoonSharp (Lua 5.2 sandboxed). Test that S-131
   rules run under MoonSharp; avoid non-portable Lua features.
8. The bundled portrayal catalogue under `content/S131/pc/` is
   byte-identical to upstream
   `iho-ohi/S-131-Product-Specification-Development`. Do not edit
   those files directly; if adapting is required, add an
   `Adapter/main.lua` analogous to S-411's `Adapter/main.xsl`.
9. Public API changes have xunit tests; synthetic GML fixtures belong
   under `tests/datasets/S131/`.

## Known pitfalls in this repo
- S-131 uses a **unified `<members>` container** rather than separate
  `<member>` and `<imember>` elements. Patterns from S-125/S-127 that
  assume `<member>` structure will not work.
- Numeric ID mapping: GML `gml:id` strings are mapped to sequential
  doubles for Lua. Keep the mapping bidirectional and deterministic.
- Spatial association synthesis: S-131 GML embeds geometry inline (no
  separate spatial records). The data provider must synthesize
  compatible spatial association structures for `HostGetSpatialData`.
- Attribute path navigation: The Lua rules use `"complexCode:index"`
  syntax to navigate complex attributes. The provider maps this to
  GML element traversal, not ISO 8211 flat arrays.
