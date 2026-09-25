# Loading datasets

## Why it matters

S-100 data rarely arrives as one loose file. Producers ship **exchange sets**:
a `CATALOG.XML` plus datasets and support files, usually zipped, and ENC cells
arrive with **sequential updates**. This guide shows how to open data from each
of those shapes with the `EncDotNet.S100` facade, and how to drop to the
lower-level pipeline API when you need more control.

Every example parses datasets against the official catalogues bundled in
`EncDotNet.S100.Specifications`; no catalogue wiring is needed.

## Quick win

Open every dataset in a zipped exchange set and render each one:

```csharp
using EncDotNet.S100;

await using var exchangeSet = await S100ExchangeSet.OpenAsync("exchange-set.zip");
using var renderer = new PngS100DatasetRenderer();

foreach (var entry in exchangeSet.Datasets)
{
    using var dataset = await entry.OpenAsync();
    byte[] png = await renderer.RenderAsync(dataset);
    File.WriteAllBytes(Path.ChangeExtension(Path.GetFileName(entry.Metadata.FileName), ".png"), png);
}
```

## Deep dive

### A loose dataset file

`S100Dataset.Open` takes a path to an ISO 8211 (`.000`), HDF5 (`.h5`) or GML
(`.gml`) file and detects the product specification from its content:

```csharp
using var dataset = S100Dataset.Open("102US004MI1CI262227.h5");
Console.WriteLine(dataset.Spec);   // S-102/3.0.0
```

Detection only reads what it needs to identify the product. The dataset is
parsed on first use, for example when you read `Spec` or render it.

### A dataset inside a folder or a ZIP archive

Datasets are read through an **asset source** (`IAssetSource`): something that
opens a file by its path relative to a root. `EncDotNet.S100.Core` ships one
for a folder and one for a ZIP archive:

```csharp
using EncDotNet.S100.Core;

using var folder = FileSystemAssetSource.Create("/data/charts");
using var fromFolder = await S100Dataset.OpenAsync(folder, "S-102/102US004MI1CI262227.h5");

using var zip = ZipAssetSource.Create("S101.zip");
using var fromZip = await S100Dataset.OpenAsync(zip, "S-101/DATASET_FILES/101AA00DS0019.000");
```

`OpenAsync` detects the product from the content, just as `Open` does. Paths
are relative to the source's root and use `/` as the separator.

The dataset **borrows** the source: you still own it, and it must stay open
until the dataset is disposed, because the dataset is parsed on first use.
Declare the source before the dataset (as above) so `using` disposes them in
the right order.

### Caching reads

Every render parses the dataset again from its source, so a ZIP entry is
decompressed again each time. When you render the same dataset repeatedly, for
example at several sizes or palettes, wrap the source in a `CachingAssetSource`.
It keeps each file's bytes in memory after the first read:

```csharp
using var zip = new CachingAssetSource(ZipAssetSource.Create("S101.zip"));
using var dataset = await S100Dataset.OpenAsync(zip, "S-101/DATASET_FILES/101AA00DS0019.000");

using var renderer = new PngS100DatasetRenderer();
foreach (var palette in new[] { PaletteType.Day, PaletteType.Dusk, PaletteType.Night })
{
    byte[] png = await renderer.RenderAsync(dataset, new S100RendererOptions { Palette = palette });
    File.WriteAllBytes($"101AA00DS0019-{palette}.png", png);
}
```

`PaletteType` is in the `EncDotNet.S100.Pipelines` namespace. The cache is never
evicted, and disposing the `CachingAssetSource` also disposes the source it
wraps.

### An exchange set

`S100ExchangeSet.OpenAsync` accepts a folder whose top level holds
`CATALOG.XML`, the `CATALOG.XML` file itself, or a `.zip` with `CATALOG.XML` at
its root. `Catalogue` is the parsed catalogue, and `Datasets` lists what to
load:

