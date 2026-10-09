# Typed data models

The per-product dataset types, such as `S124Dataset` and `S421Dataset`,
expose a dataset as a *feature bag*: flat lists of features and information
types, keyed by string codes, with weakly typed attribute dictionaries. The
portrayal pipeline reads datasets in this form, because the XSLT and Lua
portrayal rules look up attributes by name.

A typed data model is a read-only view over that feature bag, organized
around the product's own concepts, such as routes, warnings or aids to
navigation. Each GML product has one. The shared building blocks are in the
`EncDotNet.S100.Core` package, in the `EncDotNet.S100.DataModel` namespace.

## Choose a model

| To | Use |
|---|---|
| Portray a dataset, produce drawing instructions or iterate over features generically | The feature-bag dataset (`SxxxDataset`). |
| Work with a product's entities (routes, warnings, aids to navigation) as typed objects | The typed data model (`Sxxx{Root}.From(dataset, out diagnostics)`). |
| Edit a dataset or write it back to GML | Not supported. Typed models are read-only. |

## Read a typed model

Open the dataset, then call the typed root's `From` method. It returns the
typed model and a list of diagnostics:

```csharp
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;

var dataset = S124Dataset.Open("navwarn_mixed.gml");
var warning = S124NavigationalWarning.From(dataset, out var diagnostics);

Console.WriteLine($"{warning.Preamble?.GeneralArea}, {warning.Preamble?.Locality}: {warning.Parts.Count} part(s)");
foreach (var diagnostic in diagnostics)
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
```

`From` throws only when the dataset is empty or has no root entity. Anything
else it can't read, such as an unresolved reference, an attribute that
doesn't parse or a feature without geometry, becomes a diagnostic, and the
projection continues.

> [!IMPORTANT]
> Check the diagnostics. A model built with warnings may be missing the
> entities they name.

Each typed object keeps the source attributes it didn't map in
`ExtraAttributes`.

[Reading product data](reading-product-data.md) shows typed models alongside
the other ways to read each product.

## Typed roots by product

| Product | Typed root | Contents |
|---|---|---|
| S-122 | `S122MarineProtectedAreaDataset` | Marine protected areas, restricted areas and VTS areas, with typed information-type bindings. |
| S-124 | `S124NavigationalWarning` | One navigational warning: its preamble, parts and references. |
| S-125 | `S125AtonDataset` | Marine aids to navigation, with status and quality information. |
| S-127 | `S127MarineServicesDataset` | Marine resources and services. |
| S-128 | `S128ProductCatalogue` | A catalogue of nautical products, with `Supersedes` and `SupersededBy` navigation between entries. |
| S-129 | `S129UnderKeelClearancePlan` | One under-keel-clearance management plan. |
| S-131 | `S131HarbourInfrastructureDataset` | Marine harbour infrastructure. |
| S-201 | `S201AtonInventory` | An IALA aids-to-navigation inventory. |
| S-411 | `S411SeaIceInventory` | Sea-ice and lake-ice features. |
| S-421 | `S421RoutePlan` | A route plan. |

Each typed root is in the product package's `DataModel` namespace, for
example `EncDotNet.S100.Datasets.S421.DataModel`. S-101 (ISO 8211) and the
HDF5 coverage products (S-102, S-104, S-111) have no typed model; read them
through their dataset types.

## Shared types

These types are in the `EncDotNet.S100.Core` package, in the
`EncDotNet.S100.DataModel` namespace:

- **`ProjectionDiagnostic`**: `Severity`, `Message`, `Code`, `RelatedId` and
  `RelatedAttribute`. Codes include `xlink.unresolved`,
  `attribute.parse.int`, `feature.duplicate` and `feature.geometry.missing`.
- **`DiagnosticSeverity`**: `Info`, `Warning` or `Error`.
- **`GeoPosition(double Latitude, double Longitude)`**: a read-only record
  struct in WGS-84 (EPSG:4326), with latitude first as in S-100 Part 10b
  §6.2.
- **`ProjectionContext`**: holds the diagnostics list and the xlink resolver
  that projection methods pass along.
- **`AttributeParser`**: `TryParseInt`, `TryParseDouble`, `TryParseBool` and
  `TryParseDateTimeOffset`. Parsing uses the invariant culture and ISO 8601
  round-trip formats, as in S-100 Part 5 §10. A failure adds an
  `attribute.parse.{type}` diagnostic.
- **`XlinkResolver`**: a lookup table by `gml:id`. It strips the leading `#`
  from an `xlink:href`. A missing target adds `xlink.unresolved`, and a target
  of the wrong type also adds a diagnostic.
- **`ExtraAttributes.ExcludeKnown(...)`**: keeps the source attributes that
  the typed model didn't map.

`GmlReference`, in the `EncDotNet.S100.Features` namespace of the same
package, represents an `xlink:href` cross-reference for every product.

## Add a typed model for a product

When you add a typed model for another product:

1. Put the types in `src/EncDotNet.S100.Datasets.Sxxx/DataModel/`, in the
   `EncDotNet.S100.Datasets.Sxxx.DataModel` namespace.
2. Provide a static factory,
   `SxxxRoot.From(SxxxDataset, out IReadOnlyList<ProjectionDiagnostic>)`.
3. Throw only for an empty dataset or a missing root entity. Report anything
   else as a diagnostic.
4. Build the xlink lookup with `XlinkResolver.Build(...)` from the dataset's
   features and information types. Pass it in a `ProjectionContext` through
   the projection methods.
5. Parse attribute values with `AttributeParser.TryParse*`. Don't throw when
   parsing fails.
6. Keep unmapped attributes with
   `ExtraAttributes.ExcludeKnown(attributes, ...known keys)`, so extensions and
   fields from later editions are preserved unchanged.
7. Use `GeoPosition` for coordinates and `GmlReference` for xlinks.
8. Keep the typed model separate from the portrayal pipeline. Portrayal must
   still run from the feature-bag dataset without building the typed model.
