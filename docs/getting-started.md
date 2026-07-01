# Getting started

This guide gets supported S-100 data in front of you in a few minutes, with
**no prior S-100 knowledge required**. Pick the path that fits you:

- **[Desktop app](#desktop-app)** — download the viewer and open a chart on an
  interactive map. No coding and no .NET installation required. *Start here if
  you just want to look at data.*
- **[Library path](#library-path)** — render (or read features from) a dataset
  in ~20 lines of C# using the batteries-included `EncDotNet.S100` facade.
- **[CLI path](#cli-path)** — download the standalone `s100` tool and render
  from the command line, no .NET installation required.

If you just want to see it run, the
[`EncDotNet.S100.Samples.Quickstart`](../samples/EncDotNet.S100.Samples.Quickstart)
console sample does the library path end-to-end against a bundled synthetic
fixture — clone the repo and `dotnet run` it.

## Desktop app

The **S-100 Viewer** is a cross-platform desktop application that loads any
combination of supported products and renders them, time-aligned, on an
interactive map over a bundled offline basemap (Natural Earth land;
OpenStreetMap optional). It needs no .NET installation
and no commercial chart assets.

### 1. Download

Each [GitHub Release](https://github.com/philliphoff/EncDotNet.S100/releases)
attaches a pre-built, self-contained app per platform:

| Platform | Asset | First launch |
|---|---|---|
| macOS (Apple silicon) | `.dmg` | Signed and Apple-notarized — open the DMG and drag the app to Applications. |
| Windows | `.zip` | Extract and run the `.exe`. The executable is Authenticode-signed via Azure Trusted Signing. |
| Linux | `.tar.gz` | Extract and run the executable. See [Linux prerequisites](#linux-prerequisites) below. |

#### Linux prerequisites

The Linux archive is self-contained but relies on a few system libraries
for globalization, fonts, and the X11/OpenGL windowing stack. On a
minimal or container image install them first (Debian/Ubuntu):

```bash
sudo apt-get update
sudo apt-get install -y libicu74 fontconfig fonts-dejavu-core \
  libx11-6 libice6 libsm6 libxext6 libxrender1 libxi6 libxcursor1 libxrandr2 \
  libgl1 libegl1
```

A running display server (X11, or Wayland via XWayland) is required. See
the [viewer README](../src/EncDotNet.S100.Viewer/README.md#linux-runtime-prerequisites)
for the full breakdown and the headless `s100` CLI's
[lighter requirements](cli.md).

### 2. Open some data

Launch the app, then either **drag a file onto the window** or use the **File**
menu. The viewer accepts:

- **Exchange sets** — a folder containing a `CATALOG.XML`, or a `.zip` of one.
  Every dataset the catalogue lists is loaded at once.
- **Loose datasets** — an individual `.h5` (S-102 / S-104 / S-111), `.gml`
  (any GML-encoded product), or `.000` (S-101 / S-57) file.

No data yet? See [Where to get sample data](#where-to-get-sample-data) below.

### 3. Explore

Pan and zoom with the mouse, trackpad, or touch. From the activity bar you can:

- toggle layers and see how products stack in the **Layer Stack**;
- click features in **Pick Mode** to read their decoded attributes;
- switch **Day / Dusk / Night** palettes and ECDIS display settings;
- scrub the **timeline** for time-varying data (water levels, currents, ice).

See the [viewer guide](../src/EncDotNet.S100.Viewer/README.md) for the full
feature tour.

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

### 4. Composite multiple datasets

Pass an ordered list of layers (bottom-most first) to render several datasets
into a single image. The facade drives the renderer-neutral **S-98
interoperability engine** — the same cross-dataset ordering and depth
suppression the interactive viewer applies (S-98 Annex A §A-6.9.1) — so an
S-101 ENC and an S-102 bathymetric surface interleave correctly and the S-101
depth shading is suppressed where S-102 supersedes it (R-101-102-B).

```csharp
using var enc = S100Dataset.Open("enc-cell.000");   // S-101
using var bathy = S100Dataset.Open("bathy.h5");      // S-102

byte[] png = await renderer.RenderAsync(
    new[]
    {
        new S100Layer { Dataset = enc },
        new S100Layer { Dataset = bathy },
    },
    new S100CompositeOptions { Width = 2048, Height = 1536 });
```

When no `Viewport` is supplied the compositor fits a shared viewport to the
**union** extent of all active layers; pass an explicit `S100CompositeOptions.Viewport`
to pin the framing. This path is entirely Mapsui-free — see the
[headless compositing design note](design/s98-interoperability.md).

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

- [Viewer guide](../src/EncDotNet.S100.Viewer/README.md) — the desktop app's
  full feature tour.
- [Command-line rendering](cli.md) — the full `s100` reference.
- [Documentation index](index.md) — per-product libraries and conceptual guides.
- [Typed data models](typed-data-models.md) — strongly-typed projections over
  the schema-agnostic feature bags.
