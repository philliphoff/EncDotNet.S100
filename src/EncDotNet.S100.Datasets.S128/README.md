# EncDotNet.S100.Datasets.S128

`EncDotNet.S100.Datasets.S128` reads IHO S-128 Catalogue of Nautical Products
datasets: GML files (S-100 Part 10b) in which one agency lists the nautical
products it produces and their coverage. It parses a dataset, offers queries
over its product entries, projects it into a typed catalogue with resolved
supersession links, validates it, and portrays the coverages. Reference it when
you need to query an S-128 catalogue or validate it. To open and render any
product, including S-128, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S128
```

## Read a dataset

Open the dataset, then project it into the typed catalogue:

```csharp
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S128.DataModel;

var dataset = S128Dataset.Open("path/to/catalogue.gml");
var catalogue = S128ProductCatalogue.From(dataset, out var diagnostics);

foreach (var product in catalogue.Products)
{
    Console.WriteLine($"{product.FeatureType} {product.Id} " +
        $"(edition {product.EditionNumber}, spec {product.ProductSpecificationName})");

    foreach (var superseded in product.Supersedes)
        Console.WriteLine($"  supersedes {superseded.Id}");

    foreach (var successor in product.SupersededBy)
        Console.WriteLine($"  superseded by {successor.Id}");
}

foreach (var diagnostic in diagnostics)
    Console.WriteLine(diagnostic);
```

## Main types

| Type | Purpose |
|---|---|
| `S128Dataset` | The parsed dataset: `Features`, `InformationTypes`, and `Entries`, a lazily built list of product entries. `Open` takes a path or a stream. `ReadMetadata` (static, for a path or stream, or on an open dataset) returns the declared product specification and the WGS 84 extent of the feature geometry without running portrayal; the extent is `null` when no feature has geometry. |
| `S128Feature`, `S128InformationType` | Features and information types as the feature catalogue defines them. |
| `S128ProductEntry` | A view of an `S128Feature` whose type is a product class, with typed accessors such as `ProductSpecificationName`, `Status` and `CoverageRing`. |
| `S128ProductStatus` | The derived status of an entry: `InForce`, `Superseded`, `Withdrawn`, `Planned` or `Unknown`. See [Product status](#product-status). |
| `S128CatalogueQuery` | Static filters over entries: `FilterByExtent`, `FilterByProductType`, `FilterBySpecification` and `FilterByStatus`. |
| `S128ProductCatalogue` | The typed catalogue, in the `DataModel` namespace. See [Typed data model](#typed-data-model). |
| `S128CatalogueRules` | The validation rule set, in the `Validation` namespace. See [Validate](#validate). |
| `S128FeatureXmlSource` | Converts the dataset to the S-100 Part 9 FeatureXML form that the bundled XSLT reads. |
| `S128PortrayalCatalogue` | An `IVectorPortrayalCatalogue` over the bundled portrayal catalogue (Day, Dusk and Night palettes), with an outline-only adapter on the `main` rule. See [Portrayal](#portrayal). |

## What an S-128 dataset contains

Each feature describes one product and its coverage:

| Feature class | Describes |
|---|---|
| `ElectronicProduct` | ENCs (S-101), digital cells, online services |
| `PhysicalProduct` | Paper charts and printed publications |
| `S100Service` | HDF5-based services such as S-104 and S-111 |

The upstream sample also has metadata records that the feature catalogue models
as information types but that are encoded as features inside the inline
`<S128:members>` container: `DistributorInformation`, `ProducerInformation`,
`ContactDetails` and `CatalogueSectionHeader`.

## Typed data model

`S128ProductCatalogue`, in the `EncDotNet.S100.Datasets.S128.DataModel`
namespace, projects `S128Dataset` into a typed catalogue:

- `Products` lists every product through the `S128CatalogueEntry` base class.
  Each entry is an `S128ElectronicProduct`, `S128PhysicalProduct` or
  `S128Service`.
- `Producers`, `Distributors`, `Contacts` and `SectionHeaders` hold the
  metadata records.
- **Supersession is resolved.** Every `theReference` xlink with
  `ProductMapping/categoryOfProductMapping=1` ("Higher Priority Alternative",
  S-128 §12) is resolved during projection. Each entry's `Supersedes` lists the
  products it supersedes, and `SupersededBy` lists the products that supersede
  it. Chains and cycles are allowed.
- References with other `categoryOfProductMapping` values are in
  `RelatedProducts`, with the category text kept as encoded.
- Unresolved xlinks and values that don't parse become `ProjectionDiagnostic`
  entries. `From` throws only when the dataset has no features and no
  information types.

## Product status

S-128 2.0.0 has no single status attribute on a product. `S128ProductEntry.Status`
derives one:

1. If `serviceStatus` is present: 1 is `Planned`, 2 is `InForce`, 3 is
   `Withdrawn`.
2. Otherwise, if `distributionStatus` is present: 1 is `InForce`, 2 is
   `Withdrawn`.
3. Otherwise, the status is `InForce`.

Supersession comes from the typed catalogue's `Supersedes` and `SupersededBy`,
not from `Status`.

## Validate

`S128CatalogueRules`, in the `EncDotNet.S100.Datasets.S128.Validation`
namespace, is the default rule set for `S128ProductCatalogue`. Rule IDs have the
form `S128-R-{clause}` and trace to S-128 Edition 2.0.0 (§12, Feature
Catalogue), or to S-100 Part 10b §6 for geometry. The `EncDotNet.S100` package's
`dataset.Validate()` runs the same rule set.

```csharp
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S128.DataModel;
using EncDotNet.S100.Datasets.S128.Validation;

