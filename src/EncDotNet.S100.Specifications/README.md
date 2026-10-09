# EncDotNet.S100.Specifications

This package embeds the feature catalogues and portrayal catalogues for each
supported product, so an application can read and portray datasets without
asking its users to find and download catalogue files. Reference it when you
need a bundled catalogue directly, for example to start from it and override
part of it. The [`EncDotNet.S100`](../EncDotNet.S100/README.md) package uses it
for you.

## Install

```bash
dotnet add package EncDotNet.S100.Specifications
```

## Open a bundled catalogue

```csharp
using EncDotNet.S100.Core;
using EncDotNet.S100.Specifications;

// The bundled S-111 feature catalogue XML.
await using Stream fc = await Specification.OpenFeatureCatalogueAsync("S-111");

// An asset source over the bundled S-111 portrayal catalogue.
using IAssetSource pc = Specification.CreatePortrayalCatalogueSource("S-111");
```

Product names can be written with or without the hyphen (`"S-111"` or
`"S111"`).

## Main members

All members are on the static `Specification` class:

| Member | Returns |
|---|---|
| `AvailableSpecs` | The products with bundled content: S-101, S-102, S-104, S-111, S-122, S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-401, S-411 and S-421. |
| `AvailableSpecRefs` | The same list as `SpecRef`s. Their version is `0.0.0`, because no manifest records the bundled edition; read the catalogue itself for its version. |
| `OpenFeatureCatalogueAsync(productSpec)` | The feature catalogue XML as a stream. |
| `TryOpenFeatureCatalogue(productSpec)`, `TryOpenFeatureCatalogueAsync(productSpec)` | The feature catalogue XML, or `null` if there isn't one. |
| `HasFeatureCatalogue(productSpec)` | Whether a feature catalogue is bundled. |
| `CreateFeatureCatalogueSource(productSpec)` | An `IAssetSource` over the feature catalogue folder, cached in memory after the first read. Register it with `FeatureCatalogueManager.SetSource`. |
| `HasPortrayalCatalogue(productSpec)` | Whether a portrayal catalogue is bundled. |
| `CreatePortrayalCatalogueSource(productSpec)` | An `IAssetSource` over the portrayal catalogue folder, with each asset cached in memory after the first read. |

To use your own catalogues instead, see
[Custom catalogues and validation](../../docs/catalogues-and-validation.md).

## Content layout

Each product has a folder under `content/`, named without the hyphen. Every
file under `content/` is embedded:

```text
content/
  S101/
    fc/
      FeatureCatalogue.xml
    pc/
      portrayal_catalogue.xml
      ColorProfiles/
      Rules/
      Symbols/
      ...
  S102/
  ...
```

`fc/FeatureCatalogue.xml` is the feature catalogue. `pc/` holds the portrayal
catalogue: its `portrayal_catalogue.xml` manifest and the folders it refers to,
such as `Rules/`, `Symbols/`, `LineStyles/`, `AreaFills/`, `ColorProfiles/`
and `Fonts/`. S-104 has no portrayal catalogue, so its `pc/` folder is empty.

Some products have a README with their source, edition and any changes made
when they were copied:

- [S-131](content/S131/README.md)
- [S-201](content/S201/README.md)
- [S-401](content/S401/README.md)

Fonts bundled with the S-124, S-201 and S-421 portrayal catalogues are under
the Apache License 2.0; see
[`LICENSE-Fonts.txt`](https://github.com/philliphoff/EncDotNet.S100/blob/main/src/EncDotNet.S100.Specifications/LICENSE-Fonts.txt).
