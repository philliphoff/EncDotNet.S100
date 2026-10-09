# S-201 bundled catalogues

This folder holds the IALA S-201 Aids to Navigation Information feature
catalogue and portrayal catalogue. They're embedded in
`EncDotNet.S100.Specifications` and available through:

- `Specification.OpenFeatureCatalogueAsync("S-201")`
- `Specification.CreateFeatureCatalogueSource("S-201")`
- `Specification.CreatePortrayalCatalogueSource("S-201")`

## Edition

S-201 Edition 2.0.0, April–May 2025, aligned with S-100 Edition 5.2.0.

| Component | Identification |
|---|---|
| Feature catalogue | `S100FC:productId="S-201"`, `versionNumber="2.0.0"`, `versionDate="2025-05-19"` |
| Portrayal catalogue | `portrayalCatalog productId="S-201" version="1.0"` (the manifest version differs from the specification edition) |
| Top-level rule | `main_PaperChart.xsl` (`ruleType="TopLevelTemplate"`) |

The portrayal catalogue has a Day colour profile only; upstream provides no
Dusk or Night profiles.

## Source

- Repository: <https://github.com/IALA-IGO/S-201_AtoN-Information>
- Commit: `7ddfe8145812141fb8ca413107254f42febd893e`
- Branch: `main`, fetched May 2025

## Changes

The upstream files wrap everything in an extra
`7. S-201 Portrayal Catalogue - Annex D/` folder and give top-level files
numbered, document-style names. Both are removed so the layout matches the
other products in this repository:

| Upstream path | Bundled path |
|---|---|
| `6. S-201 Feature Catalogue - Annex C2.xml` | `fc/FeatureCatalogue.xml` |
| `7. S-201 Portrayal Catalogue - Annex D.zip` (root) | `pc/` (contents only, without the wrapper folder) |
| `pc/.../portrayal_catalogue.xml` | `pc/portrayal_catalogue.xml` |
| `pc/.../Rules/*.xsl` | `pc/Rules/*.xsl` |
| `pc/.../Symbols/*.svg` | `pc/Symbols/*.svg` |
| `pc/.../ColorProfiles/{colorProfile.xml, svgStyle.css}` | `pc/ColorProfiles/{colorProfile.xml, svgStyle.css}` |
| `pc/.../Fonts/*.ttf` | `pc/Fonts/*.ttf` |

Apart from these renames, the files in `fc/` and in `pc/Rules`, `pc/Symbols`,
`pc/ColorProfiles` and `pc/Fonts` are byte-identical to upstream. Don't edit
them here. If the XSLT engine needs an adaptation, put it in the dataset
library rather than in this folder, as S-411 does with the `main.xsl` adapter
in
[`src/EncDotNet.S100.Datasets.S411/Adapter`](https://github.com/philliphoff/EncDotNet.S100/tree/main/src/EncDotNet.S100.Datasets.S411/Adapter).

## Layout

```text
content/S201/
├── README.md                     ← this file
├── fc/
│   └── FeatureCatalogue.xml      ← S100FC/5.0 catalogue
└── pc/
    ├── portrayal_catalogue.xml   ← rule manifest
    ├── Rules/                    ← XSLT (65 files)
    ├── Symbols/                  ← SVG (237 files)
    ├── ColorProfiles/            ← Day colour profile and svgStyle.css
    └── Fonts/                    ← Open Sans and Droid Sans TTF (4 files)
```

## Licence

The S-201 catalogues are © IALA and used under the open publication terms of
the [upstream repository](https://github.com/IALA-IGO/S-201_AtoN-Information).
The fonts are under the Apache License 2.0; see
[`LICENSE-Fonts.txt`](https://github.com/philliphoff/EncDotNet.S100/blob/main/src/EncDotNet.S100.Specifications/LICENSE-Fonts.txt).
