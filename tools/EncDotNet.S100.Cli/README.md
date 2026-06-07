# EncDotNet.S100.Cli (`s100`)

A small, cross-platform command-line tool that renders any supported S-100
dataset to a PNG image by running the dataset's portrayal pipeline through the
Mapsui-free Skia *headless* renderer. It is intended as the basis for batch
scripts (for example, generating previews of sea-ice or surface-current
datasets).

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

`s100 list-specs` and `s100 info` need only `libicu`; `s100 render` (the Skia
path) also needs only `libicu` — text renders via an embedded fallback font even
with no fontconfig or system fonts installed.

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
| `--no-text` | off | Suppress text/label drawing instructions. Shorthand for `--hide text`. |
| `--hide <list>` | _none_ | Comma-separated list of drawing-instruction categories to suppress: `text`, `points`, `lines`, `areas` (e.g. `--hide text,points`). Combines additively with `--no-text`. Useful for label-dense products such as S-411 sea-ice, where the egg-code text overlaps fills at preview scales — `--no-text` yields a BSIS-style "clean fill" preview. |
| `--debug` | off | Print full stack traces on error. |

```bash
s100 render currents.h5 currents.png --time-step 6 --palette night
s100 render warnings.gml warnings.png --width 2048 --height 1536
s100 render seaice.gml seaice.png --no-text                # clean fill preview
s100 render chart.gml chart.png --hide text,points         # hide text + symbols
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
| Vector (ISO 8211) | S-101, S-57 (translated to S-101) | `HeadlessVectorRenderer` |
| Vector (GML) | S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421 | `HeadlessVectorRenderer` |
| Coverage (HDF5) | S-102, S-104 (gridded), S-111 (gridded) | `CoverageHeadlessRenderer` |

## Limitations

- **PNG output only.**
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
| `4` | The dataset is recognised but its shape is unsupported (e.g. fixed-station coverage). |
| non-zero | Argument validation failure (missing file, bad palette, etc.). |
