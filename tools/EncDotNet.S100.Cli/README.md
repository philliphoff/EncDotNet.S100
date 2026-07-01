# EncDotNet.S100.Cli (`s100`)

A small, cross-platform command-line tool for working with S-100 datasets. Its
primary command renders any supported dataset — or a composite of several,
via repeated `--layer` — to a PNG, JPEG, or WebP image by
running the dataset's portrayal pipeline through the Mapsui-free Skia *headless*
it can also report a dataset's product specification (`info`) and validate a
dataset against its specification's normative rule pack (`validate`). It is
intended as the basis for batch scripts (for example, generating previews of
sea-ice or surface-current datasets, or gating a data pipeline on validation).

The command name is **`s100`**.

## Distribution

The `s100` CLI is distributed two ways, both produced by CI on each release:

### Standalone download (recommended)

Each GitHub Release attaches a **self-contained, per-platform archive** that
bundles the .NET runtime and SkiaSharp's native libraries, so **no .NET
installation is required**:

| Platform | Asset | Extract & run |
|---|---|---|
| Windows x64 | `s100-<version>-win-x64.zip` | `s100.exe render …` |
| Windows arm64 | `s100-<version>-win-arm64.zip` | `s100.exe render …` |
| macOS (Apple silicon) | `s100-<version>-osx-arm64.tar.gz` | `./s100 render …` |
| Linux x64 | `s100-<version>-linux-x64.tar.gz` | `./s100 render …` |
| Linux arm64 | `s100-<version>-linux-arm64.tar.gz` | `./s100 render …` |

The macOS archive is code-signed and notarized; Gatekeeper verifies the
notarization online on first run. If macOS still quarantines the extracted
binary (for example when copied between machines), clear the attribute with:

```bash
xattr -d com.apple.quarantine ./s100
```

#### Linux runtime dependencies

The self-contained archive bundles the .NET runtime and SkiaSharp's native
libraries, but it does **not** bundle the handful of system shared libraries
those components load from the OS. Minimal/server and container base images
(for example `ubuntu`, `debian`, `mcr.microsoft.com/dotnet/runtime-deps`) often
omit them, so install them before running `s100`:

