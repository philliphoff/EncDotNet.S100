# Command-line rendering (`s100`)

`EncDotNet.S100.Cli` is a cross-platform .NET console tool, invoked as **`s100`**,
that renders any supported S-100 dataset — or a **composite** of several — to a
PNG, JPEG, or WebP image, and
**validates** datasets and exchange sets against the normative rule packs. It
drives the same portrayal and validation pipelines as the Avalonia viewer, but
rasterises through the Mapsui-free Skia *headless* renderers so it can run
without a UI — making it suitable for batch scripts (e.g. generating sea-ice or
surface-current previews) and CI conformance checks.

It offers four subcommands:

| Command | Purpose |
|---|---|
| `s100 render <dataset> <output>`<br>`s100 render --layer … <output>`<br>`s100 render <exchange-set> <output>` | Render one dataset — or composite several with `--layer`, or a whole exchange set / directory — to an image (PNG, JPEG, or WebP). |
| `s100 validate <dataset-or-exchange-set>` | Validate against the spec's normative rule pack (plus exchange-set signature/checksum integrity), with compiler-style output and suppression. |
| `s100 info <dataset>` | Show the detected spec, edition, headless-render capability, and (for time-series datasets) the available time steps. |
| `s100 list-specs` | List the supported product specifications and which support headless rendering. |

## Why it matters

Use `s100` when you need repeatable, headless rendering and validation in local
automation or CI pipelines.

## Quick win

```bash
s100 render dataset.h5 out.png
```

Expected result: one rendered image (`out.png`) and zero UI dependencies.

## Install (standalone download)

