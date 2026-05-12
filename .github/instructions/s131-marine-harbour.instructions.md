# S-131 editing rules

When modifying S-131 code:

- Load the `s131-marine-harbour` skill before proposing non-trivial
  changes.
- GML parsing must accept **both** the canonical S-100 GML 5.0
  namespace (`http://www.iho.int/s100gml/5.0`) and the legacy 1.0
  profile (`http://www.iho.int/S100/profile/s100gml/1.0`). The S-131
  application namespace is `http://www.iho.int/S131/1.0`.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326` (S-100 Part 10b §6.2).
- S-131 uses a **unified `<S131:members>` container** for all features
  AND information types (no `<member>`/`<imember>` split). Feature vs.
  info type discrimination is done via a set of known information type
  codes from the Feature Catalogue.
- Container-style features (e.g. `Authority`) may have no geometry;
  renderers must tolerate geometry-less features.
- **Portrayal flows through Lua (Part 9A), not XSLT.** This is the
  only GML-encoded product in this codebase that uses Lua portrayal.
  The bridge is `S131LuaDataProvider` → `S131LuaRuleExecutor`.
- The bundled `content/S131/pc/` tree is byte-identical to upstream
  `iho-ohi/S-131-Product-Specification-Development`. Do not edit
  those files directly; if upstream needs adapting for this codebase's
  MoonSharp Lua engine, add an `Adapter/main.lua`.
- `DrawingInstructionParser` is reused from
  `EncDotNet.S100.Datasets.S101`. If S-131 emits new instruction
  forms, extend the shared parser rather than forking it.
- Cite the S-131 (FC Ed 1.0.0 / PC Ed 2.0.0) section number in XML
  doc comments when adding spec-derived constants or element names.
- Any new public API requires a matching xunit test; synthetic GML
  fixtures belong under `tests/datasets/S131/`.
