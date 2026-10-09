# Render S-102 to PNG

This guide renders an S-102 bathymetric surface (an HDF5 `.h5` file) to a PNG
image, coloured by depth with the bundled S-102 portrayal. Use it to produce
an image from a script, or to check what a bathymetry file contains.

## Prerequisites

- The `s100` command-line tool. See
  [Getting started](../getting-started.md#command-line-tool).
- An S-102 dataset. The repository includes a small test file,
  [`tests/datasets/S102/102US004MI1CI262227.h5`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tests/datasets/S102/102US004MI1CI262227.h5).

## Steps

1. Check that `s100` detects the file as S-102 and can render it:

   ```bash
   s100 info path/to/bathy.h5
   ```

   The output table shows `S-102` in the **Specification** row and `yes` in
   the **Headless render** row.

2. Render the dataset to a PNG:

   ```bash
   s100 render path/to/bathy.h5 bathy.png -w 1600 -h 1200
   ```

   Without `-w` and `-h`, the image is 1024 × 768 pixels. The view fits the
   whole grid.

## Result

`bathy.png` shows the bathymetric surface coloured by depth, in the day
palette, on an opaque white background.

## Use another palette

Pass `--palette` with `day`, `dusk` or `night`:

```bash
s100 render path/to/bathy.h5 bathy-night.png --palette night
```

## Render part of the grid

Pass a WGS-84 bounding box as `minLon,minLat,maxLon,maxLat`, using an area
your dataset covers. For coverage products, `s100` samples and renders only
the part of the grid inside the box:

```bash
s100 render path/to/bathy.h5 bathy-detail.png --bbox -1.5,50.0,-1.0,50.5
```

For every option, including `--format`, `--background` and `--basemap`, see
[Command-line rendering](../cli.md).

## Render from .NET

The [`EncDotNet.S100`](../../src/EncDotNet.S100/README.md) package renders
the same image from code:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Pipelines; // PaletteType

using var dataset = S100Dataset.Open("path/to/bathy.h5");
using var renderer = new PngS100DatasetRenderer();

byte[] png = await renderer.RenderAsync(dataset, new S100RendererOptions
{
    Width = 1600,
    Height = 1200,
    Palette = PaletteType.Night,
});
File.WriteAllBytes("bathy.png", png);
```

`RenderAsync` throws `NotSupportedException` for a dataset that has no image
rendering. If your code accepts datasets other than S-102, check
`dataset.CanRenderHeadless` first.
