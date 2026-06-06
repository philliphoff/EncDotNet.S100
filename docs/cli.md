# Command-line rendering (`s100`)

`EncDotNet.S100.Cli` is a cross-platform .NET console tool, invoked as **`s100`**,
that renders any supported S-100 dataset to a PNG image. It drives the same
portrayal pipelines as the Avalonia viewer, but rasterises through the
Mapsui-free Skia *headless* renderers so it can run without a UI — making it
suitable for batch scripts (e.g. generating sea-ice or surface-current
previews).

The tool is structured with **subcommands** so its scope can grow (tiling,
batch, validation, …) without breaking existing usage.

## Quick start

```bash
# Render a dataset to a 1024x768 PNG (auto-detects the spec)
dotnet run --project tools/EncDotNet.S100.Cli -- render dataset.h5 out.png

# Inspect a time-series dataset and list its time steps
dotnet run --project tools/EncDotNet.S100.Cli -- info currents.h5

# Render the 7th time step at night-palette, larger canvas
dotnet run --project tools/EncDotNet.S100.Cli -- \
    render currents.h5 currents.png --time-step 6 --palette night -w 2048 -h 1536
```

## How it works

1. `DatasetPipelineFactory.DetectProductSpec` sniffs the file (extension +
   content) to determine the product specification.
2. `DatasetPipelineFactory.CreateProcessor` builds the matching
   `IDatasetProcessor`, wired to the portrayal and feature catalogues bundled in
   `EncDotNet.S100.Specifications`.
3. The CLI feature-tests the processor for `IHeadlessImageRenderer`. If present,
   it builds a spec-specific `RenderContext` (palette, scales, and — for
   time-series specs implementing `ITimeAwareDatasetProcessor` — a `DateTime`
   resolved from `--time-step`) and calls `RenderHeadlessAsync`.
4. The resulting `SKBitmap` is encoded to PNG and written to the output path.

Vector specs (S-101 and all GML products) render through
`HeadlessVectorRenderer`; coverage specs (S-102/104/111) render through
`CoverageHeadlessRenderer`, which fits the coverage extent into the requested
pixel size (preserving aspect, letter-boxed) and overlays oriented arrows via
`SkiaCoverageArrowRenderer` for S-111.

## Capabilities and limitations

- **Supported (headless):** S-101; S-122/124/125/127/128/129/131/201/411/421;
  S-102; S-104 and S-111 *gridded* coverages.
- **PNG output only** in v1.
- **Pattern area-fills are omitted** on the vector headless path (points, lines,
  solid fills, and text render).
- **Fixed-station coverage** (S-104/S-111 data coding format 3 / 8) is **not**
  supported headlessly; the CLI returns a descriptive error.
- **S-57 is not supported.**
- **Mapsui is linked but does no rendering.** The CLI transitively references
  the Mapsui renderer for the spec-detection factory and the
  `ProjNetCrsTransformFactory`. No Mapsui code runs on the headless render path;
  fully decoupling these is tracked as follow-up work.

See the project README under `tools/EncDotNet.S100.Cli/README.md` for the full
option reference and exit codes.
