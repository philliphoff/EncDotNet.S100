# What's new

## Why it matters

This page tracks notable documentation and usability improvements so readers can quickly spot what's changed.

## Quick win

Current highlights:

- New **Start here** page with audience-based paths
- New scenario guides for S-102 rendering, S-124 inspection, and S-101+S-102 composition
- New curated **Top APIs** page
- Refreshed docs navigation and visual styling

## Deep dive

## 2026

### September 2026

- Added **S-401 (IEHG inland ENC)** as a first-class product: bundled IEHG
  Feature and Portrayal Catalogues, content-based detection that tells S-401,
  S-101, and legacy S-57 apart inside the shared `.000` extension, and
  render / identify / query / describe support. No validation rule pack ships
  yet — `s100 validate` reports "no rules available" for S-401.
- **S-57 inland ENCs** (cells declaring `DSID`/`PRSP` = 10, such as USACE
  river charts) are now portrayed with the S-401 catalogues while keeping their
  S-57 identity. Large inland cells that previously could not be opened now
  load, thanks to the ISO 8211 reader fix in `EncDotNet.Iso8211` 0.6.1. Inland
  object classes are not mapped yet.

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
