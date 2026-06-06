# EncDotNet.S100.Cli (`s100`)

A small, cross-platform command-line tool that renders any supported S-100
dataset to a PNG image by running the dataset's portrayal pipeline through the
Mapsui-free Skia *headless* renderer. It is intended as the basis for batch
scripts (for example, generating previews of sea-ice or surface-current
datasets).

The command name is **`s100`**.

## Commands

### `s100 render <dataset> <output>`

Detects the product specification of `<dataset>`, runs its portrayal pipeline,
and writes a PNG to `<output>`.

| Option | Default | Description |
|---|---|---|
| `-w`, `--width` | `1024` | Output image width in pixels. |
| `-h`, `--height` | `768` | Output image height in pixels. |
| `--palette` | `day` | Colour palette: `day`, `dusk`, or `night`. |
| `--symbol-scale` | `1.0` | Symbol scale factor. |
| `--text-scale` | `1.0` | Text scale factor. |
| `--time-step <index>` | `0` | Zero-based time-step index for time-series datasets (S-104 / S-111). |
| `--background <hex>` | opaque white | Background colour, `#RRGGBB` or `#AARRGGBB`. |
| `--debug` | off | Print full stack traces on error. |

```bash
s100 render currents.h5 currents.png --time-step 6 --palette night
s100 render warnings.gml warnings.png --width 2048 --height 1536
```

### `s100 info <dataset>`

Prints the detected specification, edition, whether the dataset supports
headless rendering, and — for time-series datasets — the available time steps
with their indices (use the index with `render --time-step`).

### `s100 list-specs`

Lists the supported product specifications and whether each supports the
headless render path.

## Supported specifications

| Family | Specs | Path |
|---|---|---|
| Vector (ISO 8211) | S-101 | `HeadlessVectorRenderer` |
| Vector (GML) | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421 | `HeadlessVectorRenderer` |
| Coverage (HDF5) | S-102, S-104 (gridded), S-111 (gridded) | `CoverageHeadlessRenderer` |

## Limitations (v1)

- **PNG output only.**
- **Vector pattern area-fills are omitted** on the headless path; points,
  lines, solid-area fills, and text render. (This is a limitation of
  `HeadlessVectorRenderer`, not the CLI.)
- **Coverage fixed-station datasets are not supported.** S-104 / S-111 datasets
  using data coding format 3 or 8 (time series at fixed stations) emit point
  glyphs through the Mapsui path only; the CLI reports a clear "not supported"
  error.
- **S-57 is not supported** (no headless path).
- **Mapsui is linked but not used to render.** The CLI transitively references
  `EncDotNet.S100.Renderers.Mapsui` for the spec-detection factory and the
  `ProjNetCrsTransformFactory`, but no Mapsui rendering runs on the headless
  path. Decoupling the factory and CRS transform from the Mapsui assembly is
  tracked as follow-up work.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Unhandled error (use `--debug` for a stack trace). |
| `2` | Product specification could not be detected. |
| `3` | The detected spec does not support headless rendering. |
| `4` | The dataset is recognised but its shape is unsupported (e.g. fixed-station coverage). |
| non-zero | Argument validation failure (missing file, bad palette, etc.). |
