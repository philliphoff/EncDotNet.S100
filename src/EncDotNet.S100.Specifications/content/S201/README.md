# Bundled S-201 Aids to Navigation Information assets

These assets are bundled as embedded resources of the
`EncDotNet.S100.Specifications` library and are exposed at runtime via:

- `Specification.OpenFeatureCatalogueAsync("S-201")`
- `Specification.CreateFeatureCatalogueSource("S-201")`
- `Specification.CreatePortrayalCatalogueSource("S-201")`

## Edition

**S-201 Edition 2.0.0**, Apr–May 2025 (aligned with S-100 Edition 5.2.0).

| Component | Identification |
|---|---|
| Feature Catalogue | `S100FC:productId="S-201"`, `versionNumber="2.0.0"`, `versionDate="2025-05-19"` |
| Portrayal Catalogue | `portrayalCatalog productId="S-201" version="1.0"` (manifest version is distinct from spec edition) |
| Top-level rule | `main_PaperChart.xsl` (`ruleType="TopLevelTemplate"`) |

## Provenance

Sourced from the IALA-IGO upstream repository:

- Repository: <https://github.com/IALA-IGO/S-201_AtoN-Information>
- Commit: `7ddfe8145812141fb8ca413107254f42febd893e`
- Branch: `main` (fetched 2025-05)

Files inside `fc/` and `pc/{Rules,Symbols,ColorProfiles,Fonts}/` are
**byte-identical** to upstream after the renaming described below.
Do not edit them in place; if upstream needs adapting for this
codebase's XSLT engine, follow the S-411 pattern and add a separate
`Adapter/main.xsl` that wraps the upstream catalogue.

## Rename mapping (upstream → bundled)

The upstream zip wraps everything in an extra
`7. S-201 Portrayal Catalogue - Annex D/` directory and uses
documentation-style numbered prefixes on top-level files. Both
inconveniences are stripped on bundling so the layout matches every
other product in this repository:

| Upstream path | Bundled path |
|---|---|
| `6. S-201 Feature Catalogue - Annex C2.xml` | `fc/FeatureCatalogue.xml` |
| `7. S-201 Portrayal Catalogue - Annex D.zip` (root) | `pc/` (contents only — wrapper folder stripped) |
| `pc/.../portrayal_catalogue.xml` | `pc/portrayal_catalogue.xml` |
| `pc/.../Rules/*.xsl` | `pc/Rules/*.xsl` |
| `pc/.../Symbols/*.svg` | `pc/Symbols/*.svg` |
| `pc/.../ColorProfiles/{colorProfile.xml, svgStyle.css}` | `pc/ColorProfiles/{colorProfile.xml, svgStyle.css}` |
| `pc/.../Fonts/*.ttf` | `pc/Fonts/*.ttf` |

After flattening: 1 FC, 1 PC manifest, 65 XSLT rules, 237 SVG symbols,
1 ColorProfile + 1 svgStyle.css, 4 fonts (Droid Sans / Open Sans).

The S-201 PC ships a single Day-only color profile; no Dusk/Night
variants are provided upstream.

## Layout

```
content/S201/
├── README.md                     ← this file
├── fc/
│   └── FeatureCatalogue.xml      ← S100FC/5.0 catalogue
└── pc/
    ├── portrayal_catalogue.xml   ← rule manifest
    ├── Rules/                    ← XSLT (65 files)
    ├── Symbols/                  ← SVG (237 files)
    ├── ColorProfiles/            ← Day-only colour profile + svgStyle.css
    └── Fonts/                    ← Open Sans / Droid Sans TTF
```

## License

S-201 specification assets are © IALA and used in accordance with the
upstream repository's open-publication terms. See
<https://github.com/IALA-IGO/S-201_AtoN-Information>.