await using var stream = File.OpenRead("path/to/catalogue.gml");
var dataset = S128Dataset.Open(stream);
var catalogue = S128ProductCatalogue.From(dataset, out _);
var report = S128CatalogueRules.Validate(catalogue);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---|---|---|
| `S128-R-12.1` | Error | Each entry's `editionNumber`, when present, is at least 1. |
| `S128-R-12.2` | Error | When both are present, `issueDate` is on or before `updateDate`. |
| `S128-R-12.3` | Error | Coverage coordinates are within the WGS 84 latitude and longitude ranges. |
| `S128-R-12.4` | Error | Surface exterior rings have at least four vertices and are closed. |
| `S128-R-12.5` | Error | Product `gml:id` values are unique in the catalogue. |
| `S128-R-12.6` | Warning | `onlineResource/linkage` values, when present, parse as absolute URIs. |
| `S128-R-12.7` | Warning | The catalogue has at least one `ProducerInformation` or `DistributorInformation` record. |

The rules check one catalogue at a time. Checking entries against the datasets
you've actually loaded isn't included.

## Portrayal

The bundled portrayal catalogue is the upstream IHO one, unchanged. Its
`main.xsl` includes per-feature templates (`ElectronicProduct.xsl`,
`PhysicalProduct.xsl`, `S100Service.xsl`, `DistributorInformation.xsl`), plus
`simpleLineStyle.xsl`, `textStyle.xsl` and a `Default.xsl` fallback. This
library adds no styling of its own; for example, entries aren't styled by
status.

### Outline-only coverages

The upstream rules fill each product's coverage with `CHYLW`, `CHGRN` or
`CHMGD` at transparency 0.30 (70% opaque) on display plane `OVERRADAR`, and the
S-98 layer stack puts S-128 on `OtherChartOverlays`, above the ENC's line work.
Nested products, such as a harbour cell inside an approach cell, add up to
about 91% opaque and hide the chart, contrary to S-98 Main §9.2.1.

