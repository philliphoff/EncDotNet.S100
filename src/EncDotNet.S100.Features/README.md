# EncDotNet.S100.Features

Parser for S-100 Feature Catalogue XML files (ISO 19110 / S-100 Part 5).

## Overview

This library reads Feature Catalogue XML documents and produces a structured model including:

- **`FeatureCatalogue`** — the root parsed model containing feature types, information types, attributes, roles, and associations.
- **`FeatureCatalogueReader`** — XML parser that reads a feature catalogue from a stream or file.
- **`FeatureType`**, **`InformationType`** — definitions of feature and information types.
- **`SimpleAttribute`**, **`ComplexAttribute`**, **`ListedValue`** — attribute definitions and enumerated values. A `SimpleAttribute` of a numeric value type also exposes its **`Uom`** (a `UnitOfMeasure` of name + symbol, parsed from `<S100FC:uom>`); for example `depthRangeMinimumValue` resolves to `metre` / `m`, letting consumers annotate values with their authoritative unit rather than inferring it.
- **`FeatureAssociation`**, **`InformationAssociation`**, **`Role`** — inter-feature relationships.
- **`FeatureCatalogueManager`** — resolves, parses, and caches a `FeatureCatalogue` per product spec from an injected stream resolver (with an optional bundled `IAssetSource` fallback registered via `SetSource`). `GetCatalogueHashAsync(spec)` (the async `ICatalogueProvider<T>` content-hash member) returns a memoized lowercase-hex SHA-256 of the *resolved* catalogue XML bytes — reflecting any CLI / settings override the resolver applies, so it is a safe cache-invalidation input where a declared version string could miss an override that changes the file without bumping its version. The hash is computed lazily, once per spec, and a transient failure (null) is not permanently memoized. `SetSource` invalidates the parse, decoder, and content-hash caches for the affected spec.
- **`FeatureCatalogueDecoder`** — O(1) lookups over a parsed catalogue: `ResolveAttributeName`, `ResolveFeatureTypeName`, `ResolveInformationTypeName`, `IsEnumeratedAttribute`, and — for enumerated simple attributes — `ResolveListedValue` (returns the value's display **label**) and `ResolveListedValueDefinition` (returns its prose **definition**, e.g. `"Grey Ice"` for an S-411 stage-of-development code). Both listed-value lookups are keyed by attribute code + raw value and return `null` for non-enumerated attributes or unknown values.

## Installation

```sh
dotnet add package EncDotNet.S100.Features
```
