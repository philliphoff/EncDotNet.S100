# EncDotNet.S100.Samples.Quickstart

This console sample uses the [`EncDotNet.S100`](../../src/EncDotNet.S100/README.md)
package to open a dataset, list its features and render it to a PNG. The
package supplies the feature and portrayal catalogues, so the sample doesn't
set any up.

It's the runnable version of the .NET library section of
[Getting started](../../docs/getting-started.md#net-library).

## Prerequisites

- The .NET 10 SDK, to build the sample from this repository.

The sample includes a small synthetic S-124 (navigational warnings) dataset,
`sample-navwarn.gml`, copied from `tests/datasets/S124/navwarn_point.gml`. It's
hand-written test data, not a real navigational warning, so you don't need to
download anything.

## Run the sample

From the repository root:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.Quickstart
```

The sample opens the bundled dataset and writes `out.png` to the current
folder. It prints:

```text
Opened sample-navwarn.gml — S-124/0.0.0
Features: 2
  f1: NAVWARN Part
  f2: NAVWARN Part
Wrote 7,242 bytes to out.png
```

The byte count can differ between builds.

To use your own dataset, pass its path and an output path. The sample accepts
ISO 8211 (`.000`), HDF5 (`.h5`) and GML files:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.Quickstart -- path/to/dataset.000 out.png
```

Coverage products such as S-102, S-104 and S-111 have no features to list. If
a dataset can't be rendered headlessly, such as a fixed-station time series,
the sample says so and doesn't write a PNG.

## How it works

[`Program.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/samples/EncDotNet.S100.Samples.Quickstart/Program.cs)
makes three calls:

1. `S100Dataset.Open(path)` opens the dataset and detects its product
   specification from the file.
2. `S100FeatureCatalogue.Bundled(spec).EnumerateFeatures(dataset)` lists the
   features through the bundled feature catalogue.
3. `new PngS100DatasetRenderer().RenderAsync(dataset)` renders the dataset to
   PNG bytes through the bundled portrayal catalogue. The sample checks
   `dataset.CanRenderHeadless` first.

The sample references the `EncDotNet.S100` project so that it builds in this
repository. In your own project, add the package instead:

```bash
dotnet add package EncDotNet.S100
```

## Next steps

- [Getting started](../../docs/getting-started.md): the desktop app and the
  command-line tool, and where to find sample data.
- [Render options](../../src/EncDotNet.S100/README.md#render-options): image
  size, palette, symbol scale and time step.
- [Loading datasets](../../docs/loading-datasets.md): folders, ZIPs, exchange
  sets and S-101 updates.
