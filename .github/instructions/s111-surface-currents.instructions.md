---
applyTo: "src/EncDotNet.S100.Datasets.S111/**,tests/EncDotNet.S100.Datasets.S111.Tests/**"
---

# S-111 editing rules

When modifying S-111 code:

- Load the `s111-surface-currents` skill before proposing non-trivial
  changes.
- Preserve HDF5 group/attribute names exactly as specified in the
  S-111 Edition 2.0.0 product spec (case-sensitive).
- Do not assume `dataCodingFormat` is regular grid (2) — support (or
  explicitly reject) the other formats declared on the root group.
- `surfaceCurrentDirection` is **degrees true, "going to"**
  (oceanographic convention). Document this in XML doc comments where
  the value is exposed.
- Always honor the file's `surfaceCurrentSpeedUom` attribute rather
  than assuming knots.
- Handle 0/360° wrap-around when interpolating or aggregating
  directions.
- Cite the S-111 section number in XML doc comments when adding
  spec-derived constants, attribute names, or group paths.
- Any new public API requires a matching xunit test (use `SkippableFact`
  when a real HDF5 file is required).
