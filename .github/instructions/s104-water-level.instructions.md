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
