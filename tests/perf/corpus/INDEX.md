# Performance Corpus Inventory

This file documents every dataset consumed by the
[PerfRunner](../../../tools/EncDotNet.S100.PerfRunner/) scenarios.

All entries listed here are **synthetic fixtures bundled in the
repository** under `tests/datasets/`. No external downloads are
required for the default (synthetic-only) baseline run.

For future large or license-restricted datasets, the companion
[`fetch-corpus.sh`](../../../tools/EncDotNet.S100.PerfRunner/scripts/fetch-corpus.sh)
script can download assets listed in
[`corpus.json`](corpus.json) into a local cache.

## Corpus entries

### `s101/AA-exchange-set`

| Field | Value |
|-------|-------|
| Path | `tests/datasets/S101/S-101/DATASET_FILES/*.000` |
| Source | Synthetic — bundled in repository |
| License | MIT (project license) |
| Size | ~600 KB (19 ISO 8211 files) |
| Scenarios | `s101-portray-cold`, `s101-portray-warm`, `s101-render-warm` |

S-101 Electronic Navigational Chart fixture. Contains 19 synthetic
ISO 8211 dataset files exercising the Lua portrayal pipeline.

### `s102/102US004MI1CI262227`

| Field | Value |
|-------|-------|
| Path | `tests/datasets/S102/102US004MI1CI262227.h5` |
| Source | Synthetic — bundled in repository |
| License | MIT (project license) |
| SHA-256 | `42d652a4d9c8454e79772e09b23ef08680fa8b41e34e176b24509d5dff92095d` |
| Size | 30 KB |
| Scenarios | `s102-coverage` |

S-102 Bathymetric Surface HDF5 fixture. Small synthetic depth grid
exercising the coverage pipeline and Skia/Mapsui renderers.

### `s124/navwarn-fixtures`

| Field | Value |
|-------|-------|
| Path | `tests/datasets/S124/*.gml` |
| Source | Synthetic — bundled in repository |
| License | MIT (project license) |
| Size | ~13 KB (4 GML files) |
| Scenarios | `s124-vector` |

S-124 Navigational Warnings GML fixtures. The scenario uses the
first `.gml` file found; the set includes point, curve, surface,
and mixed geometry variants to exercise the XSLT vector pipeline.

### `s124/navwarn-fixtures` — individual files

| File | SHA-256 | Size |
|------|---------|------|
| `navwarn_curve.gml` | `de2062ac…` | 2.4 KB |
| `navwarn_mixed.gml` | `62d5520b…` | 4.3 KB |
| `navwarn_point.gml` | `e86b8d28…` | 2.4 KB |
| `navwarn_surface.gml` | `70be10f9…` | 3.6 KB |

### `exchange-sets/synthetic-mixed`

| Field | Value |
|-------|-------|
| Path | `tests/datasets/ExchangeSets/Synthetic-Mixed/` |
| Source | Synthetic — bundled in repository |
| License | MIT (project license) |
| Size | ~4 KB (`CATALOG.XML`) |
| Scenarios | `exchange-set-open` |

Multi-product synthetic exchange set. Contains a `CATALOG.XML` that
references S-101, S-102, and S-124 datasets from sibling directories.
The `exchange-set-open` scenario opens the exchange set, walks all
referenced datasets, and drives each through its pipeline.

## Adding external corpus entries

To add a dataset that cannot be committed to the repository:

1. Add an entry to `corpus.json` with a non-null `url` field and the
   expected `sha256`.
2. Run `fetch-corpus.sh` to download it into the cache.
3. Set `ENC_DOTNET_PERF_CORPUS` to the cache directory before running
   scenarios that reference the external asset.
4. Update the scenario code to check `ENC_DOTNET_PERF_CORPUS` for
   the asset when the bundled path is not found.
