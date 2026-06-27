# Command-line rendering (`s100`)

`EncDotNet.S100.Cli` is a cross-platform .NET console tool, invoked as **`s100`**,
that renders any supported S-100 dataset to a PNG, JPEG, or WebP image, and
**validates** datasets and exchange sets against the normative rule packs. It
drives the same portrayal and validation pipelines as the Avalonia viewer, but
rasterises through the Mapsui-free Skia *headless* renderers so it can run
without a UI — making it suitable for batch scripts (e.g. generating sea-ice or
surface-current previews) and CI conformance checks.

It offers four subcommands:

| Command | Purpose |
|---|---|
| `s100 render <dataset> <output>` | Render a dataset to an image (PNG, JPEG, or WebP). |
| `s100 validate <dataset-or-exchange-set>` | Validate against the spec's normative rule pack (plus exchange-set signature/checksum integrity), with compiler-style output and suppression. |
| `s100 info <dataset>` | Show the detected spec, edition, headless-render capability, and (for time-series datasets) the available time steps. |
| `s100 list-specs` | List the supported product specifications and which support headless rendering. |

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
| `--no-text` | off | Suppress text/label drawing instructions (shorthand for `--hide text`). |
| `--hide <list>` | _none_ | Suppress drawing-instruction categories — any of `text`, `points`, `lines`, `areas` — useful for clean fills on label-dense products such as S-411 sea-ice. |

## Capabilities

- **Headless rendering** for S-101; S-102; S-104 and S-111 *gridded* coverages;
  and the GML products S-122/124/125/127/128/129/131/201/411/421.
- **Output formats:** PNG, JPEG, and WebP. The format is inferred from the
  output file extension (or set explicitly with `--format`); `--quality`
  controls the encoder quality for the lossy formats.
- **Text and category suppression** via `--no-text` / `--hide` for cleaner
  previews of label-dense products.
- **Fixed-station coverage** (S-104 / S-111 data coding format 3 / 8) is not
  rendered headlessly; the CLI returns a descriptive error.
- **S-57 is not supported** by the headless path.

See the project README under `tools/EncDotNet.S100.Cli/README.md` for the full
option reference and exit codes.
