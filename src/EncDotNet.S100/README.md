# EncDotNet.S100

`EncDotNet.S100` is the facade package for IHO S-100 data. It opens a dataset,
detects its product specification, reads its features and renders it to an
image. It uses the official feature and portrayal catalogues bundled in
[`EncDotNet.S100.Specifications`](../EncDotNet.S100.Specifications/README.md),
so you don't have to load or wire catalogues yourself. Reference it unless you
need to assemble the readers and pipelines yourself; see
[Use the lower-level packages](#use-the-lower-level-packages).

## Install

```bash
dotnet add package EncDotNet.S100
```

The package brings in the product readers, the dataset pipeline factory, the
MoonSharp Lua portrayal engine, the bundled specifications and the headless
Skia renderer.

### Publish for Linux arm64

If you publish a `linux-arm64` application that uses this package, or the Skia
renderer directly, reference the self-contained SkiaSharp native library in
your executable project:

```xml
<!-- In your app's .csproj -->
<ItemGroup>
  <PackageReference Include="SkiaSharp.NativeAssets.Linux" ExcludeAssets="all" />
  <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" />
</ItemGroup>
```

The arm64 `libSkiaSharp.so` in the regular `SkiaSharp.NativeAssets.Linux`
package declares undefined `uuid_*` and `FT_Get_BDF_Property` symbols. The
process aborts once `fontconfig` and `freetype` load, on a normal arm64 desktop
or container. The `NoDependencies` build is self-contained and renders on both
x64 and arm64. The final executable selects native assets for its runtime
identifier, so this package can't make the swap for you. See
[issue #23](https://github.com/philliphoff/EncDotNet.S100/issues/23).

## Render a dataset to PNG

```csharp
using EncDotNet.S100;

using var dataset = S100Dataset.Open("path/to/dataset.000"); // detects the product specification
using var renderer = new PngS100DatasetRenderer();
byte[] png = await renderer.RenderAsync(dataset);
File.WriteAllBytes("out.png", png);
```

`RenderAsync(dataset)` uses the bundled feature and portrayal catalogues for
the dataset's product specification. `dataset.CanRenderHeadless` is `false` for
products that have no image rendering; of the bundled products, that's S-131.

### Render options

Pass `S100RendererOptions` to set the image size, palette, symbol scale and
time step:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Pipelines; // PaletteType

byte[] png = await renderer.RenderAsync(dataset, new S100RendererOptions
{
    Width = 2048,
    Height = 1536,
    Palette = PaletteType.Night,
    SymbolScale = 1.25,
    TimeStep = 0, // time-varying products, such as S-104 and S-111
});
```

`S100RendererOptions` also has `TextScale`, `Background`, `HiddenCategories`,
`Basemap`, `DisplayModeId` and `EcdisDisplay`.

## Open a dataset from a folder, ZIP or exchange set

`S100Dataset.OpenAsync` opens a dataset inside any `IAssetSource`: a
`FileSystemAssetSource` folder, a `ZipAssetSource` archive, or a decorator over
either. It detects the product specification from the content, as `Open` does
for a loose file:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Core;

using var zip = ZipAssetSource.Create("S101.zip");
using var dataset = await S100Dataset.OpenAsync(zip, "S-101/DATASET_FILES/101AA00DS0019.000");
```

`S100ExchangeSet` opens an S-100 exchange set from a folder, its `CATALOG.XML`,
or a `.zip`, and lists its datasets. An S-101 base cell and the sequential
updates the set includes for it are one entry, which opens with the updates
applied:

```csharp
await using var exchangeSet = await S100ExchangeSet.OpenAsync("S101.zip");
foreach (var entry in exchangeSet.Datasets)
{
    using var dataset = await entry.OpenAsync();
    byte[] png = await renderer.RenderAsync(dataset);
}
```

To read an encrypted (S-100 Part 15) exchange set, build an
`IDatasetKeyProvider` from the set's `Catalogue`, usually a `PermitKeyProvider`
over an authenticated permit, and read through
`exchangeSet.WithDecryption(keys)`. See
[Reading protected exchange sets](../../docs/protected-exchange-sets.md).

[Loading datasets](../../docs/loading-datasets.md) covers caching, S-101
updates, custom asset sources and the lower-level processor API.

## Read features

Feature access is on the feature catalogue, because decoding a feature's type
name and attributes needs one:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Datasets.Pipelines; // FeatureInfo

using var featureCatalogue = S100FeatureCatalogue.Bundled(dataset.Spec.Name);

foreach (var summary in featureCatalogue.EnumerateFeatures(dataset))
    Console.WriteLine($"{summary.FeatureRef}: {summary.FeatureType}");

FeatureInfo? info = featureCatalogue.GetFeature(dataset, someFeatureRef);
```

Coverage products (S-102, S-104, S-111) have no features to list.
[Reading product data](../../docs/reading-product-data.md) shows typed access
to each product's features, grids and time series.

## Use your own catalogues

An `S100Layer` pairs a dataset with the catalogues used to interpret and
portray it. Set either catalogue to override the bundled one:

```csharp
var layer = new S100Layer
{
    Dataset = dataset,
    PortrayalCatalogue = S100PortrayalCatalogue.FromAssetSource(myPortrayalSource),
    FeatureCatalogue = S100FeatureCatalogue.FromStream(myFeatureCatalogueXml),
};

byte[] png = await renderer.RenderAsync(layer);
```

When `FeatureCatalogue` or `PortrayalCatalogue` is `null`, the layer uses the
bundled catalogue for the dataset's product specification.

## Validate a dataset

`dataset.Validate()` runs the product's bundled validation rules and returns a
`ValidationReport` of findings. It returns `null` when the product has no rule
pack. [Custom catalogues and validation](../../docs/catalogues-and-validation.md)
also shows how to add your own rules.

## Render several datasets into one image

`PngS100DatasetRenderer` also renders an ordered list of layers, bottom-most
first, such as an S-101 chart under S-102 bathymetry:

```csharp
using var renderer = new PngS100DatasetRenderer();

byte[] png = await renderer.RenderAsync(
    new[]
    {
        new S100Layer { Dataset = enc },   // S-101
        new S100Layer { Dataset = bathy }, // S-102
    },
    new S100CompositeOptions { Width = 2048, Height = 1536 });
```

This overload (`IS100CompositeRenderer<byte[]>`) runs the S-98
interoperability engine for cross-dataset paint ordering and depth
suppression, such as the S-101-under-S-102 interleave and the R-101-102-B
depth-shading suppression (S-98 Annex A §A-6.9.1). It then paints all layers
against one shared viewport. Set `S100CompositeOptions.Viewport` to fix the
framing, or leave it `null` to fit the union of the extents of all active
layers. [Compose S-101 and S-102](../../docs/scenarios/compose-s101-s102.md)
walks through an example.

## Renderer result types

`IS100DatasetRenderer<TResult>` is generic in its result type.
`PngS100DatasetRenderer` implements `IS100DatasetRenderer<byte[]>` and returns
PNG bytes. Another renderer can return a different encoding or an in-memory
bitmap through the same interface.

The public API of this package doesn't use Mapsui types. It returns `byte[]`,
`FeatureSummary` and `FeatureInfo`, and the package has no Mapsui dependency.

## Use the lower-level packages

This package adds to the lower-level packages; it doesn't replace them. For
full control, use a product reader with a catalogue you inject, or drive
`DatasetPipelineFactory` in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md)
directly. [Top APIs](../../docs/top-apis.md) lists the main entry points in
each package.

## Reuse and disposal

- `S100Dataset`, `S100FeatureCatalogue` and `PngS100DatasetRenderer` are
  `IDisposable`. Dispose them.
- A dataset is parsed lazily, on first use, so it reads from its source after
  `Open` or `OpenAsync` returns.
  - `S100Dataset.OpenAsync(source, …)` and `S100ExchangeSet.OpenAsync(source, …)`
    borrow the `IAssetSource` you pass. Keep it alive until you've disposed the
    datasets, then dispose it yourself.
  - `S100ExchangeSet.OpenAsync(path)` owns the source it creates. Dispose
    datasets opened from an exchange set before the exchange set.
- A `PngS100DatasetRenderer` can render many datasets one after another. It
  caches the bundled pipeline host, so repeated renders reuse the parsed
  catalogues. Don't use one instance from several threads at once.
