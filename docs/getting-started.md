# Getting started

There are three ways to use EncDotNet.S100. Each section below is a
self-contained quickstart.

| If you want to… | Use | .NET required |
|---|---|---|
| Look at S-100 data on an interactive map | [Desktop app](#desktop-app) | No |
| Read or render S-100 data from your own .NET code | [.NET library](#net-library) | Yes |
| Render datasets to PNG from a script or terminal | [Command-line tool](#command-line-tool) | No |

All three need a dataset to open. If you don't have one, see
[Get sample data](#get-sample-data).

## Desktop app

SoundCharts is a desktop viewer for S-100 data. It loads any mix of supported
products onto one map, aligned in time, over an offline basemap.

### Install

Download the archive for your platform from the latest
[GitHub release](https://github.com/philliphoff/EncDotNet.S100/releases).
The app is self-contained; you don't need to install .NET.

| Platform | Asset | To install |
|---|---|---|
| macOS (Apple silicon) | `.dmg` | Open the DMG and drag the app to **Applications**. |
| Windows | `.zip` | Extract the archive and run the `.exe`. |
| Linux | `.tar.gz` | Install the [Linux prerequisites](#linux-prerequisites), then extract the archive and run the executable. |

#### Linux prerequisites

The app needs a display server (X11, or Wayland through XWayland) and a few
system libraries. On Debian or Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y libicu74 fontconfig fonts-dejavu-core \
  libx11-6 libice6 libsm6 libxext6 libxrender1 libxi6 libxcursor1 libxrandr2 \
  libgl1 libegl1
```

For details, see
[Linux runtime prerequisites](../src/EncDotNet.S100.Viewer/README.md#linux-runtime-prerequisites)
in the viewer guide.

### Open a dataset

1. Start the app.
2. Drag a dataset onto the window, or use one of the **File** menu commands:
   - **Open Dataset...** opens a single dataset file: `.000` (S-101 or S-57),
     `.h5` (S-102, S-104 or S-111) or `.gml` (any GML-encoded product).
   - **Open Exchange Set...** opens a folder that contains `CATALOG.XML`.
     **Open Exchange Set (ZIP)...** opens a `.zip` of one. The app loads every
     dataset the catalogue lists.
3. Pan and zoom with the mouse, trackpad or touch.

### Next steps

The [viewer guide](../src/EncDotNet.S100.Viewer/README.md) covers the layer
stack, feature picking, display palettes and settings, and the timeline for
time-varying products.

## .NET library

The [`EncDotNet.S100`](../src/EncDotNet.S100/README.md) package opens a
dataset, detects its product, and reads or renders it using the official feature
and portrayal catalogues, which ship inside the package.

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) 8.0 or later.
- A dataset file. See [Get sample data](#get-sample-data).

### Create a project

```bash
dotnet new console -n S100Quickstart
cd S100Quickstart
dotnet add package EncDotNet.S100
```

### Read and render a dataset

Replace the contents of `Program.cs` with:

```csharp
using EncDotNet.S100;

string datasetPath = args[0];

// Open the dataset. The product specification is detected from the file.
using var dataset = S100Dataset.Open(datasetPath);
Console.WriteLine($"Opened {dataset.Spec}");

// List features using the bundled feature catalogue.
// Coverage products (S-102, S-104, S-111) have no features to list.
using var featureCatalogue = S100FeatureCatalogue.Bundled(dataset.Spec.Name);
foreach (var feature in featureCatalogue.EnumerateFeatures(dataset))
    Console.WriteLine($"  {feature.FeatureRef}: {feature.FeatureTypeName ?? feature.FeatureType}");

// Render to PNG using the bundled portrayal catalogue.
if (dataset.CanRenderHeadless)
{
    using var renderer = new PngS100DatasetRenderer();
    byte[] png = await renderer.RenderAsync(dataset);
    File.WriteAllBytes("out.png", png);
    Console.WriteLine("Wrote out.png");
}
```

Run it with the path to your dataset:

```bash
dotnet run -- path/to/dataset.000
```

The program prints the detected product and its features, then writes
`out.png` to the project folder.

`CanRenderHeadless` is `false` for dataset shapes that have no image
rendering, such as fixed-station time series.

The runnable
[Quickstart sample](../samples/EncDotNet.S100.Samples.Quickstart/README.md)
contains the same code and a bundled test dataset.

### Next steps

- [Render options](../src/EncDotNet.S100/README.md#render-options): image size,
  palette, symbol scale and time step.
- [Loading datasets](loading-datasets.md): open folders, ZIPs and exchange sets,
  and apply S-101 updates.
- [Reading product data](reading-product-data.md): typed access to each
  product's features, grids and time series.
- [Compose S-101 and S-102](scenarios/compose-s101-s102.md): render several
  datasets into one image.
- [Top APIs](top-apis.md): the main entry points in each package.

## Command-line tool

`s100` inspects datasets and renders them to PNG. It uses the same portrayal
code as the library.

### Install

Download the `s100` archive for your platform from the latest
[GitHub release](https://github.com/philliphoff/EncDotNet.S100/releases).
The tool is self-contained; you don't need to install .NET.

On macOS or Linux:

```bash
tar -xzf s100-<version>-<rid>.tar.gz
./s100 list-specs
```

On Windows, extract `s100-<version>-win-x64.zip` and run `s100.exe list-specs`.

`list-specs` prints the product specifications the tool supports. For the asset
names per platform and the macOS Gatekeeper prompt, see
[Command-line rendering](cli.md).

### Inspect and render a dataset

Show the detected product, the bounds and, for time series, the available time
steps:

```bash
./s100 info path/to/dataset.h5
```

Render it to a PNG:

```bash
./s100 render path/to/dataset.h5 out.png
```

### Next steps

[Command-line rendering](cli.md) lists every command and option, including
palettes, image size, time steps and rendering several layers into one image.

## Get sample data

This repository doesn't include real ENC data. Don't commit real ENC data to it.

- **Official sample data**: the IHO and its test-bed working groups publish
  sample datasets and exchange sets with several product specifications
  (S-101, S-102, S-104, S-111, S-12x and others). Start at the
  [IHO S-100 page](https://iho.int/en/s-100-edition-5-2-0) and follow the links
  to each product specification.
- **Test fixtures in this repository**: the small datasets under
  [`tests/datasets/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/datasets)
  are synthetic. They're useful for trying the tools, but they aren't
  navigationally meaningful.
