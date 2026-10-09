# EncDotNet.S100.Portrayals

`EncDotNet.S100.Portrayals` parses S-100 portrayal catalogues (S-100 Part 9):
the symbols, line styles, area fills, colour profiles, rules, viewing groups
and display modes used to draw S-100 data. Reference it when you need to read
a portrayal catalogue or its assets directly. The
[`EncDotNet.S100`](../EncDotNet.S100/README.md) facade uses it for you through
`S100PortrayalCatalogue`.

## Install

```bash
dotnet add package EncDotNet.S100.Portrayals
```

## Example: open a catalogue folder

```csharp
using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

using var provider = await PortrayalCatalogueProvider.OpenAsync(
    FileSystemAssetSource.Create("path/to/PortrayalCatalog"));

PortrayalCatalogue catalogue = provider.Catalogue;
Console.WriteLine($"{catalogue.ProductId} {catalogue.Version}: {catalogue.RuleFiles.Count} rule files");
```

`OpenAsync` reads `portrayal_catalogue.xml` from the source unless you pass
another path. The provider disposes the source when you dispose it.

## Main entry points

- `PortrayalCatalogueProvider` loads a catalogue and fetches the assets it
  references (rule files, symbols, line styles and others) from an
  `IAssetSource`.
- `PortrayalCatalogue` is the parsed model: symbols, line styles, area fills,
  colour profiles, pixmaps, style sheets, rule files, viewing groups, display
  modes, display planes and context parameters.
- `PortrayalCatalogueReader` parses portrayal catalogue XML from a stream or a
  file path.
- `ColorProfileReader`, `LineStyleReader` and `AreaFillReader` parse individual
  portrayal components.
- `ViewingGroup`, `DisplayMode`, `DisplayPlane` and `ContextParameter` describe
  display configuration.
- `PortrayalCatalogueManager` manages the portrayal catalogues for several
  product specifications. `SetPath` and `SetSource` set where a product's
  catalogue comes from.
  - It implements `ICatalogueProvider<PortrayalCatalogueProvider>`, including
    `GetCatalogueHashAsync(spec)`. That returns a lowercase hexadecimal
    SHA-256 over the catalogue XML and the bytes of every asset it declares:
    rule files, symbols, line styles, area fills, colour profiles, pixmaps and
    style sheets. Use it as a cache-invalidation key.
  - The hash is computed once per product, when first requested, and cleared
    by `SetPath` and `SetSource`. A failure (`null`) isn't cached, so the next
    call tries again.

## See also

- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  use your own portrayal catalogue with the facade.
- [`EncDotNet.S100.Specifications`](../EncDotNet.S100.Specifications/README.md):
  the official catalogues bundled with the library.
