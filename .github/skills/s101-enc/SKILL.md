---
name: s101-enc
description: |
  Expert knowledge of IHO S-101 Electronic Navigational Chart Product
  Specification. Covers ISO 8211 record encoding (S-100 Part 10a),
  S-101 feature/spatial/information records, the Lua-based portrayal
  pipeline (S-100 Part 9A) including drawing instructions, symbol
  references, line/area styles, and the data provider contract used by
  rule scripts. USE FOR: S-101 datasets, ENC files, ISO 8211 parsing,
  S101DocumentReader, S101Dataset, S101VectorSource,
  S101LuaPortrayal, S101LuaDataProvider, DrawingInstructionParser,
  S101PortrayalCatalogue, drawing instructions, Lua portrayal rules,
  vector pipeline changes affecting S-101, adding S-101 features.
  DO NOT USE FOR: S-124 GML-encoded features (use s124-nav-warnings),
  HDF5 coverage products S-102/S-104/S-111 (use their respective
  skills), generic S-100 framework questions (use s100-framework).
---

# S-101 ENC expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S101/**` or
  `tools/TestS101Lua/**`
- Changes to the vector pipeline (`VectorPipeline`,
  `IVectorSource`, `IVectorPortrayalCatalogue`) that affect S-101
- ISO 8211 reader integration (`EncDotNet.Iso8211` package usage)
- Lua portrayal authoring, debugging, sandboxing concerns
- Drawing instruction parsing or rendering of S-101 output

## Spec anchors
- Canonical: **S-101 Edition 1.x** product specification
- S-100 Part 9 / 9A: Portrayal catalogue + Lua rules engine
- S-100 Part 10a: ISO 8211 encoding
- S-101 Annex A: Feature Catalogue
- S-101 Annex B: Portrayal Catalogue

## Review checklist
1. ISO 8211 record reads go through `EncDotNet.Iso8211`; do not
   re-implement field/subfield parsing.
2. Feature/Spatial/Information records map to the model defined in
   `S101Document.cs`; preserve invariants when adding fields.
3. Lua execution stays inside `MoonSharpLuaEngine` (sandboxed Lua 5.2).
   Do not expose host file IO or reflection to scripts.
4. `S101LuaDataProvider` is the only contract the rules see — every
   addition is a spec-defined function from S-100 Part 9A; cite the
   section in XML docs.
5. Drawing instructions parsed by `DrawingInstructionParser` must
   round-trip through `DrawingInstruction` records used by the
   renderers (Skia / Mapsui).
6. Portrayal catalogue assets are loaded via
   `S101PortrayalCatalogue` from `EncDotNet.S100.Specifications` (not
   hard-coded in the dataset library).
7. Tests live in `tests/EncDotNet.S100.Pipelines.Tests` (or a dedicated
   S101 test project). Use `SkippableFact` when a real ENC is required.

## Known pitfalls in this repo
- ISO 8211 string fields can be ISO/IEC 8859-1, UTF-8, or UCS-2; honor
  the DSSI/DSPM character set declaration rather than assuming UTF-8.
- Lua 5.2 (MoonSharp) — not 5.1 or 5.4. Avoid `goto`/`bit32` quirks.
- Drawing instruction priorities and the display-priority ordering are
  spec-defined; do not reorder in the renderer.
- Coordinate values in S-101 are in COMF/SOMF-scaled integers; convert
  to decimal degrees at the source boundary.
