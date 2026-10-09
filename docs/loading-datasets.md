# Loading datasets

S-100 data arrives in several shapes: a loose dataset file, a dataset inside a
folder or ZIP archive, or an **exchange set** (a `CATALOG.XML` plus the
datasets and support files it lists, often zipped). ENC cells also come with
**sequential updates**. This guide shows how to open each shape with the
`EncDotNet.S100` facade:

- `S100Dataset` opens one dataset and detects its product specification.
- `S100ExchangeSet` opens an exchange set and lists its datasets.
- `IAssetSource` reads files from a folder (`FileSystemAssetSource`), a ZIP
  archive (`ZipAssetSource`) or your own storage. `CachingAssetSource` keeps
  what it reads in memory.
- `BundledDatasetProcessorFactory` gives you the lower-level dataset
  processors.

Datasets are parsed against the official feature and portrayal catalogues
bundled in `EncDotNet.S100.Specifications`, so you don't need to load any
catalogues yourself. The examples use files from
[`tests/datasets`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/datasets).

## Open a dataset file

`S100Dataset.Open` takes the path to an ISO 8211 (`.000`), HDF5 (`.h5`) or GML
(`.gml`) file and detects the product specification from its content:

```csharp
using EncDotNet.S100;

using var dataset = S100Dataset.Open("102US004MI1CI262227.h5");
Console.WriteLine(dataset.Spec);   // S-102/3.0.0
```

Detection reads only what it needs to identify the product. The dataset is
parsed on first use, for example when you read `Spec` or render it.

`Open` throws `NotSupportedException` when the file isn't a product this
library recognizes: an unsupported product, a GML file whose root element no
product matches, or a file that isn't ISO 8211, HDF5 or GML. It throws
`FileNotFoundException` when the file doesn't exist.

## Open a dataset in a folder or ZIP archive

Datasets are read through an **asset source** (`IAssetSource`), which opens a
file by its path relative to a root. `EncDotNet.S100.Core` has one for a folder
and one for a ZIP archive. Pass the source and the dataset's relative path to
`S100Dataset.OpenAsync`:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Core;

using var folder = FileSystemAssetSource.Create("/data/charts");
using var fromFolder = await S100Dataset.OpenAsync(folder, "S-102/102US004MI1CI262227.h5");

using var zip = ZipAssetSource.Create("S101.zip");
using var fromZip = await S100Dataset.OpenAsync(zip, "S-101/DATASET_FILES/101AA00DS0019.000");
```

`OpenAsync` detects the product from the content, as `Open` does. Relative
paths use `/` as the separator.

The dataset **borrows** the source. You still own the source, and it must stay
open until you dispose the dataset, because the dataset is parsed on first
use. Declare the source before the dataset, as above, so `using` disposes them
in the right order.

> [!IMPORTANT]
> If you dispose a source before a dataset that reads from it, the dataset
> throws `ObjectDisposedException` the first time you use it. The same applies
> to a dataset opened from an exchange set that you open from a path. Dispose
> datasets first.

## Cache repeated reads

Every render parses the dataset again from its source, so a ZIP entry is
decompressed again each time. When you render the same dataset several times,
for example at several sizes or palettes, wrap the source in a
`CachingAssetSource`. It keeps each file's bytes in memory after the first
read:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

using var zip = new CachingAssetSource(ZipAssetSource.Create("S101.zip"));
using var dataset = await S100Dataset.OpenAsync(zip, "S-101/DATASET_FILES/101AA00DS0019.000");

using var renderer = new PngS100DatasetRenderer();
foreach (var palette in new[] { PaletteType.Day, PaletteType.Dusk, PaletteType.Night })
{
    byte[] png = await renderer.RenderAsync(dataset, new S100RendererOptions { Palette = palette });
    File.WriteAllBytes($"101AA00DS0019-{palette}.png", png);
}
```

The cache is never evicted. Disposing the `CachingAssetSource` also disposes
the source it wraps.

## Open an exchange set

`S100ExchangeSet.OpenAsync` accepts any of these:

- a folder whose top level holds `CATALOG.XML`
- the `CATALOG.XML` file itself
- a `.zip` archive with `CATALOG.XML` at its root

`Catalogue` is the parsed catalogue, and `Datasets` lists the datasets to load.
This example opens every dataset in a zipped exchange set and renders it:

```csharp
using EncDotNet.S100;

await using var exchangeSet = await S100ExchangeSet.OpenAsync("S101.zip");
using var renderer = new PngS100DatasetRenderer();

foreach (var entry in exchangeSet.Datasets)
{
    using var dataset = await entry.OpenAsync();
    if (!dataset.CanRenderHeadless)
        continue;

    byte[] png = await renderer.RenderAsync(dataset);
    File.WriteAllBytes(Path.ChangeExtension(Path.GetFileName(entry.Metadata.FileName), ".png"), png);
}
```

Opening an entry uses the product specification the catalogue declares. If the
catalogue doesn't declare one this library recognizes, the entry detects it from
the content.