Upstream reached the same view:
[issue #51](https://github.com/iho-ohi/S-128-Product-Specification-Development/issues/51)
recommends outlines only, and the Lua port of the catalogue
([pull request #56](https://github.com/iho-ohi/S-128-Product-Specification-Development/pull/56))
comments the fills out. `S128PortrayalCatalogue` does the same with an adapter,
`Adapter/outlineOnly.xsl`. The adapter imports the upstream `main.xsl` with
`xsl:import`. For `ElectronicProduct`, `PhysicalProduct` and `S100Service`
surfaces, it keeps everything the upstream rule emits except its
`areaInstruction`, so coverages draw as the upstream dashed outlines.

`S128CoverageOverlayTests` checks that the adapter's output equals the upstream
output without the fills, and that the upstream rule still emits them. When a
catalogue update drops the fills, that test fails and the adapter can be
removed. Moving to the upstream Lua catalogue is tracked in
[issue #763](https://github.com/philliphoff/EncDotNet.S100/issues/763).

## Encoding notes

Coordinates in `<gml:pos>` and `<gml:posList>` are latitude then longitude for
`EPSG:4326` (S-100 Part 10b §6.2).

The reader handles these producer variations, most of which it shares with the
other GML products:

1. **`s100gml` namespaces.** It accepts `http://www.iho.int/s100gml/5.0` (the
   namespace for 2.0.0), `http://www.iho.int/s100gml/1.0`, and the older
   profile namespace `http://www.iho.int/S100/profile/s100gml/1.0`.
2. **`<member>` and `<members>` containers.** It accepts both the wrapper and
   the inline form. The upstream 2.0.0 sample uses inline `<S128:members>`.
3. **Commas in `gml:posList` and `gml:pos`.** It accepts `lon,lat lon,lat`
   pairs written in the `gml:coordinates` style.
4. **Longitude-first coordinates.** When the dataset has a `<gml:Envelope>`,
   and the parsed coordinates clearly fall outside it as parsed but inside it
   when swapped, the reader swaps the axes for the whole dataset. Without an
   envelope it doesn't check; the upstream 2.0.0 sample has none.
5. **`gml:gmlId` identifiers.** Some S-128 GML 1.0 catalogue datasets from
   IC-ENC and Denmark identify features with a non-standard `gml:gmlId`
   attribute instead of `gml:id`. The reader accepts either. Without this, the
   feature identifier is empty and the feature doesn't render.
6. **Polygons without an exterior.** Some GML 1.0 datasets write
   `<gml:Polygon><gml:posList>...` directly, without the
   `<gml:exterior>/<gml:LinearRing>` wrapper. The shared `GmlCoordinateParser`
   reads a `posList` or `pos` sequence directly under the surface as the
   exterior ring.
7. **One ordinate per `<gml:pos>`.** Some GML 1.0 datasets split each
   coordinate across consecutive single-value `<gml:pos>` elements
   (`<gml:pos>41.68</gml:pos><gml:pos>21.61</gml:pos>...`). The shared
   `GmlCoordinateParser` detects this and pairs the values as latitude and
   longitude.

Items 5 to 7 recover geometry from S-128 GML 1.0 catalogues, which otherwise
render blank. Two GML 1.0 gaps remain, tracked in
[issue #247](https://github.com/philliphoff/EncDotNet.S100/issues/247): full
portrayal of the GML 1.0 feature classes (`ElectronicChart`, `PaperChart` and
others), and a reliable axis order for longitude-first datasets that have no
`<gml:Envelope>`.

## Limitations

- Entries aren't styled by status (in force, superseded, withdrawn, planned).
  The typed catalogue resolves supersession, but the portrayal doesn't use it.
- Datasets that `onlineResource/linkage` URLs point to aren't downloaded.
- Writing S-128 datasets isn't supported.
- Labels aren't resolved by language.
- The viewer has no catalogue browser panel for S-128. Datasets load through the
  standard pipeline and draw as coverage outlines.

## Bundled specification

| Property | Value |
|---|---|
| Edition | 2.0.0 |
| Application namespace | `http://www.iho.int/S128/2.0` |
| Upstream repository | [`iho-ohi/S-128-Product-Specification-Development`](https://github.com/iho-ohi/S-128-Product-Specification-Development) |
| Pinned commit | `c266c43820ceadcf5b71ceb2a084c279c3a51801` |
| Bundled assets | Feature catalogue and the whole portrayal catalogue, byte-identical to upstream |

The feature and portrayal catalogues are in
[`src/EncDotNet.S100.Specifications/content/S128/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/src/EncDotNet.S100.Specifications/content/S128)
and load through `Specification.OpenFeatureCatalogueAsync()` and
`Specification.CreatePortrayalCatalogueSource()`.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders, ZIPs
  and exchange sets through the `EncDotNet.S100` package.
- [Reading product data](../../docs/reading-product-data.md): features,
  information types and typed models for each product.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.
- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  run the bundled rules and add your own.
