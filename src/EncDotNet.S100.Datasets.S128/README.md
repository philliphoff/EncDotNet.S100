# EncDotNet.S100.Datasets.S128

Reader and portrayal-pipeline integration for IHO S-128 (Catalogue of
Nautical Products) datasets, encoded as GML per S-100 Part 10b.

## Upstream source

| Property | Value |
|---|---|
| Edition | **2.0.0** |
| Application namespace | `http://www.iho.int/S128/2.0` |
| Upstream repo | [`iho-ohi/S-128-Product-Specification-Development`](https://github.com/iho-ohi/S-128-Product-Specification-Development) |
| Pinned commit | `c266c43820ceadcf5b71ceb2a084c279c3a51801` |
| Bundled assets | FC + entire PC tree (byte-identical to upstream) |

The bundled FC and PC live under
`src/EncDotNet.S100.Specifications/content/S128/{fc,pc}/` and are surfaced
through the standard `Specification.OpenFeatureCatalogueAsync()` /
`Specification.CreatePortrayalCatalogueSource()` factory methods.

## What S-128 encodes

An S-128 dataset is a *catalogue* of the nautical products produced by a
single agency. Each "feature" describes one product and its coverage:

| Feature class | Encodes |
|---|---|
| `ElectronicProduct` | ENCs (S-101), digital cells, online services |
| `PhysicalProduct` | Paper charts and printed publications |
| `S100Service` | HDF5-based services such as S-104 / S-111 |

Plus metadata records modelled in the FC as information types but
encoded as features inside the inline `<S128:members>` container in the
upstream sample (`DistributorInformation`, `ProducerInformation`,
`ContactDetails`, `CatalogueSectionHeader`).

## Public surface

| Type | Purpose |
|---|---|
| `S128Dataset` | Root data model: `Features`, `InformationTypes`, lazy `Entries` projection |
| `S128DatasetReader` | GML parser. Use `S128Dataset.Open(path)` for the typical case |
| `S128Feature`, `S128InformationType` | FC-faithful feature/information instances |
| `S128ProductEntry` | Façade over an `S128Feature` whose type is one of the navigational product classes; surfaces strongly-typed accessors (`ProductSpecificationName`, `Status`, `CoverageRing`, etc.) |
| `S128ProductStatus` | Heuristic enum: `InForce | Superseded | Withdrawn | Planned | Unknown` |
| `S128CatalogueQuery` | Static helpers: `FilterByExtent`, `FilterByProductType`, `FilterBySpecification`, `FilterByStatus` |
| `S128ProductCatalogue` (`DataModel/`) | Strongly-typed projection of the dataset as a catalogue of typed `S128CatalogueEntry` subclasses with resolved `Supersedes`/`SupersededBy` navigation. See [Strongly-typed data model](#strongly-typed-data-model). |
| `S128FeatureXmlSource` | Projects the dataset into the S-100 Part 9 FeatureXML neutral form consumed by the bundled XSLT |
| `S128FeatureGeometryProvider` | `IFeatureGeometryProvider` adapter for the unified Mapsui display-list renderer |
| `S128PortrayalCatalogue` | `IVectorPortrayalCatalogue` over the bundled PC (Day / Dusk / Night palettes) |

## Producer-bug compensations

The reader inherits the four mitigations applied to other GML-encoded
products in this codebase (S-122 in particular). When real-world S-128
samples surface, edge cases here may need expanding.

1. **`s100gml` namespace tolerance.** Accepts
   `http://www.iho.int/s100gml/5.0` (canonical for 2.0.0),
   `http://www.iho.int/s100gml/1.0`, and the legacy profile namespace
   `http://www.iho.int/S100/profile/s100gml/1.0`.
2. **`<member>` and `<members>` containers.** Both the wrapper and inline
   variants are accepted; the upstream 2.0.0 sample uses inline
   `<S128:members>`.
3. **Comma-and-whitespace tokenisation in `gml:posList`/`gml:pos`.** Handles
   `lon,lat lon,lat` tokens smuggled in via the `gml:coordinates`
   convention.
4. **lon-lat axis-order swap heuristic.** When a `<gml:Envelope>` is
   present and parsed coords clearly fall outside as-is but inside when
   swapped, the reader globally flips axes. Skipped silently when no
   envelope is declared (the upstream 2.0.0 sample omits it).

## Status heuristic

S-128 2.0.0 does not surface a single `status` enum on product entries.
`S128ProductEntry.Status` is derived as follows:

1. If `serviceStatus` is present → `Planned (1)`, `InForce (2)`,
   `Withdrawn (3)`.
2. Else if `distributionStatus` is present → `InForce (1)` /
   `Withdrawn (2)`.
3. Otherwise defaults to `InForce`.

Resolution of `theReference` xlink references with
`ProductMapping/categoryOfProductMapping=1` (supersedes) is handled by
the typed-model projection — see
[Strongly-typed data model](#strongly-typed-data-model).

## Strongly-typed data model

`EncDotNet.S100.Datasets.S128.DataModel.S128ProductCatalogue` projects
the feature-bag `S128Dataset` into a strongly-typed catalogue with:

- Polymorphic `Products` collection over the common
  `S128CatalogueEntry` base; instances are sealed
  `S128ElectronicProduct`, `S128PhysicalProduct`, or `S128Service`.
- Dedicated typed records for `Producers`, `Distributors`,
  `Contacts`, and `SectionHeaders` metadata.
- **Resolved supersedes navigation.** Every `theReference` xlink with
  `ProductMapping/categoryOfProductMapping=1`
  ("Higher Priority Alternative", S-128 § 12) is resolved at
  projection time. Each entry exposes `Supersedes` (forward
  traversal) and `SupersededBy` (reverse traversal, populated by
  inverting the forward map). Cycles and chains tolerated.
- Other `categoryOfProductMapping` values surface in
  `RelatedProducts` with their raw category text preserved for
  future-edition compatibility.
- Permissive projection: unresolved xlinks and parse failures emit
  `ProjectionDiagnostic` entries rather than throwing. The projection
  only throws when both `Features` and `InformationTypes` are empty.

### Quick start (typed model)

```csharp
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S128.DataModel;

var dataset = S128Dataset.Open("catalogue.gml");
var catalogue = S128ProductCatalogue.From(dataset, out var diagnostics);

foreach (var product in catalogue.Products)
{
    Console.WriteLine($"{product.FeatureType} {product.Id} " +
        $"(ed. {product.EditionNumber}, spec {product.ProductSpecificationName})");

    foreach (var superseded in product.Supersedes)
        Console.WriteLine($"  supersedes → {superseded.Id}");

    foreach (var successor in product.SupersededBy)
        Console.WriteLine($"  superseded by → {successor.Id}");
}

foreach (var d in diagnostics)
    Console.WriteLine(d);
```

## Coordinate ordering

Per S-100 Part 10b §6.2, all coordinates inside `<gml:pos>` /
`<gml:posList>` for `EPSG:4326` are **lat lon**.

## Portrayal

The bundled `main.xsl` includes per-feature templates
(`ElectronicProduct.xsl`, `PhysicalProduct.xsl`, `S100Service.xsl`,
`DistributorInformation.xsl`) plus `simpleLineStyle.xsl` /
`textStyle.xsl` and a `Default.xsl` fallback. **No locally-authored XSLT
ships in this PR** — status-driven (in-force / superseded / withdrawn /
planned) styling is not in the upstream PC and is left as a TODO.

## Out of scope (this release)

- Status-driven local XSLT styling (in-force / superseded /
  withdrawn / planned). The data-model side of supersedes is now
  resolved via `S128ProductCatalogue`; the corresponding XSLT
  portrayal hook is still deferred.
- Auto-downloading datasets pointed at by `onlineResource.linkage` URLs.
- Dataset authoring / serialisation.
- Multi-language label resolution.
- A dedicated catalogue browser side panel in the viewer (datasets load
  through the existing pipeline and render as coverage polygons).
