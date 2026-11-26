---
name: s101-enc
description: |
  Expert knowledge of the IHO S-101 ENC Product Specification
  (Edition 2.0.0) and the S-401 Inland ENC, which shares its encoding and
  portrayal. Covers ISO 8211 records (S-100 Part 10a), feature/spatial/
  information records, sequential updates, validation, and the Lua
  portrayal (Part 9a): drawing instructions, the host data provider,
  patches to upstream rules. USE FOR: S-101 or S-401 datasets, ENC
  files, ISO 8211 parsing/writing, src/EncDotNet.S100.Datasets.S101,
  S101DatasetProcessor, S101DocumentReader/Writer, S101Dataset,
  S101VectorSource, S101LuaRuleExecutor, S101LuaDataProvider,
  S401PortrayalPatches, S101PortrayalCatalogue, DrawingInstructionParser
  as used by S-101, bundled content/S101 or content/S401 catalogues,
  Lua portrayal rules, tools/TestS101Lua. DO NOT USE FOR: GML products
  (s100-gml-products + product skill), HDF5 coverages (s102/s104/s111
  skills), the Lua engines or generic framework questions
  (s100-framework), draw order/declutter (chart-cartography).
---

# S-101 / S-401 ENC expert

## When engaged
- `src/EncDotNet.S100.Datasets.S101/**`,
  `src/EncDotNet.S100.Datasets.Pipelines/S101*.cs`,
  `src/EncDotNet.S100.Specifications/content/{S101,S401}/**`,
  `tools/TestS101Lua/**`, `tests/EncDotNet.S100.Datasets.S101.Tests/**`,
  `tests/EncDotNet.S100.Pipelines.Tests/S101*.cs` / `S401*.cs`
- Vector pipeline changes that affect S-101 (`IVectorSource`,
  `IVectorPortrayalCatalogue`, `DrawingInstructionParser`)
- Lua portrayal authoring, debugging, rule patches

## Spec anchors
- Canonical: **S-101 Edition 2.0.0** —
  `docs/S-101_ENC_Product_Specification_Ed_2.0.0.pdf`. Bundled FC and PC
  are 2.0.0; cells from earlier editions are read, and their old feature
  class names are mapped by `S101LegacyFeatureNames` for portrayal only.
- S-101 Annex A: Data Classification and Encoding Guide; Annex B: data
  product format (encoding)
- S-100 Part 9 / 9a (portrayal, Lua), Part 10a (ISO 8211)
- S-401 (IEHG, not IHO): Edition 1.3.0 FC/PC bundled under
  `content/S401/` — provenance and known upstream defects in its README.

## How S-401 fits
`S100Products.S401` reuses `S101DatasetProcessor` with the S-401
catalogues; `S101VectorSource` reports the product the DSID declares.
Everything in the S-101 library except the validation rule pack applies
to S-401. S-401 rule defects are corrected at load time by
`S401PortrayalPatches` (via `S101LuaDataProvider`'s post-load scripts),
and `S401PortrayalPatchTests` fails once upstream fixes one — then delete
the patch.

## Review checklist
1. ISO 8211 reads/writes go through the `EncDotNet.Iso8211` package; do
   not re-implement field/subfield parsing or character decoding (the
   package honours the DDR lexical level).
2. Records map to the model in `S101Document.cs`; `S101DocumentWriter`
   must keep read → write → read identical. Updates go through
   `S101UpdateApplicator` / `S101Document.ApplyChanges`.
3. `S101LuaDataProvider` is the only contract the rules see; every
   addition is a Part 9a host function with its section cited.
   `S101LuaRuleExecutor` wraps Core's product-neutral `LuaRuleExecutor`.
4. Bundled catalogues are byte-identical to upstream. Fix a broken rule
   with an ordered post-load patch in `S101LuaDataProvider` (or
   `S401PortrayalPatches`), never by editing `content/`; document it in
   the README and remove it when upstream fixes the defect.
5. Drawing instructions are parsed by `DrawingInstructionParser`
   (`src/EncDotNet.S100.Core/Pipelines/Vector/`, shared with S-131 and
   the GML products) into `DrawingInstruction` records; keep renderer-
   specific types out of the parser.
6. Run new or changed rules on both Lua engines —
   `LuaEngineDifferentialTests` compares them.
7. Tests: reader/writer/update/validation in
   `tests/EncDotNet.S100.Datasets.S101.Tests`; processor, portrayal and
   S-401 in `tests/EncDotNet.S100.Pipelines.Tests`. Real-cell tests use
   `ENCDOTNET_S101_BASE_CELL`.

## Known pitfalls in this repo
- Coordinates are COMF/SOMF-scaled integers (DSSI); convert to decimal
  degrees at the source boundary (`S101VectorSource`), not in pipeline
  or renderer code.
- Surfaces carry interior rings (`RIAS`, `USAG` = 2); don't drop them.
- Soundings are multipoint records (`RCNM` = 115) with many depths per
  feature — pick/search per depth point (`S101SoundingSampler`).
- Drawing priority and display-plane ordering are spec-defined; do not
  reorder in the renderer.
- `Debug.Trace` output from rules (e.g. OBSTRN07 fallbacks) is expected,
  not an error; it goes to the provider's `trace` callback.