Each [GitHub Release](https://github.com/philliphoff/EncDotNet.S100/releases)
attaches a **self-contained, per-platform archive** of `s100`. It bundles the
.NET runtime and SkiaSharp's native libraries, so **no .NET installation is
required** — just download, extract, and run.

| Platform | Asset | Run |
|---|---|---|
| Windows x64 | `s100-<version>-win-x64.zip` | `s100.exe render dataset.h5 out.png` |
| Windows arm64 | `s100-<version>-win-arm64.zip` | `s100.exe render dataset.h5 out.png` |
| macOS (Apple silicon) | `s100-<version>-osx-arm64.tar.gz` | `./s100 render dataset.h5 out.png` |
| Linux x64 | `s100-<version>-linux-x64.tar.gz` | `./s100 render dataset.h5 out.png` |
| Linux arm64 | `s100-<version>-linux-arm64.tar.gz` | `./s100 render dataset.h5 out.png` |

```bash
# macOS / Linux
tar -xzf s100-<version>-<rid>.tar.gz
./s100 list-specs
```

The macOS archive is code-signed and notarized, so Gatekeeper verifies it
online on first launch. If a copied binary is still quarantined, clear the
attribute with `xattr -d com.apple.quarantine ./s100`. The Windows `s100.exe`
is Authenticode-signed via Azure Trusted Signing.

The same `s100` executable also ships inside the S-100 Viewer application
bundle (under `cli/`); see the project README for that layout.

## Quick start

```bash
# Render a dataset to a 1024x768 PNG (auto-detects the spec)
dotnet run --project tools/EncDotNet.S100.Cli -- render dataset.h5 out.png

# Inspect a time-series dataset and list its time steps
dotnet run --project tools/EncDotNet.S100.Cli -- info currents.h5

# Validate a dataset or exchange set against its normative rule pack
dotnet run --project tools/EncDotNet.S100.Cli -- validate exchange-set/

# Render the 7th time step at night-palette, larger canvas
dotnet run --project tools/EncDotNet.S100.Cli -- \
    render currents.h5 currents.png --time-step 6 --palette night -w 2048 -h 1536

# Composite several products into one chart (repeated --layer + output)
dotnet run --project tools/EncDotNet.S100.Cli -- \
    render --layer enc.000 --layer bathy.h5 --layer warnings.gml chart.png

# Composite an entire exchange set / directory (auto-detected)
dotnet run --project tools/EncDotNet.S100.Cli -- \
    render exchange-set/ chart.png
```

## How it works

The CLI detects a dataset's product specification from the file, runs that
spec's portrayal pipeline — the same pipeline the Avalonia viewer uses, wired to
the feature and portrayal catalogues bundled in the tool — and encodes the
result to the requested image format (PNG by default; JPEG or WebP via
`--format` or the output extension). Vector specs (S-101 and the GML products) and coverage specs
(S-102/104/111) each rasterise through their own headless Skia renderer; for
S-111 the current arrows are overlaid on the coverage. No UI or map projection
stack is involved, so it runs anywhere .NET does.

## Render options

| Option | Default | Description |
|---|---|---|
| `-w`, `--width` / `-h`, `--height` | `1024` × `768` | Output image size in pixels. |
| `--palette` | `day` | Colour palette: `day`, `dusk`, or `night`. |
| `--symbol-scale` / `--text-scale` | `1.0` | Symbol and text scale factors. |
| `--time-step <index>` | `0` | Zero-based time step for time-series datasets (S-104 / S-111). |
| `--background <hex>` | opaque white | Background colour, `#RRGGBB` or `#AARRGGBB`. |
| `--format <fmt>` | inferred from extension (else `png`) | Output image format: `png`, `jpeg` (`jpg`), or `webp`. |
| `--quality <1-100>` | `90` | Encoder quality for lossy formats (`jpeg`, `webp`). Ignored for `png`. |
| `--bbox <minLon,minLat,maxLon,maxLat>` | auto-fit | Explicit WGS-84 viewport for a single dataset or composite image. Mutually exclusive with `--center`/`--scale`. Gridded coverages sample only the intersecting region. |
| `--center <lon,lat>` + `--scale <denominator>` | auto-fit | Explicit viewport by centre and scale denominator. Gridded coverages sample only the intersecting region. |
| `--no-text` | off | Suppress text/label drawing instructions (shorthand for `--hide text`). |
| `--hide <list>` | _none_ | Suppress drawing-instruction categories — any of `text`, `points`, `lines`, `areas` — useful for clean fills on label-dense products such as S-411 sea-ice. |
| `--basemap <mode>` | `none` | Draw a basemap beneath the chart data: `none` (default) or `offline`. `offline` composites the bundled Natural Earth 1:10m land layer (public domain, parchment tone `238,232,220`) under all chart layers, projected with the chart's own viewport. Applies to both forms. Online tile basemaps are not available headlessly. |
| `--display-mode <mode>` | `ice-concentration` | **S-411 only.** Select the sea-ice portrayal display mode: `ice-concentration` (total concentration, default), `ice-sod` (stage of development) or `ice-navigational` (**provisional** preview derived from total concentration — **not** a POLARIS/RIO navigational-risk computation). A single dataset carries the full WMO egg code, so the same data renders in any mode. Supplying the option for a non-S-411 dataset is an error; `s100 info <dataset>` lists the modes a dataset supports. |

## Compositing multiple datasets

Pass a dataset per `--layer` (repeatable) to stack several products into one
image via the renderer-neutral **S-98 interoperability** engine. The output path
is the trailing positional argument or `-o|--output`:

```bash
s100 render --layer enc.000 --layer bathy.h5 --layer warnings.gml chart.png
s100 render --layer enc.000 --layer bathy.h5 -o chart.png --bbox -1.5,50.0,-1.0,50.5
s100 render --layer enc.000 --layer bathy.h5 chart.png --center -1.25,50.25 --scale 50000
```

The same viewport options apply to a single gridded S-102, S-104, or S-111
dataset:

```bash
s100 render bathy.h5 window.png --bbox -1.5,50.0,-1.0,50.5
```

Coverage viewport coordinates are WGS-84. For projected grids, the sampler
transforms the requested window into the dataset's native CRS before selecting
cells, then reprojects the sampled subset for output. A viewport that extends
beyond the dataset paints only the intersecting coverage; the remaining image
uses the selected background or basemap. Omitting viewport options preserves the
existing dataset auto-fit.

The `--palette`, `--symbol-scale`, `--text-scale`, `--time-step`,
`--background`, `--width`/`--height`, `--format`/`--quality`,
`--hide`/`--no-text`, and `--basemap` options apply as in the single-dataset
form; suppression (`--hide`/`--no-text`) is **global** — it applies to every
layer, and `--basemap offline` draws the shared land layer beneath all layers.

| Composite-only option | Default | Description |
|---|---|---|
| `--layer <path>` | _none_ | Add a dataset as a layer (repeatable). Any `--layer` selects the composite form. |
| `-o`, `--output <path>` | _positional_ | Output image path (alternative to the positional `<output>`). |
| `--bbox <minLon,minLat,maxLon,maxLat>` | union auto-fit | Explicit shared viewport as a WGS-84 bounding box. Mutually exclusive with `--center`/`--scale`. |
| `--center <lon,lat>` + `--scale <denominator>` | union auto-fit | Explicit shared viewport by centre + scale denominator (e.g. `--center -1.25,50.25 --scale 50000`). |

Two behaviours differ from the single-dataset form:

- **Ordering.** The S-98 authority orders layers by display plane, so the
  `--layer` order is only a **within-plane tiebreak** — hand-ordering layers
  generally has no visible effect (an S-102 surface is already placed above an
  S-101 chart by its plane).
- **S-101 updates.** The composite form does **not** apply S-101
  sequential/sibling updates; `--no-updates` applies to the single-dataset form
  only. Render an S-101 cell singly if you need its updates folded in.

When no `--bbox` / `--center`+`--scale` is supplied, the compositor auto-fits
the union extent of all layers to the requested `--width` × `--height`. The
auto-fit is **antimeridian-aware**: datasets whose geometry straddles the ±180°
seam (e.g. an S-411 Alaska product spanning ~175°E → ~225°E) are framed on their
true, narrow extent instead of collapsing to a near-global viewport (issue #413).
This applies to both single-dataset renders and `--layer` / exchange-set
composites.

### Compositing a whole exchange set / directory

Rather than listing every `--layer` by hand, point `render` at an **exchange
set** and composite everything discoverable in it (issue #407). The source may
be a directory containing a top-level `CATALOG.XML`, a `CATALOG.XML` file, or a
`.zip` archive whose root holds one — passed positionally (auto-detected) or via
`--exchange-set` / `--from`:

```bash
s100 render exchange-set/ chart.png                     # positional directory
s100 render exchange-set/CATALOG.XML chart.png          # positional catalogue
s100 render --exchange-set set.zip -o chart.png         # explicit, ZIP
s100 render --from set/ chart.png --only S101,S102      # restrict to some specs
```

The datasets are discovered with the same exchange-set reader the viewer and
`validate` use, then composited through the identical S-98 engine — so every
option above (`--bbox`/`--center`/`--scale`, palette, `--hide`/`--no-text`,
etc.) applies unchanged. `--exchange-set`/`--from` is mutually exclusive with
`--layer`.

| Exchange-set option | Default | Description |
|---|---|---|
| `--exchange-set`, `--from <path>` | _positional_ | The exchange set to composite (directory / `CATALOG.XML` / `.zip`). A directory / `CATALOG.XML` / `.zip` passed positionally is auto-detected. |
| `--only <specs>` | _all_ | Restrict compositing to a comma-separated list of product specifications (e.g. `--only S101,S128`; hyphenation and case are ignored). |

Discovery notes:

- **No S-101 updates.** Like the `--layer` form, the exchange-set form applies
  **no** S-101 sequential/sibling updates: only base and single cells are
  composited; update files (and orphan updates with no in-set base) are skipped.
- **Partial sets.** Datasets whose product specification is unsupported, whose
  file is missing, or that declare data protection (encryption — this CLI has no
  decryption keys) are **skipped with a warning on stderr** rather than failing
  the whole render. If nothing renderable remains, `render` exits non-zero.
- **ZIP archives** are extracted to a uniquely-named temporary directory (cleaned
  up after rendering, even on failure); a large exchange set therefore needs
  transient temporary disk space of roughly its uncompressed size.

## Capabilities

- **Headless rendering** for S-101; S-102; S-104 and S-111 *gridded* coverages;
  and the GML products S-122/124/125/127/128/129/131/201/411/421.
- **Output formats:** PNG, JPEG, and WebP. The format is inferred from the
  output file extension (or set explicitly with `--format`); `--quality`
  controls the encoder quality for the lossy formats.
- **Text and category suppression** via `--no-text` / `--hide` for cleaner
  previews of label-dense products.
- **Offline land basemap** via `--basemap offline` — composites the bundled
  Natural Earth 1:10m land layer beneath the chart data (headless; no online
  tiles).
- **Fixed-station coverage** (S-104 / S-111 data coding format 3 / 8) is not
  rendered headlessly; the CLI returns a descriptive error.
- **S-57 is not supported** by the headless path.

See the project README under `tools/EncDotNet.S100.Cli/README.md` for the full
option reference and exit codes.

## Troubleshooting

> [!WARNING]
> If rendering fails on S-104/S-111 fixed-station encodings, that's expected for
> headless mode; inspect with `s100 info` first.

> [!TIP]
> If output layering looks surprising in composite renders, remember S-98
> interoperability rules determine cross-product ordering.

## Next step

- [Scenario: Render S-102 to PNG](scenarios/render-s102-to-png.md)
- [Scenario: Compose S-101 + S-102](scenarios/compose-s101-s102.md)
- [Top APIs](top-apis.md)