| Package | Required on | Why |
|---|---|---|
| `libicu` (`libicu74` on Ubuntu 24.04, `libicu72` on Debian 12) | **all** Linux | .NET globalization. Without it **every** command aborts at startup with a `Couldn't find a valid ICU package` failure. |
| `fontconfig` + a font package (e.g. `fonts-dejavu-core`) | optional | The bundled `libSkiaSharp.so` is the self-contained `SkiaSharp.NativeAssets.Linux.NoDependencies` build (issue #23), so `render` loads and draws text **without** fontconfig: labels fall back to a font embedded in the renderer. Installing fontconfig and a system font package only changes *which* font labels use (the discovered system font instead of the embedded fallback) and silences the `Fontconfig error: Cannot load default config file` warning. |

> The shipped native library is now the same self-contained
> `NoDependencies` build on both `linux-x64` and `linux-arm64`. Earlier
> releases bundled the regular native, which hard-linked `libfontconfig.so.1`
> on x64 (so `render` failed to load without it) and aborted on arm64 with
> `undefined symbol: uuid_parse` once fontconfig was present (issue #221).

Debian/Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y libicu74          # required
sudo apt-get install -y fontconfig fonts-dejavu-core   # optional: system label fonts
# Debian 12: use libicu72 instead of libicu74
```

`s100 list-specs`, `s100 info`, and `s100 validate` need only `libicu`;
`s100 render` (the Skia path) also needs only `libicu` — text renders via an
embedded fallback font even with no fontconfig or system fonts installed.

### Inside the Viewer application bundle

The `s100` executable also ships **inside the S-100 Viewer application bundle**
so a single viewer download provides both the GUI and the command-line tool. CI
publishes it self-contained (per platform RID) into a `cli/` subfolder of the
viewer's publish output:

| Platform | Location of `s100` |
|---|---|
| macOS | `EncDotNet.S100.Viewer.app/Contents/MacOS/cli/s100` (code-signed, notarized, and hardened-runtime alongside the viewer) |
| Windows | `cli/s100.exe` next to the published viewer |
| Linux | `cli/s100` next to the published viewer |

On macOS the bundled `s100` binary and its native libraries are signed by the
same step that signs the viewer, so it runs without Gatekeeper prompts.

## Commands

### `s100 render <dataset> <output>` (single dataset)
### `s100 render --layer <dataset> … <output>` (composite)

Detects the product specification of each input, runs its portrayal pipeline,
and writes an image. Two grammars are supported:

- **Single dataset** — `s100 render <dataset> <output>` renders one dataset.
- **Composite** — `s100 render --layer A --layer B … <output>` stacks several
  products into one image via the renderer-neutral S-98 interoperability
  engine. `--layer` is repeatable; the output path is either the trailing
  positional argument or `-o|--output`.

The output format (PNG, JPEG, or WebP) is inferred from the file extension
unless `--format` is given.

| Option | Default | Description |
|---|---|---|
| `--layer <path>` | _none_ | Add a dataset as a composite layer (repeatable). When any `--layer` is given, the composite grammar is used. |
| `-o`, `--output <path>` | _positional_ | Output image path. Required (or given positionally) for the composite form; an alternative to the positional `<output>` for the single form. |
| `--bbox <minLon,minLat,maxLon,maxLat>` | union auto-fit | **Composite only.** Explicit shared viewport as a WGS-84 bounding box (e.g. `--bbox -1.5,50.0,-1.0,50.5`). Mutually exclusive with `--center`/`--scale`. |
| `--center <lon,lat>` | union auto-fit | **Composite only.** Explicit shared viewport centre. Must be used with `--scale`. |
| `--scale <denominator>` | union auto-fit | **Composite only.** Explicit shared viewport scale denominator (e.g. `--scale 50000` for 1:50 000). Must be used with `--center`. |
| `-w`, `--width` | `1024` | Output image width in pixels. |
| `-h`, `--height` | `768` | Output image height in pixels. |
| `--palette` | `day` | Colour palette: `day`, `dusk`, or `night`. |
| `--symbol-scale` | `1.0` | Symbol scale factor. |
| `--text-scale` | `1.0` | Text scale factor. |
| `--time-step <index>` | `0` | Zero-based time-step index for time-series datasets (S-104 / S-111). |
| `--background <hex>` | opaque white | Background colour, `#RRGGBB` or `#AARRGGBB`. |
| `--format <fmt>` | inferred from extension, else `png` | Output image format: `png`, `jpeg` (`jpg`), or `webp`. When omitted, the format is inferred from the output file extension; an unrecognised extension falls back to `png`. An explicit `--format` that conflicts with a recognised output extension is rejected. |
| `--quality <1-100>` | `90` | Encoder quality for lossy formats (`jpeg`, `webp`). Ignored for `png`. |
| `--no-text` | off | Suppress text/label drawing instructions. Shorthand for `--hide text`. In the composite form the suppression is global (applies to every layer). |
| `--hide <list>` | _none_ | Comma-separated list of drawing-instruction categories to suppress: `text`, `points`, `lines`, `areas` (e.g. `--hide text,points`). Combines additively with `--no-text`. In the composite form the suppression is global. Useful for label-dense products such as S-411 sea-ice, where the egg-code text overlaps fills at preview scales — `--no-text` yields a BSIS-style "clean fill" preview. |
| `--no-updates` | off | **Single form only.** Do not apply S-101 sequential updates. By default, when the dataset is an S-101 base cell (`….000`), any sibling update files (`….001`, `….002`, …) in the same directory are applied best-effort before rendering so the cell is drawn at its up-to-date state (S-100 Part 10a). |
| `--basemap <mode>` | `none` | Draw a basemap **beneath** the chart data: `none` (default) or `offline`. `offline` composites the bundled Natural Earth 1:10m land layer (public domain) under all chart layers in the muted parchment tone `238,232,220`, projected with the chart's own Web-Mercator viewport so it registers exactly. Works in both the single-dataset and `--layer` composite forms. Online tile basemaps (e.g. OSM) are **not** available in the headless renderer — only the offline land layer. |
| `--debug` | off | Print full stack traces on error. |

```bash
s100 render currents.h5 currents.png --time-step 6 --palette night
s100 render warnings.gml warnings.png --width 2048 --height 1536
s100 render seaice.gml seaice.png --no-text                # clean fill preview
s100 render seaice.gml seaice.png --basemap offline        # land under the chart
s100 render chart.gml chart.png --hide text,points         # hide text + symbols
s100 render NL4NZ110.000 cell.png                          # applies .001/.002/… updates
s100 render NL4NZ110.000 base.png --no-updates             # render the base cell only
s100 render warnings.gml warnings.jpg --quality 85         # JPEG (format inferred from .jpg)
s100 render warnings.gml preview.webp                      # WebP preview

# Composite several products into one chart:
s100 render --layer enc.000 --layer bathy.h5 --layer warnings.gml chart.png
s100 render --layer enc.000 --layer bathy.h5 -o chart.png --bbox -1.5,50.0,-1.0,50.5
s100 render --layer enc.000 --layer bathy.h5 chart.png --center -1.25,50.25 --scale 50000
s100 render --layer enc.000 --layer warnings.gml chart.png --basemap offline
```

> **Composite ordering.** The S-98 authority orders layers by display plane,
> so the order in which you pass `--layer` is only a **within-plane tiebreak** —
> hand-ordering layers generally has no visible effect. Explicitly ordering an
> S-102 bathymetry surface above an S-101 chart, for example, is unnecessary:
> the plane assignment already places it correctly.
>
> **Composite viewport.** When no `--bbox` / `--center`+`--scale` is given the
> compositor auto-fits the union extent of all layers to the requested
> `--width` × `--height`.
>
> **Composite and S-101 updates.** The composite form does **not** apply S-101
> sequential/sibling updates — `--no-updates` applies to the single-dataset
> form only. Render an S-101 cell singly if you need its updates folded in.

> **S-101 sequential updates.** When pointed at an S-101 base cell
> (`….000`), the single-dataset `render` and `info` discover sibling update
> files (`….001`, `….002`, …) in the same directory and apply them in order
> before processing the cell, mirroring how an exchange set is loaded
> in the viewer. Application is best-effort: a missing, out-of-order, or
> unreadable update is reported but never blocks the command. Pass
> `--no-updates` to operate on the base cell exactly as named.

### `s100 info <dataset>`

Prints the detected specification, edition, whether the dataset supports
headless rendering, and — for time-series datasets — the available time steps
with their indices (use the index with `render --time-step`). For an S-101
base cell, sibling sequential updates are applied first (see `--no-updates`)
so the reported model reflects the up-to-date cell.

### `s100 validate <dataset>`

Detects the product specification of `<dataset>` and runs that spec's normative
validation rule pack against the parsed dataset, reporting the findings. Each
finding carries a spec-traceable rule id (e.g. `S127-R-2.1`), a severity, a
message, and — where the rule can locate the problem — a feature id or
geographic position.

Validation is a pure function of the parsed dataset: it does not depend on the
palette, opacity, or selected time step. Specs without a rule pack today —
S-101, S-201, and S-57 — report *no rules available* and exit successfully;
this is distinct from a dataset that was evaluated and found conformant.

| Option | Default | Description |
|---|---|---|
| `--format <fmt>` | `text` | Output format: `text` (a findings table) or `json` (machine-readable, for CI). |
| `--suppress <list>` | _none_ | Comma-separated list of rule ids (or `*` glob patterns) whose findings are dropped from the report **and** ignored by the exit code — e.g. `--suppress S101-R-1.2,S101-R-3.2` or `--suppress "S101-*"`. Compiler-style "no-warn": mute a known rule class (such as a feature-catalogue-version mismatch) to surface the more-likely-real findings. The count of suppressed findings is still reported. |
| `--strict` | off | Treat warnings as failures: exit `6` when any warning (not just an error) is present. |
| `--debug` | off | Print full stack traces on error. |

```bash
s100 validate warnings.gml                 # human-readable findings table
s100 validate route.gml --strict           # fail the build on warnings too
s100 validate currents.h5 --format json    # machine-readable report for CI
s100 validate chart.000 --suppress S101-R-1.2,S101-R-3.2   # mute a rule class
s100 validate chart.000 --suppress "S101-*"                # glob: mute a whole spec's rules
```

The `--suppress` patterns are matched case-insensitively against each finding's
rule id; `*` is the only wildcard and matches any run of characters, so a
pattern with no `*` is an exact rule-id match. Suppressed findings are removed
from the table/JSON and do not contribute to the exit code, but the trailing
summary still reports how many were suppressed.

By default the command exits `0` when no **error**-severity findings remain
after suppression (warnings and info are reported but do not fail); pass
`--strict` to also fail on warnings. Exit code `6` signals failing findings.

### `s100 list-specs`

Lists the supported product specifications and whether each supports the
headless render path.

## Supported specifications

| Family | Specs | Path |
|---|---|---|
| Vector (ISO 8211) | S-101, S-57 (translated to S-101) | `HeadlessVectorRenderer` |
| Vector (GML) | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421 | `HeadlessVectorRenderer` |
| Coverage (HDF5) | S-102, S-104 (gridded), S-111 (gridded) | `CoverageHeadlessRenderer` |

## Limitations

- **Vector pattern area-fills are omitted** on the headless path; points,
  lines, solid-area fills, and text render.
- **Coverage fixed-station datasets are not supported.** S-104 / S-111 datasets
  using data coding format 3 or 8 (time series at fixed stations) emit point
  glyphs through the Mapsui path only; the CLI reports a clear "not supported"
  error.
- **S-57 renders through the S-101 pipeline.** Datasets are translated to
  `S101Document` in-memory and rasterised with S-101 symbology (not S-52).

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Unhandled error (use `--debug` for a stack trace). |
| `2` | Product specification could not be detected. |
| `3` | The detected spec does not support headless rendering. |
| `4` | The dataset is recognised but its shape or encoding is unsupported — e.g. a fixed-station coverage, or a data coding format the reader does not yet implement (such as dcf1, irregular time series at fixed stations). |
| `5` | The dataset is recognised but non-conforming (a required attribute, dataset, or group is missing or malformed). |
| `6` | `validate` only: the dataset was evaluated and produced failing findings (any error-severity finding, or — with `--strict` — any warning). |
| non-zero | Argument validation failure (missing file, bad palette, etc.). |
