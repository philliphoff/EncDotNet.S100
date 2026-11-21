# S-412 bundled catalogues

This folder holds the S-412 Marine Weather Warnings feature catalogue and
portrayal catalogue, copied from the IHO's public working repository for the
product specification.

S-412 is a WMO product in the S-411 to S-420 range. NOAA's Ocean Prediction
Center leads its development. It isn't approved in the IHO GI Registry yet: the
product specification, feature catalogue and portrayal catalogue are all
drafts, and WMO targets operational use for Phase 2 (2029). Expect the
catalogues to change before release.

## Attribution

- Feature catalogue and portrayal catalogue © International Hydrographic
  Organization (IHO) and World Meteorological Organization (WMO), from
  <https://github.com/iho-ohi/S-412-Product-Specification>.
- The upstream repository has no licence file. These files are redistributed
  unmodified, apart from the changes in [Changes](#changes), with this
  attribution so that S-412 datasets can be portrayed with the bundled
  catalogues.

## Source

| Item | Value |
|---|---|
| Repository | <https://github.com/iho-ohi/S-412-Product-Specification> |
| Commit | `1e4fe558984b3b5492a6ad13abf2e6fa588379f2` (2026-04-02) |
| Feature catalogue upstream file | `FC/S-412-1.1.0_v009.xml` |
| Feature catalogue edition | `productId="S-412"`, `versionNumber="1.1.0"`, `versionDate="2026-01-09"` (draft v009) |
| Portrayal catalogue upstream folder | `PC/S-412 Portrayal Catalogue/` |
| Portrayal catalogue edition | `portrayalCatalog productId="S-412" version="1.0"` |
| Product specification | Edition 1.3.0 draft, February 2026 (`PS/`) |
| Date copied | 2026-11-07 |

The repository also holds `PC/S-412_FC.xml`, an older draft (v006) of the same
feature catalogue edition. The bundled copy is the newer `FC/` one; both define
the same feature and information types.

## Changes

| Upstream path | Bundled path |
|---|---|
| `FC/S-412-1.1.0_v009.xml` | `fc/FeatureCatalogue.xml` |
| `PC/S-412 Portrayal Catalogue/` | `pc/` (contents only, without the wrapper folder) |
| `PC/S-412 Portrayal Catalogue/Fonts/` | Not bundled |

The four fonts (Open Sans and Droid Sans) aren't bundled: the portrayal
catalogue's `<fonts/>` list is empty, so nothing refers to them.

Line endings are normalized to LF, as in the other bundled catalogues; 10
upstream files use CRLF. File contents are otherwise identical to upstream.
Don't edit them here.

## Layout

```text
S412/
├── README.md                     ← this file
├── fc/
│   └── FeatureCatalogue.xml      ← S100FC/5.2; 6 feature types (1 abstract), 1 information type
└── pc/
    ├── portrayal_catalogue.xml   ← rule and asset manifest
    ├── Rules/                    ← XSLT (10 files)
    └── ColorProfiles/            ← colorProfile.xml (Day only) and svgStyle.css
```

## Known upstream gaps

At the commit above:

- `main.xsl` includes `SignificantWaveHeightWarning.xsl`, and the manifest
  lists it, but the upstream file is named `WaveHeightWarning.xsl`.
- `WaveHeightWarning.xsl` matches `WaveHeightWarning` features by
  `waveHeightWarningThreshold`, but the feature catalogue names them
  `SignificantWaveHeightWarning` and `significantWaveHeightWarningThreshold`.
- The wave height rules cover thresholds 1 to 4. The feature catalogue also
  lists 5 (greater than fourteen metres), which falls back to the default
  rule.
- The fallback rule `_Default_COMMON.xsl` refers to the `QUESMRK1` symbol and
  the `CHMGD` colour, but the catalogue has no symbols and its colour profile
  has no `CHMGD`.
- The colour profile has a Day palette only, so Dusk and Night use the Day
  colours.
- The rules put line work on `UNDERRADAR` and fills on `OVERRADAR`. The
  library places S-412 on the S-98 cautions and warnings plane instead, as it
  does S-124.

## Host-side corrections

Rather than edit the files here, `S412PortrayalCatalogue` in
`EncDotNet.S100.Datasets.S412` corrects the significant wave height rule at
load time:

| Rule | Defect | Correction |
|---|---|---|
| `SignificantWaveHeightWarning.xsl` | Missing; the upstream file is `WaveHeightWarning.xsl`, which matches the wrong feature and attribute names | Served from `WaveHeightWarning.xsl`, with `WaveHeightWarning` and `waveHeightWarningThreshold` renamed to the feature catalogue's names |

The correction does nothing once the catalogue has a
`SignificantWaveHeightWarning.xsl` of its own, and
`S412PortrayalCatalogueTests` fails when a refreshed catalogue fixes the defect,
as the cue to delete it.

Refresh these files when the IHO and WMO publish an approved S-412 edition.