```csharp
await using var exchangeSet = await S100ExchangeSet.OpenAsync("S101.zip");

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

The catalogue's discovery metadata is available without opening any dataset,
so you can filter first (by product, extent or edition) and open only what you
need. Opening an entry uses the product specification the catalogue declares,
and falls back to detecting it from the content when the catalogue doesn't
declare a recognised one.

Real exchange sets often include products this library doesn't support. Opening
one of those throws `NotSupportedException`, so skip them explicitly:

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

To open an exchange set held in some other asset source, pass the source and
the catalogue's path: `S100ExchangeSet.OpenAsync(source, "CATALOG.XML")`. As
with `S100Dataset.OpenAsync`, the exchange set then borrows the source; with a
path, it owns the source it creates.

### S-101 updates

An ENC cell is issued as a base cell (`….000`) followed by sequential updates
(`….001`, `….002`, …). When an exchange set ships a base cell together with its
updates, `Datasets` has **one** entry for the cell: `Metadata` describes the
base cell, `Updates` lists the updates in order, and `OpenAsync` returns the
cell with the updates applied (S-100 Part 10a).

An update whose base cell isn't in the same exchange set can't be applied; that
entry's `OpenAsync` throws `InvalidOperationException`.

### Your own asset source

Implement `IAssetSource` to read from anywhere else, such as blob storage, a
database or an HTTP server. The contract is one method plus `Dispose`:

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

Throw `FileNotFoundException` for a missing file, so callers get the same error
as from the built-in sources. The caller disposes the stream you return. It
doesn't need to be seekable; readers that need to seek (HDF5) copy it first.
Wrap the source in a `CachingAssetSource` if the same file will be read more
than once.

Base URIs need a trailing slash (`https://example.org/data/`) for relative
paths to resolve beneath them.

### Lower level: processors

The facade covers opening, rendering and reading features. For everything else
a dataset can do (hit-testing, coverage sampling, validation, portrayal
diagnostics), use its **dataset processor** (`IDatasetProcessor`).
`BundledDatasetProcessorFactory` creates processors with the same bundled
catalogues the facade uses:

```csharp
using EncDotNet.S100.Datasets.Pipelines;

using var factory = BundledDatasetProcessorFactory.Create();

// A loose ENC cell, with any sibling update files (.001, .002, ...) applied.
var processor = factory.CreateProcessorWithFilesystemUpdates("101AA00DS0019.000");

Console.WriteLine($"{processor.Spec}, extent {processor.Metadata.Extent}");
foreach (var feature in processor.EnumerateFeatures().Take(5))
    Console.WriteLine($"  {feature.FeatureRef}: {feature.FeatureTypeName ?? feature.FeatureType}");

(processor as IDisposable)?.Dispose();
```

Keep the factory alive while its processors are in use; it owns the catalogue
caches they share. `S100Dataset.Open` doesn't apply sibling update files, so use
`CreateProcessorWithFilesystemUpdates` when a loose cell has updates next to
it.

For full control over the catalogues, the Lua engine or the CRS transforms,
construct a `DatasetPipelineFactory` yourself; see the
[`EncDotNet.S100.Datasets.Pipelines` README](../src/EncDotNet.S100.Datasets.Pipelines/README.md).
It can also load a whole exchange set through `ExchangeSetLoader`.

## Troubleshooting

> [!IMPORTANT]
> A dataset opened from a source or exchange set reads from it on first use.
> Disposing a ZIP source, or an exchange set opened from a path, before the
> dataset makes the dataset throw `ObjectDisposedException` the first time you
> use it. Dispose datasets first.

> [!NOTE]
> `NotSupportedException` from `Open` or `OpenAsync` means the file isn't a
> product this library recognises: an unsupported product, a GML file whose
> root element no product matches, or a file that isn't ISO 8211, HDF5 or GML.

> [!TIP]
> `S100ExchangeSet.OpenAsync` throws `FileNotFoundException` when there's no
> `CATALOG.XML` where it looks: at the folder's top level or at the zip's root.
> If a zip nests the exchange set in a sub-folder, open it with
> `ZipAssetSource.Create(path, basePath: "SubFolder/")` and pass that source.
> The base path is prepended to every relative path as-is, so it needs the
> trailing `/`.

## Next step

- [Top APIs](top-apis.md) — the main entry point in each package.
- [Reading product data](reading-product-data.md) — each product's own data
  types: features, typed models, coverage grids and time series.
- [Reading protected exchange sets](protected-exchange-sets.md) — S-100 Part 15
  permits, decryption and signature verification.
