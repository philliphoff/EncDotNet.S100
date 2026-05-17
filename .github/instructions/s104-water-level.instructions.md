---
applyTo: "src/EncDotNet.S100.Datasets.S104/**,tests/EncDotNet.S100.Datasets.S104.Tests/**"
---

# S-104 editing rules

When modifying S-104 code:

- Load the `s104-water-level` skill before proposing non-trivial changes.
- Preserve HDF5 group/attribute names exactly as specified in the
  S-104 Edition 2.0.0 product spec (case-sensitive).
- Do not assume `dataCodingFormat` is regular grid (2) — support (or
  explicitly reject) the other formats declared on the root group.
- `waterLevelTrend` is a spec-defined enumeration (0 nodata, 1
  decreasing, 2 increasing, 3 steady). Never recompute it client-side;
  preserve producer-supplied values.
- Times derive from `dateTimeOfFirstRecord` + `timeRecordInterval *
  index` unless an explicit time dataset exists.
- Cite the S-104 section number in XML doc comments when adding
  spec-derived constants, attribute names, or group paths.
- Any new public API requires a matching xunit test (use `SkippableFact`
  when a real HDF5 file is required).
- **Portrayal is synthesised by design.** IHO publishes no official S-104
  portrayal catalogue (the spec treats water levels as input to ECDIS
  depth adjustment, not a visual layer). `S104PortrayalCatalogue` ships
  hand-coded Day / Dusk / Night band tables and per-palette `NoDataColor`
  values purely for viewer parity with S-102 / S-111. Do **not** invent
  a synthetic IHO-shaped PC under `content/S104/pc/`; that directory
  stays `.gitkeep`-only until IHO publishes one. If you change the band
  table, preserve the Day palette byte-for-byte (backward compatibility)
  and update `S104PortrayalCatalogueTests` accordingly.
