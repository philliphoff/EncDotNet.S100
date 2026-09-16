# S-401 Bundled Specification Content

This directory contains the IEHG S-401 Inland Electronic Navigational Chart
(IENC) Feature Catalogue and Portrayal Catalogue assets, copied from the
Inland ENC Harmonization Group's upstream repositories.

S-401 is produced and maintained by the **Inland ENC Harmonization Group
(IEHG)**, not the IHO; the IHO assigned the product number and lists it in the
GI Registry. S-401 Edition 1.3.0 is released for **implementation and testing
only** — IEHG intends a first operational Edition 2.0.0 after testing.

## Attribution

- Feature Catalogue and Portrayal Catalogue © Inland ENC Harmonization Group
  (IEHG), <https://ienc.openecdis.org/>.
- S-401 is derived from IHO S-101. The S-401 Product Specification reproduces
  S-101 material with the permission of the IHO Secretariat (IHO Permission
  N°10/2024); the IHO does not accept responsibility for the correctness of
  that material as reproduced, modified, or translated by IEHG, and its
  incorporation does not constitute an IHO endorsement.
- The upstream repositories carry no explicit licence file. These assets are
  redistributed unmodified (apart from the directory rename and line-ending
  normalization below) with this attribution so the bundled S-401 product can be portrayed out of the box.

## Provenance

| Item | Value |
|---|---|
| Feature Catalogue repository | <https://github.com/IEHG/Feature-Catalogue> |
| Feature Catalogue commit | `bf64f1abe63ed718f2715c6cda5cf928353ab387` (2026-05-26) |
| Feature Catalogue upstream file | `S-401 Feature Catalogue edition 1.3.0.xml` |
| Feature Catalogue edition | `productId="S-401"`, `versionNumber="1.3.0"`, `versionDate="2026-05-15"` |
| Portrayal Catalogue repository | <https://github.com/IEHG/Portrayal-Catalogue> |
| Portrayal Catalogue commit | `0bf3725eff4322e127bb9cf48199a3c1c49bdf76` (2026-07-15) |
| Portrayal Catalogue edition | `portrayalCatalog productId="S-401" version="1.3.0"` |
| Product Specification | S-401 Edition 1.3.0, May 2026 (§9.2 cites PC edition 1.3.0) |
| Date copied | 2026-09-14 |

The Feature Catalogue is the copy from the IEHG `Feature-Catalogue` repository.
It differs from the FC attached to the published Edition 1.3.0 documentation
(<https://editions.openecdis.org/edition-2.5/s-401>, `versionDate` 2026-05-05)
only in its `versionDate` and a corrected `postalCode` attribute name
("Postal colde" → "Postal code").

The Portrayal Catalogue has **not yet been formally published** by IEHG (the
IHO GI Registry lists it as "still in development"); this is the working
catalogue from IEHG's repository at the commit above. Its rules are S-101
Edition 2.0.0 Lua scripts plus inland-specific rules (files prefixed `_`).

## Rename mapping (upstream → bundled)

| Upstream path | Bundled path |
|---|---|
| `Feature-Catalogue/S-401 Feature Catalogue edition 1.3.0.xml` | `fc/FeatureCatalogue.xml` |
| `Portrayal-Catalogue/Portrayal Catalogue/` (root) | `pc/` (contents only — wrapper folder stripped) |
| `Portrayal Catalogue/Linestyles/` | `pc/LineStyles/` |

`Linestyles` is renamed to `LineStyles` because that is the directory the
portrayal loaders request for `<lineStyles>` manifest entries and the name
every other bundled Lua catalogue in this repository uses (without it, all 46
line-style references would only resolve via the case-insensitive fallback, and
not at all when extracted to a case-sensitive filesystem). File contents are
identical to upstream except that line endings are normalized to LF (245
upstream files use CRLF), matching the other bundled catalogues; do not edit
them in place.

## Layout

```
S401/
├── README.md                     ← this file
├── fc/
│   └── FeatureCatalogue.xml      ← S100FC/5.2; 178 feature types, 6 information types
└── pc/
    ├── portrayal_catalogue.xml   ← rule/asset manifest
    ├── Rules/                    ← Lua (205 files)
    ├── Symbols/                  ← SVG (874 files) + CSS (3 files)
    ├── LineStyles/               ← XML (63 files)
    ├── AreaFills/                ← XML (25 files)
    └── ColorProfiles/            ← colorProfile.xml (Day/Dusk/Night) + svgStyle.css
```

## Known upstream gaps (at the pinned commit)

- `portrayal_catalogue.xml` references `Symbols/NOTMRK03.svg`, which is not
  present upstream.
- 20 asset files are present but not referenced by the manifest (17 line
  styles, `AreaFills/ICEARE04.xml`, `AreaFills/OVERSC01.xml`, and
  `ColorProfiles/svgStyle.css`).
- IEHG's Portrayal-Catalogue repository has open issues for unregistered or
  pending notice-mark and other symbols.

Refresh from upstream when IEHG publishes an official Portrayal Catalogue or a
new S-401 edition.
