# S-131 Bundled Specification Content

This directory contains the IHO S-131 Marine Harbour Infrastructure
Feature Catalogue and Portrayal Catalogue assets, copied byte-identically
from the upstream IHO repository.

## Provenance

| Item | Value |
|---|---|
| Upstream repository | `iho-ohi/S-131-Product-Specification-Development` |
| Upstream commit | `46eb6c7` (tag `ed2.0.0`) |
| Feature Catalogue edition | 2.0.0 (bundled with PC; `PC/2.0.0/131_FC_2.0.0.20251025.xml`) |
| Portrayal Catalogue edition | 2.0.0 |
| Date copied | 2026-05-12 |

## Layout

```
S131/
├── fc/
│   └── FeatureCatalogue.xml      ← PC/2.0.0/131_FC_2.0.0.20251025.xml
├── pc/
│   ├── portrayal_catalogue.xml   ← PC/2.0.0/portrayal_catalogue.xml
│   ├── Rules/                    ← PC/2.0.0/Rules/*.lua (41 files)
│   ├── Symbols/                  ← PC/2.0.0/Symbols/*.svg + *.css (9 files)
│   ├── LineStyles/               ← PC/2.0.0/LineStyles/*.xml (1 file)
│   └── ColorProfiles/            ← PC/2.0.0/ColorProfiles/*.xml (1 file)
└── README.md                     ← this file
```

## Notes

- All files are **byte-identical** to the upstream originals. Do not edit
  them directly. If any adaptation is required for this codebase's Lua
  engine (MoonSharp / Lua 5.2), add an `Adapter/` directory analogous to
  the S-411 adapter pattern.
- The S-131 Portrayal Catalogue uses the **S-100 Part 9A Lua portrayal
  pipeline** (not XSLT), which is unique among the GML-encoded products
  in this repository.
