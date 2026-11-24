Use `s100` for non-interactive inspection, validation, querying, conversion,
and portrayal of nautical datasets. Prefer machine-readable output when a
command offers it, and treat paths as local filesystem paths.

## Choosing a command

| Goal | Command |
|---|---|
| Discover supported product specifications | `s100 list-specs` |
| Discover live data sources for `--source` and their options | `s100 sources` |
| Detect a dataset's product and available time steps | `s100 info` |
| Validate a dataset or verify an exchange set | `s100 validate` |
| Query features or coverage values at a position | `s100 identify` |
| Check an RTZ or S-421 route against datasets (land, depths, dangers, areas, S-124 warnings, UKC) | `s100 route check` |
| Find tide stations near a point | `s100 tides stations` |
| Predict the tide at a station (series, highs and lows, CSV, JSON or S-104) | `s100 tides predict` |
| Download or check the offline tide station catalogue (one file from the mirror; `--from noaa` for NOAA itself) | `s100 tides update` |
| Prepare a tide station catalogue and manifest for a mirror | `s100 tides publish` |
| Render one dataset, multiple layers, or an exchange set | `s100 render` |
| Write XYZ raster tiles (folder, PMTiles, MBTiles or GeoPackage) or one GeoTIFF for a web map or GIS | `s100 tiles export` |
| Serve a built tile set, or datasets rendered on demand, to a local web map on XYZ URLs or to a GIS as WMTS or WMS | `s100 tiles serve` |
| Convert an S-57 base cell to S-101 (or an inland ENC cell to S-401) | `s100 s57 convert` |
| Convert an S-101 dataset to an S-57 cell or exchange set for testing or S-57-only tools (not a production ENC) | `s100 s101 convert --to s57` |
| Serve the read-only query tools to an MCP client over stdio | `s100 mcp serve` |
| Publish datasets to other machines' viewers over HTTP | `s100 feed serve` |
| Write datasets as a static feed for any web host | `s100 feed export` |

## General operating guidance

- Run `info` before `render` when the product, edition, display modes, or
  time-step indices are unknown.
- Use `--format json` for automation where supported. Diagnostics and update
  notices are written to standard error so standard output remains parseable.
- Redirected text output wraps at the console's width; when none is reported
  it uses `COLUMNS` if set to a positive number, and 80 columns otherwise.
- A dataset path may be an HDF5 coverage (`.h5`), ISO 8211 cell (`.000`), or
  GML feature dataset (`.gml`). Commands that accept exchange sets also accept
  a directory, `CATALOG.XML`, or a ZIP whose root contains `CATALOG.XML`.
- Coordinates are WGS 84 longitude/latitude unless an option explicitly says
  otherwise. `identify` takes separate latitude and longitude options;
  `render --bbox`, `tiles export --bbox` and `--center` use longitude before
  latitude.
- Every command that takes repeated `--layer` also takes `--layers-from
  <FILE>`: a UTF-8 list of dataset paths, one per line, with blank lines and
  `#` lines skipped and relative paths resolved against the list's folder. The
  listed datasets follow any `--layer` values and are treated exactly like
  them. Use it when there are more datasets than fit on the command line
  (Windows stops at 32,767 characters). A list is read once, at start.
- S-101 sibling sequential updates (`.001`, `.002`, and so on) are normally
  discovered beside a base `.000` cell. Read each command's guidance because
  composite rendering deliberately handles updates differently.
- Do not infer that exit code `0` from `validate` means rules were available.
  Its text or JSON result distinguishes “no rules available” from a conformant
  result.

## Global flags

| Flag | Purpose |
|---|---|
| `--help` | Show progressive human-oriented help. |
| `--version` | Print the informational version. |
| `--skill` | Print this complete agent-oriented Markdown document. |

`--skill` is a top-level standalone invocation. It performs no dataset access
and no network update check.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Unexpected processing or I/O error; for `health`, the server didn't answer `200 OK`. |
| `2` | Invalid input, unresolved dataset, or undetected product. |
| `3` | Render output or viewport could not be produced. |
| `4` | Recognized operation or dataset capability is not supported. |
| `5` | Dataset schema is non-conforming. |
| `6` | Validation findings failed the selected validation policy. |
| `7` | `route check` findings reached the `--fail-on` level. |
| `8` | `tides update --check` found newer data (from NOAA or the mirror) than the catalogue. |
| `9` | `tides publish` refused a catalogue that failed its publish check. |
