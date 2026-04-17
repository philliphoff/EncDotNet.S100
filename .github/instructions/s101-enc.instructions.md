---
applyTo: "src/EncDotNet.S100.Datasets.S101/**,tools/TestS101Lua/**,tests/**/S101*.cs"
---

# S-101 editing rules

When modifying S-101 code:

- Load the `s101-enc` skill before proposing non-trivial changes.
- Read ISO 8211 records via the `EncDotNet.Iso8211` package — do not
  re-implement field/subfield parsing.
- Preserve the Lua sandbox: `MoonSharpLuaEngine` must not expose host
  file I/O or reflection to scripts. The contract visible to rules is
  `S101LuaDataProvider`; every added method maps to a spec-defined
  Part 9A function and carries an XML doc with the section number.
- Portrayal catalogue assets come from `EncDotNet.S100.Specifications`;
  do not hard-code rule file paths in the dataset library.
- Drawing instructions must round-trip through `DrawingInstruction`
  records shared with the Skia/Mapsui renderers; do not introduce
  renderer-specific types into the parser.
- Respect COMF/SOMF integer coordinate scaling — convert to decimal
  degrees at the source boundary, not in pipeline/renderer code.
- Any new public API requires a matching xunit test (use `SkippableFact`
  when a real ENC is required).
