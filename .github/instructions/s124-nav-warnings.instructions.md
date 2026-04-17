---
applyTo: "src/EncDotNet.S100.Datasets.S124/**,tests/EncDotNet.S100.Datasets.S124.Tests/**"
---

# S-124 editing rules

When modifying S-124 code:

- Load the `s124-nav-warnings` skill before proposing non-trivial
  changes.
- GML parsing uses the S-124 application schema namespaces; verify
  against the published XSD before adding attributes.
- Warning lifecycle (in-force, cancellation, `messageSeriesIdentifier`,
  `NAVWARNPreamble` references) must round-trip; do not drop metadata
  at the source boundary.
- Portrayal for S-124 flows through XSLT, not Lua; keep transforms to
  features supported by .NET's `XslCompiledTransform`.
- Renderers must tolerate geometry-less features (cancellation
  messages may omit geometry).
- Time validity is UTC — do not coerce to local time at the source.
- Cite the S-124 section number in XML doc comments when adding
  spec-derived constants or element names.
- Any new public API requires a matching xunit test; synthetic GML
  fixtures belong under `tests/datasets/`.
