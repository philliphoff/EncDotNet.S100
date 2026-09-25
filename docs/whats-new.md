# What's new

## Why it matters

This page tracks notable documentation and usability improvements so readers can quickly spot what's changed.

## Quick win

Current highlights:

- New **Start here** page with audience-based paths
- New scenario guides for S-102 rendering, S-124 inspection, and S-101+S-102 composition
- New curated **Top APIs** page
- New **developer guides**: loading datasets, protected exchange sets, product data, and custom catalogues and validation
- Refreshed docs navigation and visual styling

## Deep dive

## 2026

### September 2026

- **Developer guides** for the library:
  [Loading datasets](loading-datasets.md),
  [Reading protected exchange sets](protected-exchange-sets.md),
  [Reading product data](reading-product-data.md) and
  [Custom catalogues and validation](catalogues-and-validation.md). Every code
  sample in them was run against the repository's test data.
- The facade gained `S100Dataset.OpenAsync(IAssetSource, …)`, `S100ExchangeSet`
  (folders, `CATALOG.XML` or ZIPs, with S-101 updates applied), and the
  `WithDecryption` and `Validate` extensions.
- Every public API in the NuGet packages now has XML documentation, and the
  packages ship it for IntelliSense.
- The docs site now covers all 29 packages in the API reference, publishes the
  package READMEs under **Packages**, and is built on every pull request.

- Added **S-401 (IEHG inland ENC)** as a first-class product: bundled IEHG
  Feature and Portrayal Catalogues, content-based detection that tells S-401,
  S-101, and legacy S-57 apart inside the shared `.000` extension, and
  render / identify / query / describe support. No validation rule pack ships
  yet — `s100 validate` reports "no rules available" for S-401.
- **S-57 inland ENCs** (cells declaring `DSID`/`PRSP` = 10, such as USACE
  river charts) are now portrayed with the S-401 catalogues while keeping their
  S-57 identity. Large inland cells that previously could not be opened now
  load, thanks to the ISO 8211 reader fix in `EncDotNet.Iso8211` 0.6.1. The
  inland object classes and attributes of the IENC Feature Catalogue 2.4 are
  translated to S-401, so inland bridges, distance marks, notice marks, and
  waterway gauges now appear.
- `s100 s57 convert` writes an inland ENC cell as an **S-401** dataset, so its
  inland features are no longer dropped. `--target s101|s401` overrides the
  choice, and the summary and `--report` JSON name the product written.

### July 2026

- Introduced audience-path onboarding and scenario-first docs flow.
- Added callout-heavy troubleshooting guidance in main guide pages.
- Added architecture and workflow diagrams.
- Added themed docs styling for stronger brand identity.
- Added a **Bringing S-57 into the pipeline** guide covering convert / view /
  validate, sibling-update folding, and the `s100 s57 convert` diagnostics
  summary (`--report`).

## Troubleshooting

> [!NOTE]
> For code-level release notes and package version changes, continue to use GitHub Releases.

## Next step

- [Start here](start-here.md)
- [Documentation index](index.md)
- [Releases](https://github.com/philliphoff/EncDotNet.S100/releases)
