# EncDotNet.S100.Features

`EncDotNet.S100.Features` parses S-100 feature catalogue XML (ISO 19110,
S-100 Part 5) into a model of feature types, information types, attributes,
roles and associations. Reference it when you need to read a feature catalogue
directly, for example to look up attribute names or enumerated values. The
[`EncDotNet.S100`](../EncDotNet.S100/README.md) facade uses it for you through
`S100FeatureCatalogue`.

## Install

```bash
dotnet add package EncDotNet.S100.Features
```

## Example: read a catalogue and decode codes

```csharp
using EncDotNet.S100.Features;

FeatureCatalogue catalogue = FeatureCatalogueReader.Read("path/to/feature_catalogue.xml");
Console.WriteLine($"{catalogue.Name} {catalogue.VersionNumber}: {catalogue.FeatureTypes.Count} feature types");

var decoder = new FeatureCatalogueDecoder(catalogue);
Console.WriteLine(decoder.ResolveFeatureTypeName("DepthArea"));
```

## Main entry points

- `FeatureCatalogueReader` reads a feature catalogue from a stream or a file
  path and returns a `FeatureCatalogue`.
- `FeatureCatalogue` is the parsed model. It lists feature types, information
  types, simple and complex attributes, roles, and feature and information
  associations.
- `FeatureType` and `InformationType` define the types.
- `AttributeBinding`, `FeatureBinding` and `InformationBinding` are the
  attributes, feature associations and information associations a type binds.
  A feature or information binding can allow several target types:
  `FeatureTypeRefs` and `InformationTypeRefs` list them all, and
  `FeatureTypeRef` and `InformationTypeRef` give the first.
- `SimpleAttribute`, `ComplexAttribute` and `ListedValue` define attributes and
  enumerated values. A numeric `SimpleAttribute` also has a `Uom`, a
  `UnitOfMeasure` with a name and symbol parsed from `<S100FC:uom>`. For
  example, `depthRangeMinimumValue` resolves to `metre` / `m`, so you can label
  values with their unit from the catalogue instead of guessing it.
- `FeatureAssociation`, `InformationAssociation` and `Role` describe
  relationships between features.
- `FeatureCatalogueDecoder` looks up names in a parsed catalogue in constant
  time:
  - `ResolveAttributeName`, `ResolveFeatureTypeName`,
    `ResolveInformationTypeName` and `IsEnumeratedAttribute`.
  - For enumerated simple attributes, `ResolveListedValue` returns the value's
    label and `ResolveListedValueDefinition` returns its definition, such as
    `"Grey Ice"` for an S-411 stage-of-development code. Both take the
    attribute code and the raw value, and return `null` for an attribute that
    isn't enumerated or a value that isn't listed.
- `FeatureCatalogueManager` resolves, parses and caches one `FeatureCatalogue`
  per product specification.
  - It reads catalogues through a stream resolver you supply. `SetSource`
    registers an `IAssetSource` as a bundled fallback for a product, and
    clears that product's cached catalogue, decoder and content hash.
  - `GetCatalogueHashAsync(spec)` (from `ICatalogueProvider<T>`) returns the
    lowercase hexadecimal SHA-256 of the catalogue XML that the resolver
    returns. Because it hashes the resolved bytes, it reflects any command-line
    or settings override. That makes it a safer cache-invalidation key than
    the declared version, which an override can leave unchanged. The hash is
    computed once per product, when first requested. A failure (`null`) isn't
    cached, so the next call tries again.

## See also

- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  use your own feature catalogue with the facade.
- [Typed data models](../../docs/typed-data-models.md): typed views over
  feature attributes.
