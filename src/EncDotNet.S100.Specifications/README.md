# EncDotNet.S100.Specifications

Bundles official S-100 Feature Catalogues (FCs) and Portrayal Catalogues (PCs) as embedded resources so that applications can work out of the box without requiring users to locate and download specification files.

## Content layout

Place specification assets under `content/` following this convention:

```
content/
  S101/
    fc/
      FeatureCatalogue.xml
    pc/
      portrayal_catalogue.xml
      ColorProfiles/
      Rules/
      Symbols/
  S102/
    fc/
      FeatureCatalogue.xml
    pc/
      ...
  S104/
    ...
  S111/
    ...
  S124/
    ...
  S125/
    ...
  S129/
    ...
  S421/
    fc/
      FeatureCatalogue.xml
    pc/
      portrayal_catalogue.xml
      ColorProfiles/
      Rules/
      Symbols/
```

## Usage

```csharp
using EncDotNet.S100.Specifications;

// Open the bundled S-111 Feature Catalogue
await using Stream fc = await Specification.OpenFeatureCatalogueAsync("S111");

// Get an IAssetSource for the bundled S-111 Portrayal Catalogue
using IAssetSource pcSource = Specification.CreatePortrayalCatalogueSource("S111");
```
