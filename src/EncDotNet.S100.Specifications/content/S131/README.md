# S-131 bundled catalogues

This folder holds the IHO S-131 Marine Harbour Infrastructure feature
catalogue and portrayal catalogue, copied unchanged from the IHO repository.
The S-131 portrayal catalogue uses the S-100 Part 9A Lua portrayal, not XSLT;
it's the only GML product in this repository that does.

## Source

| Item | Value |
|---|---|
| Upstream repository | `iho-ohi/S-131-Product-Specification-Development` |
| Upstream commit | `46eb6c7` (tag `ed2.0.0`) |
| Feature catalogue edition | 2.0.0 (shipped with the portrayal catalogue as `PC/2.0.0/131_FC_2.0.0.20251025.xml`) |
| Portrayal catalogue edition | 2.0.0 |
| Date copied | 2026-05-12 |

## Layout

```text
S131/
├── fc/
│   └── FeatureCatalogue.xml      ← PC/2.0.0/131_FC_2.0.0.20251025.xml
├── pc/
│   ├── portrayal_catalogue.xml   ← PC/2.0.0/portrayal_catalogue.xml
│   ├── Rules/                    ← PC/2.0.0/Rules/*.lua (41 files)
│   ├── Symbols/                  ← PC/2.0.0/Symbols/*.svg and *.css (9 files)
│   ├── LineStyles/               ← PC/2.0.0/LineStyles/*.xml (1 file)
│   └── ColorProfiles/            ← PC/2.0.0/ColorProfiles/*.xml (1 file)
└── README.md                     ← this file
```

## Changes

None. Every file is byte-identical to the upstream original, so don't edit
them here. If the Lua engine (MoonSharp, Lua 5.2) needs an adaptation, put it
in the dataset library rather than in this folder, as S-411 does with its
adapter in
[`src/EncDotNet.S100.Datasets.S411/Adapter`](https://github.com/philliphoff/EncDotNet.S100/tree/main/src/EncDotNet.S100.Datasets.S411/Adapter).
