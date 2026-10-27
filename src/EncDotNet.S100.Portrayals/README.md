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

## Code-defined portrayal

Some products have no portrayal catalogue: S-104 Ed 2.0.0 defines no
symbology, and weather grids and CAP warnings aren't S-100 products. The
`EncDotNet.S100.Portrayals.Application` namespace holds the pieces for drawing
them in code, producing the same drawing instructions, palettes and symbols a
catalogue does:

- `ApplicationPalette` holds named colour tokens, each with a Day, Dusk and
  Night value (`PaletteColors`). `GetColorPalette(palette)` returns the
  `ColorPalette` that instruction colour tokens and SVG palette classes
  resolve against.
- `SymbolSet` reads S-100 SVG symbols from a project's embedded resources.
  Its `Provider` is the `Func<string, string?>` symbol provider a sub-layer
  takes.
- `InstructionBuilder` builds point-symbol, text, line and area
  `DrawingInstruction`s, stamping its viewing group, drawing priority, display
  plane and scale limits on each. `TextInstructionStyle` describes a label's
  font, colours, halo and offset.
- `IDynamicFeaturePortrayal` is a code-defined portrayal of a dynamic
  feature source's features: drawing instructions (feature reference = the
  feature id), with the palette and symbols they resolve against. The NDBC
  station-model glyph (`EncDotNet.S100.DynamicSources.Ndbc`) is one; the
  Mapsui renderer is `PortrayedDynamicFeatureRenderer`.
- `ApplicationContentScope.For(style, version)` is the style's part of the
  tile disk-cache content scope: the style name and version plus the toolkit
  assembly's version. A code-defined style has no catalogue files to hash, so
  bump its version when its symbols or colours change.

```csharp
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals.Application;

var palette = new ApplicationPalette("Weather", new Dictionary<string, PaletteColors>
{
    ["WXINK"] = PaletteColors.FromHex(day: "#0F5E73", dusk: "#9FD3DE", night: "#3E7C88"),
});
var symbols = new SymbolSet(typeof(MyStyle).Assembly, "My.Product.Symbols");
var stations = new InstructionBuilder { ViewingGroup = 31050, DrawingPriority = 15 };

var instruction = stations.Point("station:42", "WXSTATION");
ColorPalette night = palette.GetColorPalette(PaletteType.Night);
string scope = ApplicationContentScope.For("Weather", version: 1);
```

`S104CoverageStyle` in
[`EncDotNet.S100.Datasets.S104`](../EncDotNet.S100.Datasets.S104/README.md#portrayal)
is built this way.

## See also

- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  use your own portrayal catalogue with the facade.
- [`EncDotNet.S100.Specifications`](../EncDotNet.S100.Specifications/README.md):
  the official catalogues bundled with the library.