`OpenAsync` throws `FileNotFoundException` when there's no `CATALOG.XML` where
it looks: at the folder's top level or at the ZIP archive's root. If an archive
holds the exchange set in a subfolder, open the archive with
`ZipAssetSource.Create(path, basePath: "SubFolder/")` and pass that source, as
described in [Open an exchange set from an asset source](#open-an-exchange-set-from-an-asset-source).
The base path is prepended to every relative path as it is, so it needs the
trailing `/`.

### Filter datasets before opening them

The catalogue's discovery metadata is available without opening any dataset.
Use it to filter by product, extent or edition, then open only what you need:

```csharp
foreach (var entry in exchangeSet.Datasets)
{
    var metadata = entry.Metadata;
    Console.WriteLine(
        $"{metadata.FileName}: {metadata.ProductSpecification?.ProductIdentifier} " +
        $"edition {metadata.EditionNumber}, {entry.Updates.Count} update(s)");

    if (metadata.BoundingBox is { } box)
        Console.WriteLine($"  {box.SouthBoundLatitude}..{box.NorthBoundLatitude} N, " +
                          $"{box.WestBoundLongitude}..{box.EastBoundLongitude} E");
}
```

### Skip unsupported products

Real exchange sets often include products this library doesn't support. Opening
one of those throws `NotSupportedException`, so catch it and move on:

```csharp
foreach (var entry in exchangeSet.Datasets)
{
    S100Dataset dataset;
    try
    {
        dataset = await entry.OpenAsync();
    }
    catch (NotSupportedException ex)
    {
        Console.WriteLine($"Skipping {entry.Metadata.FileName}: {ex.Message}");
        continue;
    }

    using (dataset)
    {
        Console.WriteLine($"{entry.Metadata.FileName}: {dataset.Spec}");
    }
}
```

### Open an exchange set from an asset source

To open an exchange set held in any other asset source, pass the source and the
catalogue's path:

```csharp
using var zip = ZipAssetSource.Create("path/to/archive.zip", basePath: "SubFolder/");
await using var exchangeSet = await S100ExchangeSet.OpenAsync(zip, "CATALOG.XML");
```

The exchange set then borrows the source, as `S100Dataset.OpenAsync` does: keep
the source open while the exchange set and its datasets are in use. When you
open an exchange set from a path, it owns the source it creates and disposes it
with the exchange set.

## Apply S-101 updates

An ENC cell is issued as a base cell (`….000`) followed by sequential updates
(`….001`, `….002`, …). When an exchange set has a base cell and its updates,
`Datasets` has **one** entry for the cell:

- `Metadata` describes the base cell.
- `Updates` lists the updates in order.
- `OpenAsync` returns the cell with the updates applied (S-100 Part 10a).

An update can only be applied with its base cell. If an exchange set has an
update but not its base cell, that entry's `OpenAsync` throws
`InvalidOperationException`.

`S100Dataset.Open` doesn't apply update files that sit next to a loose cell. To
open a loose cell with its updates, use
[`CreateProcessorWithFilesystemUpdates`](#use-dataset-processors).

## Read from your own storage

To read from somewhere else, such as blob storage, a database or an HTTP
server, implement `IAssetSource`. You implement `OpenAsync` and `Dispose`:

```csharp
using System.Net;
using EncDotNet.S100.Core;

sealed class HttpAssetSource(HttpClient http, Uri baseUri) : IAssetSource
{
    public async Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        var response = await http.GetAsync(
            new Uri(baseUri, relativePath), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new FileNotFoundException("Asset not found.", relativePath);

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync(cancellationToken);
    }

    public void Dispose() { }
}
```

- Throw `FileNotFoundException` for a missing file, so callers get the same
  error as from the built-in sources.
- The caller disposes the stream you return. The stream doesn't need to be
  seekable; readers that need to seek (HDF5) copy it first.
- Wrap the source in a `CachingAssetSource` if the same file is read more than
  once.
- Give the base URI a trailing slash (`https://example.org/data/`), so relative
  paths resolve beneath it.

## Use dataset processors

The facade opens, renders and lists features. For everything else a dataset can
do, such as hit-testing, coverage sampling, validation and portrayal
diagnostics, use its **dataset processor** (`IDatasetProcessor`).
`BundledDatasetProcessorFactory` creates processors with the same bundled
catalogues the facade uses:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Datasets.Pipelines;

using var factory = BundledDatasetProcessorFactory.Create();

// A loose ENC cell, with any sibling update files (.001, .002, ...) applied.
IDatasetProcessor processor = factory.CreateProcessorWithFilesystemUpdates("101AA00DS0019.000");

Console.WriteLine($"{processor.Spec}, extent {processor.Metadata.Extent}");
foreach (var feature in processor.EnumerateFeatures().Take(5))
    Console.WriteLine($"  {feature.FeatureRef}: {feature.FeatureTypeName ?? feature.FeatureType}");

(processor as IDisposable)?.Dispose();
```

Keep the factory alive while you use its processors; it owns the catalogue
caches they share.

For full control over the catalogues, the Lua engine or the CRS transforms,
construct a `DatasetPipelineFactory` yourself. The
[`EncDotNet.S100.Datasets.Pipelines` README](../src/EncDotNet.S100.Datasets.Pipelines/README.md)
describes it, and `ExchangeSetLoader`, which loads a whole exchange set.

## Next steps

- [Reading protected exchange sets](protected-exchange-sets.md): S-100 Part 15
  permits, decryption and signature verification.
- [Reading product data](reading-product-data.md): each product's own types for
  features, typed models, coverage grids and time series.
- [Top APIs](top-apis.md): the main entry points in each package.
