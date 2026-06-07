# Getting started

This guide gets a supported S-100 dataset onto your screen — as a PNG — in a
few minutes, with **no prior S-100 knowledge required**. There are two paths:

- **[Library path](#library-path)** — render (or read features from) a dataset
  in ~20 lines of C# using the batteries-included `EncDotNet.S100` facade.
- **[CLI path](#cli-path)** — download the standalone `s100` tool and render
  from the command line, no .NET installation required.

If you just want to see it run, the
[`EncDotNet.S100.Samples.Quickstart`](../samples/EncDotNet.S100.Samples.Quickstart)
console sample does the library path end-to-end against a bundled synthetic
fixture — clone the repo and `dotnet run` it.

## Library path

The [`EncDotNet.S100`](../src/EncDotNet.S100/README.md) package is the
on-ramp: open a dataset, read its features, and render it to an image
**without hand-wiring feature or portrayal catalogues** — the official
catalogues bundled in
[`EncDotNet.S100.Specifications`](../src/EncDotNet.S100.Specifications/README.md)
are discovered and wired for you.

### 1. Install

```sh
dotnet add package EncDotNet.S100
```

That single package transitively brings in the readers, the pipeline factory,
the Lua/MoonSharp portrayal engine, the bundled specifications, and the
headless Skia renderer.

### 2. Render a dataset to PNG

```csharp
using EncDotNet.S100;

// Detects the product specification from the file (ISO 8211, HDF5, or GML).
using var dataset = S100Dataset.Open("chart.000");
Console.WriteLine($"Opened {dataset.Spec}");

// Read features through the bundled feature catalogue
// (empty for coverage products such as S-102/104/111).
using var featureCatalogue = S100FeatureCatalogue.Bundled(dataset.Spec.Name);
foreach (var feature in featureCatalogue.EnumerateFeatures(dataset))
    Console.WriteLine($"  {feature.FeatureRef}: {feature.FeatureTypeName ?? feature.FeatureType}");

// Render to PNG through the bundled portrayal catalogue.
using var renderer = new PngS100DatasetRenderer();
byte[] png = await renderer.RenderAsync(dataset);
File.WriteAllBytes("out.png", png);
```

`RenderAsync(dataset)` is the one-call path: it uses the bundled feature and
portrayal catalogues for the dataset's product specification.

### 3. Render options

```csharp
using EncDotNet.S100.Pipelines; // PaletteType

byte[] png = await renderer.RenderAsync(dataset, new S100RendererOptions
{
    Width = 2048,
    Height = 1536,
    Palette = PaletteType.Night,
    SymbolScale = 1.25,
    TimeStep = 0,            // time-aware products (S-104, S-111)
});
```

Not every dataset shape can be rasterised headlessly (for example, fixed-station
time series). Guard with `dataset.CanRenderHeadless` before rendering, and use
`dataset.AvailableTimes` to discover the time steps of S-104 / S-111 products.

For custom catalogues and the full API, see the
[`EncDotNet.S100` README](../src/EncDotNet.S100/README.md).

## CLI path

[`s100`](cli.md) is a cross-platform console tool that renders any supported
dataset to PNG using the same portrayal pipelines as the library, through the
Mapsui-free Skia headless renderer.

### 1. Install (standalone download)

Each [GitHub Release](https://github.com/philliphoff/EncDotNet.S100/releases)
attaches a **self-contained, per-platform archive** that bundles the .NET
runtime and native libraries — **no .NET installation required**.

```bash
# macOS / Linux
tar -xzf s100-<version>-<rid>.tar.gz
./s100 list-specs
```

On Windows, extract `s100-<version>-win-x64.zip` and run `s100.exe`. See
[docs/cli.md](cli.md) for the per-platform asset names and the macOS Gatekeeper
note.

### 2. Inspect and render

```bash
# Show the detected spec, bounds, and (for time-series) the available time steps
s100 info dataset.h5

# Render to a 1024x768 PNG (auto-detects the spec)
s100 render dataset.h5 out.png

# Render the 7th time step at night palette on a larger canvas
s100 render currents.h5 currents.png --time-step 6 --palette night -w 2048 -h 1536
```

See [docs/cli.md](cli.md) for the full option and exit-code reference.

## Where to get sample data

This repository **does not** ship real ENC data, and you should never commit
real ENC data to it either. To try the tools with real-world data, use one of
the freely available official sample sets:

- **IHO / product-specification test data** — the IHO and the test-bed working
  groups publish sample datasets and exchange sets alongside several S-100
  product specifications (S-101, S-102, S-104, S-111, S-12x, …). Start from the
  [IHO S-100 page](https://iho.int/en/s-100-edition-5-2-0) and the per-product
  specification repositories.
- **Synthetic fixtures in this repo** — the small hand-authored GML/HDF5
  fixtures under [`tests/datasets/`](../tests/datasets) are safe to experiment
  with and are exactly what the test suite and the quickstart sample use. They
  are deliberately minimal and are **not** navigationally meaningful.

The quickstart sample reuses one of these synthetic S-124 fixtures so it runs
with no downloads at all.

## Next steps

- [Command-line rendering](cli.md) — the full `s100` reference.
- [Documentation index](index.md) — per-product libraries and conceptual guides.
- [Typed data models](typed-data-models.md) — strongly-typed projections over
  the schema-agnostic feature bags.
