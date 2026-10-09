# Compose S-101 and S-102

This guide renders an S-101 electronic navigational chart and an S-102
bathymetric surface into one PNG, so the chart's features are drawn together
with the bathymetry. Use it when you need a single image of several products
for the same area.

## Prerequisites

- The `s100` command-line tool. See
  [Getting started](../getting-started.md#command-line-tool).
- An S-101 dataset (`.000`) and an S-102 dataset (`.h5`) that cover the same
  area.

## Steps

1. Render both datasets as layers of one image. With `--layer` and no `-o`,
   the positional argument is the output path:

   ```bash
   s100 render --layer path/to/enc.000 --layer path/to/bathy.h5 chart.png
   ```

   The view fits the combined extent of all layers.

2. To render a specific area instead, pass a WGS-84 bounding box as
   `minLon,minLat,maxLon,maxLat`:

   ```bash
   s100 render --layer path/to/enc.000 --layer path/to/bathy.h5 -o chart.png --bbox -1.5,50.0,-1.0,50.5
   ```

## Result

`chart.png` contains both products in one image. The S-98 interoperability
rules decide which product draws above the other.

## Layer order

The S-98 interoperability engine orders the layers by display plane, so the
order of the `--layer` options doesn't set the drawing order. It only breaks
ties between layers in the same display plane. For how the engine decides,
see the [S-98 interoperability design note](../design/s98-interoperability.md).

## Compose in .NET

The [`EncDotNet.S100`](../../src/EncDotNet.S100/README.md) package composites
a list of `S100Layer` values with the same S-98 engine:

```csharp
using EncDotNet.S100;

using var enc = S100Dataset.Open("path/to/enc.000");
using var bathy = S100Dataset.Open("path/to/bathy.h5");
using var renderer = new PngS100DatasetRenderer();

byte[] png = await renderer.RenderAsync(
    new[]
    {
        new S100Layer { Dataset = enc },
        new S100Layer { Dataset = bathy },
    },
    new S100CompositeOptions { Width = 2048, Height = 1536 });
File.WriteAllBytes("chart.png", png);
```

Without a `Viewport` in `S100CompositeOptions`, the image fits the combined
extent of all layers. Set `Mariner` to pass mariner settings, such as the
safety contour, to each layer's portrayal and to the S-98 rules.

For more compositing options, including whole exchange sets, see
[Command-line rendering](../cli.md#compositing-multiple-datasets). To draw the
layers on an interactive map instead, see
[Embedding the renderer](../embedding-the-renderer.md).
