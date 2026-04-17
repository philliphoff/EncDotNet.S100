---
applyTo: "src/EncDotNet.S100.Core/**,src/EncDotNet.S100.ExchangeSets/**,src/EncDotNet.S100.Features/**,src/EncDotNet.S100.Portrayals/**,src/EncDotNet.S100.Specifications/**"
---

# S-100 framework editing rules

When modifying code in these projects:

- Load the `s100-framework` skill before proposing non-trivial changes.
- Preserve the abstraction-first pattern: concrete I/O
  (`PureHdfFile`, `MoonSharpLuaEngine`, `FileSystemAssetSource`,
  `ZipAssetSource`) stays behind interfaces defined in
  `EncDotNet.S100.Core`.
- Cite the relevant S-100 Edition 5.2.1 Part/section in XML doc comments
  when adding spec-derived constants, enums, attribute names, or group
  paths.
- New bundled spec assets belong under
  `src/EncDotNet.S100.Specifications/content/<SXXX>/` and must be
  exposed through `Specification.*` factory methods — not ad-hoc
  resource loading in dataset libraries.
- Any new public API requires a matching xunit test under
  `tests/` (use `SkippableFact` when a real asset file is required).
