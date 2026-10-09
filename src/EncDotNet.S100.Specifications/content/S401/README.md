# S-401 bundled catalogues

This folder holds the S-401 Inland Electronic Navigational Chart (IENC) feature
catalogue and portrayal catalogue, copied from the repositories of the Inland
ENC Harmonization Group (IEHG).

The IEHG, not the IHO, produces and maintains S-401. The IHO assigned the
product number and lists it in the GI Registry. S-401 Edition 1.3.0 is released
for implementation and testing only; the IEHG plans a first operational
Edition 2.0.0 after testing.

## Attribution

- Feature catalogue and portrayal catalogue © Inland ENC Harmonization Group
  (IEHG), <https://ienc.openecdis.org/>.
- S-401 is derived from IHO S-101. The S-401 Product Specification reproduces
  S-101 material with the permission of the IHO Secretariat (IHO Permission
  N°10/2024). The IHO doesn't accept responsibility for the correctness of
  that material as reproduced, modified or translated by the IEHG, and its
  inclusion doesn't mean the IHO endorses it.
- The upstream repositories have no licence file. These files are
  redistributed unmodified, apart from the folder rename and line-ending
  change in [Changes](#changes), with this attribution so that S-401 datasets
  can be portrayed with the bundled catalogues.

## Source

| Item | Value |
|---|---|
| Feature catalogue repository | <https://github.com/IEHG/Feature-Catalogue> |
| Feature catalogue commit | `bf64f1abe63ed718f2715c6cda5cf928353ab387` (2026-05-26) |
| Feature catalogue upstream file | `S-401 Feature Catalogue edition 1.3.0.xml` |
| Feature catalogue edition | `productId="S-401"`, `versionNumber="1.3.0"`, `versionDate="2026-05-15"` |
| Portrayal catalogue repository | <https://github.com/IEHG/Portrayal-Catalogue> |
| Portrayal catalogue commit | `0bf3725eff4322e127bb9cf48199a3c1c49bdf76` (2026-07-15) |
| Portrayal catalogue edition | `portrayalCatalog productId="S-401" version="1.3.0"` |
| Product specification | S-401 Edition 1.3.0, May 2026 (§9.2 cites portrayal catalogue edition 1.3.0) |
| Date copied | 2026-09-14 |

The feature catalogue is the copy in the IEHG `Feature-Catalogue` repository.
It differs from the one attached to the published Edition 1.3.0 documentation
(<https://editions.openecdis.org/edition-2.5/s-401>, `versionDate` 2026-05-05)
only in its `versionDate` and a corrected `postalCode` attribute name
("Postal colde" → "Postal code").

The IEHG hasn't formally published the portrayal catalogue yet; the IHO GI
Registry lists it as "still in development". This copy is the working
catalogue from the IEHG repository at the commit above. Its rules are the S-101
Edition 2.0.0 Lua rules plus inland rules, whose file names start with `_`.

## Changes

| Upstream path | Bundled path |
|---|---|
| `Feature-Catalogue/S-401 Feature Catalogue edition 1.3.0.xml` | `fc/FeatureCatalogue.xml` |
| `Portrayal-Catalogue/Portrayal Catalogue/` (root) | `pc/` (contents only, without the wrapper folder) |
| `Portrayal Catalogue/Linestyles/` | `pc/LineStyles/` |

`Linestyles` is renamed to `LineStyles` because that's the folder the
portrayal loaders look in for `<lineStyles>` manifest entries, and the name the
other bundled Lua catalogues use. Without the rename, the 46 line style
references would resolve only through the case-insensitive fallback, and not at
all when extracted to a case-sensitive file system.

Line endings are normalized to LF, as in the other bundled catalogues; 245
upstream files use CRLF. File contents are otherwise identical to upstream.
Don't edit them here.

## Layout

```text
S401/
├── README.md                     ← this file
├── fc/
│   └── FeatureCatalogue.xml      ← S100FC/5.2; 178 feature types, 6 information types
└── pc/
    ├── portrayal_catalogue.xml   ← rule and asset manifest
    ├── Rules/                    ← Lua (205 files)
    ├── Symbols/                  ← SVG (874 files) and CSS (3 files)
    ├── LineStyles/               ← XML (63 files)
    ├── AreaFills/                ← XML (25 files)
    └── ColorProfiles/            ← colorProfile.xml (Day, Dusk, Night) and svgStyle.css
```

## Known upstream gaps

At the commits above:

- `portrayal_catalogue.xml` refers to `Symbols/NOTMRK03.svg`, which isn't in
  the upstream repository.
- 20 asset files aren't referenced by the manifest: 17 line styles,
  `AreaFills/ICEARE04.xml`, `AreaFills/OVERSC01.xml` and
  `ColorProfiles/svgStyle.css`.
- The IEHG Portrayal-Catalogue repository has open issues about unregistered
  or pending notice mark symbols and others.

Refresh these files when the IEHG publishes an official portrayal catalogue or
a new S-401 edition.
