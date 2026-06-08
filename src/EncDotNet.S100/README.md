# EncDotNet.S100

The batteries-included on-ramp for IHO S-100 nautical data. Open a dataset, read
its features, and render it to an image **without hand-wiring feature or portrayal
catalogues** — the official catalogues bundled in
[`EncDotNet.S100.Specifications`](../EncDotNet.S100.Specifications/README.md) are
discovered and wired for you.

## Install

```sh
dotnet add package EncDotNet.S100
```

That single package transitively brings in the readers, the pipeline factory, the
Lua/MoonSharp portrayal engine, the bundled specifications, and the headless Skia
renderer.

### Linux arm64 native dependency

If you publish a **`linux-arm64`** application that uses this facade (or the Skia
renderer directly), reference the self-contained SkiaSharp native in **your
executable project**:

```xml
<!-- In your app's .csproj -->
<ItemGroup>
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" ExcludeAssets="all" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" />
</ItemGroup>
```

The regular `SkiaSharp.NativeAssets.Linux` arm64 `libSkiaSharp.so` declares
undefined `uuid_*` / `FT_Get_BDF_Property` symbols that abort the process once
`fontconfig`/`freetype` load on a normal arm64 desktop or container. The
`…NoDependencies` build is self-contained and renders on both x64 and arm64.
Native RID asset selection belongs to the final executable, so this package does
**not** force the swap on your behalf. See
[issue #23](https://github.com/philliphoff/EncDotNet.S100/issues/23).


## Quickstart — render a dataset to PNG

```csharp
using EncDotNet.S100;

using var dataset = S100Dataset.Open("chart.000");      // detects the product spec
using var renderer = new PngS100DatasetRenderer();
byte[] png = await renderer.RenderAsync(dataset);        // bundled FC + PC
File.WriteAllBytes("out.png", png);
```

`RenderAsync(dataset)` is the one-call path: it uses the bundled feature and
portrayal catalogues for the dataset's product specification.

### Render options

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

## Read features

Feature access lives on the **feature catalogue**, because decoding a feature's
type name and attributes presupposes one:

```csharp
var fc = S100FeatureCatalogue.Bundled(dataset.Spec.Name);

foreach (var summary in fc.EnumerateFeatures(dataset))
    Console.WriteLine($"{summary.FeatureRef}: {summary.FeatureType}");

FeatureInfo? info = fc.GetFeature(dataset, someFeatureRef);
```

## Custom catalogues

A **layer** pairs a dataset with the catalogues used to interpret and portray it.
Supply your own to override the bundled defaults:

```csharp
var layer = new S100Layer
{
    Dataset = dataset,
    PortrayalCatalogue = S100PortrayalCatalogue.FromAssetSource(myPortrayalSource),
    FeatureCatalogue = S100FeatureCatalogue.FromStream(myFeatureCatalogueXml),
};

byte[] png = await renderer.RenderAsync(layer);
```

When `FeatureCatalogue` / `PortrayalCatalogue` are left `null`, the bundled
catalogue for the dataset's product specification is used.

## Layering — the grow-up story

`S100Layer` is the composable unit, so growing from a single chart to a stacked
view (e.g. an S-101 chart under S-102 bathymetry and S-411 sea ice) is "add more
layers." The single-layer render path ships today; an additive multi-layer
composite overload (shared viewport + S-98 paint ordering) is a planned
fast-follow and does not change this API shape.

## Renderers are generic in their result

`IS100DatasetRenderer<TResult>` is parameterised on the result type.
`PngS100DatasetRenderer` implements `IS100DatasetRenderer<byte[]>` (PNG bytes).
The same abstraction extends to other encoders (JPEG/WebP), to in-memory bitmaps,
and — once the Mapsui decoupling
([#189](https://github.com/philliphoff/EncDotNet.S100/issues/189)) lands — to a
Mapsui layer renderer, without changing the contract.

> **Mapsui note.** The public API of this package is Mapsui-free (it returns
> `byte[]`, `FeatureSummary`, and `FeatureInfo` — never Mapsui types). Mapsui is
> currently a *transitive* dependency of the underlying pipeline; when #189 lands
> it drops out with no change to this package's API.

## À-la-carte (advanced)

This facade is purely additive. Advanced users who need full control can keep
using a per-spec reader with an injected catalogue, or drive
`DatasetPipelineFactory`
([`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md))
directly.

## Reuse and disposal

- `S100Dataset` and `PngS100DatasetRenderer` are `IDisposable`; dispose them.
- A `PngS100DatasetRenderer` instance may render many datasets sequentially; it
  caches the bundled pipeline host so repeated renders reuse warmed catalogue
  parse caches. Concurrent use of one instance is not supported.
