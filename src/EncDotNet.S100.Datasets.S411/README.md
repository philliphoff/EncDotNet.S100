# EncDotNet.S100.Datasets.S411

`EncDotNet.S100.Datasets.S411` reads
[IHO/JCOMM S-411](https://iho.int/en/s-411-ice-information) Ice Information for
Surface Navigation datasets: GML files (S-100 Part 10b) that describe sea ice
and lake ice. It reads both the JCOMM operational encoding and the IHO sample
encoding, projects a dataset into a typed inventory, validates it, and portrays
it with the S-411 portrayal catalogue in three display modes. Reference it when
you need typed access to S-411 ice data or its validation rules. To open and
render any product, including S-411, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S411
```

## Read a dataset

Open the dataset, then project it into the typed inventory:

```csharp
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S411.DataModel;

using var stream = File.OpenRead("path/to/ice.gml");
var dataset = S411Dataset.Open(stream);
var inventory = S411SeaIceInventory.From(dataset, out var diagnostics);

foreach (var seaIce in inventory.IceFeatures.OfType<S411SeaIce>())
{
    var concentration = seaIce.EggCode?.TotalConcentration;
    Console.WriteLine($"{seaIce.NormalizedFeatureType} ({seaIce.GeometryKind}): total concentration {concentration}");
}
```

## Main types

- **`S411Dataset`**: the parsed dataset, with its features and dataset
  identification. `Open` takes a path or a stream. `SourceDocument` is the
  parsed `XDocument`. `ReadMetadata` (static, for a path or stream, or on an
  open dataset) returns the declared product specification and the WGS 84
  extent of the feature geometry without running portrayal. The extent is
  `null` when no feature has geometry.
- **`S411Feature`**: a feature with its type code, geometry, simple attributes
  and complex attributes. **`S411ComplexAttribute`** holds a complex
  attribute's sub-attributes. The geometry kind is the shared
  `S100GeometryType` enum from `EncDotNet.S100.Core`.
- **`S411SeaIceInventory`**: the typed model. See
  [Typed data model](#typed-data-model).
- **`S411SeaIceRules`**: the validation rule set. See [Validate](#validate).
- **`S411FeatureXmlSource`**: an `IFeatureXmlSource` that converts an
  `S411Dataset` to S-100 Part 9 FeatureXML for the XSLT rules.
- **`S411PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules, symbols, line styles, area fills and colour palettes.

The reader extracts the ice features (`SeaIce`, `LakeIce`, `Iceberg`,
`IceEdge`, `IceLead` and others; see the S-411 1.2.1 feature catalogue for the
full set). S-411 has no information types (`<imember>` elements). Feature
geometry reaches the renderers through the shared
`FeatureGeometryProvider<TFeature>` from `EncDotNet.S100.Core`.

## Typed data model

`S411SeaIceInventory`, in the `EncDotNet.S100.Datasets.S411.DataModel`
namespace, is a read-only projection of `S411Dataset`, built on the shared
types in the `EncDotNet.S100.DataModel` namespace. The portrayal pipeline
doesn't use it; it reads `S411Dataset` directly.

- **`S411SeaIceInventory.From(S411Dataset, out IReadOnlyList<ProjectionDiagnostic>)`**
  builds the inventory. `IceFeatures` lists the typed features.
- **`S411IceFeature`** is the abstract base. The concrete classes are
  `S411SeaIce`, `S411LakeIce`, `S411Iceberg`, `S411IceEdge`, `S411IceLead`,
  `S411IceThickness`, `S411SnowCover`, `S411StageOfMelt`, `S411DataCoverage`,
  and `S411OtherFeature` for anything else.
- **`S411EggCode`** holds the WMO egg-code attributes of `SeaIce` and `LakeIce`
  features. Both vocabularies, JCOMM (`iceact`, `iceapc`, `icesod`, `iceflz`)
  and the IHO 1.2.1 sample (`totalConcentration`, `snowDepth`), fill the same
  type. List-valued JCOMM attributes are kept as raw text, because real
  producers write them as Python-style list strings rather than standard WMO
  tokens.
- **`S411GeometryKind`** is `None`, `Point`, `Curve` or `Surface`.

The projection maps the JCOMM short codes (`seaice`, `lacice`, `icebrg`,
`icelne`, `icethk`, `snwcvr`, `stgmlt` and others) to the feature catalogue's
class names. Both encodings produce the same typed classes, so you can use
`NormalizedFeatureType` without knowing which encoding the dataset uses. The
element name as encoded is in `SourceFeatureType`.

S-411 has no information types and no xlinks, so the projection doesn't resolve
references. Attributes that don't parse become `ProjectionDiagnostic` entries.
`From` throws only when the dataset has no features.

## Validate

`S411SeaIceRules`, in the `EncDotNet.S100.Datasets.S411.Validation` namespace,
is the default rule set for `S411SeaIceInventory`. Rule IDs have the form
`S411-R-{clause}`. The rules read the typed model, so they work the same for
both encodings. The `EncDotNet.S100` package's `dataset.Validate()` runs the
same rule set.

```csharp
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S411.DataModel;
using EncDotNet.S100.Datasets.S411.Validation;

var dataset = S411Dataset.Open("path/to/ice.gml");
var inventory = S411SeaIceInventory.From(dataset, out _);
var report = S411SeaIceRules.Validate(inventory);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId}: {finding.Message}");
```

To run your own selection of rules, build a
`ValidationRuleSet<S411SeaIceInventory>`.

| Rule ID | Severity | Checks |
|---|---|---|
| `S411-R-3.1` | Error | Feature coordinates are within the WGS 84 latitude and longitude ranges (S-100 Part 10b §6.2). |
| `S411-R-3.2` | Error | Surface features have a closed ring with at least four coordinates. |
| `S411-R-3.3` | Error | Curve features have at least two vertices. |
| `S411-R-4.1` | Warning | Egg-code `totalConcentration` is one of the WMO codes listed in S-411 Annex A. |
| `S411-R-4.2` | Error | `iceAverageThickness`, when present, is at least 0. |
| `S411-R-4.3` | Error | Egg-code `snowDepth`, when present, is at least 0. |
| `S411-R-4.4` | Warning | `icebergSize` is one of the codes listed in S-411 Annex A (1 to 9, 99). |
| `S411-R-5.1` | Error | Feature identifiers are unique in the dataset. |

## Portrayal

The bundled portrayal catalogue under `EncDotNet.S100.Specifications` is
byte-identical to the upstream catalogue at
[iho-ohi/S-411-Product-Specification](https://github.com/iho-ohi/S-411-Product-Specification)
(version 1.2.1), with nothing edited or added.

### Adapter for the main rule

The upstream `mainRule` (`pc/Rules/main.xsl`) writes a display-list format that
this library's `Part9DisplayListReader` can't read. For example, it writes
`<symbol><symbolReference>X</symbolReference></symbol>` instead of
`<symbol reference="X"/>`. So this library includes an adapter,
`Adapter/main.xsl`, and `S411PortrayalCatalogue.GetCompiledRuleAsync("mainRule")`
returns the adapter in place of the catalogue's `mainRule`. All other rules,
such as sub-templates and simple-symbol templates, load from the unchanged
catalogue. The adapter handles both encodings.

The catalogue has several top-level XSLT rules: `mainRule`, plus rules for each
ice class, such as `SeaiceClass1ARule`. Only `mainRule` is an active portrayal
rule by default. The ice-class rules still load by name through
`GetCompiledRuleAsync`.

### Display modes

Each S-411 polygon carries the full WMO egg code (`iceact`, `iceapc`, `icesod`,
`iceflz`), and the upstream catalogue declares three display modes (S-100 Part 9
§11.7). The adapter takes the active mode as an
`<xsl:param name="displayMode"/>`, which the vector engine sets from the
catalogue's `DisplayModeController`.

| Display mode ID | `s100 render --display-mode` | Fill |
|---|---|---|
| `IceScientificIceactDisplayMode` (default) | `ice-concentration` | Total concentration, in the WMO `iceact` colours. |
| `IceScientificIcesodDisplayMode` | `ice-sod` | Stage of development, in the WMO `icesod` colours. |
| `IceNavigationalDisplayMode` | `ice-navigational` | A provisional preview derived from total concentration, written for the adapter. |

> [!WARNING]
> The navigational mode isn't a POLARIS or RIO navigational-risk calculation.
> S-411 1.2.1 has no colours for it, so the adapter uses a provisional
> traffic-light scale based on total concentration.

The concentration and stage-of-development colours are written into the
adapter as `#RRGGBB` values, copied from the bundled upstream tables
(`pc/Rules/seaice_wmo_iceact.xsl` and `seaice_wmo_icesod.xsl`). Those upstream
files stay the source of the values and stay byte-identical to upstream.
`S411WmoColourParityTests` parses them, converts each
`number($iceX)=N -> colorToken` "R G B" entry to `#RRGGBB`, and checks that the
adapter's tables match. If upstream changes, the build fails. This avoids
reading the upstream tables with `document()` at render time.

When an egg code isn't in the upstream table (real Canadian Ice Service feeds
use tenths and list-style codes), the concentration mode uses a colour scale,
written for the adapter, based on the code's leading digit.

From the command line, `s100 info <dataset>` lists the available modes, and
`s100 render <dataset> --display-mode <token>` selects one. See
[Command-line rendering](../../docs/cli.md).

## Encoding notes

S-411 1.2.1 datasets come in two XML shapes. The reader accepts both:

1. **JCOMM / Canadian Ice Service operational shape**, the common case in real
   data. The root element is
   `<ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice">`. Each feature is in
   its own `<ice:IceFeatureMember>`, and feature elements use the short codes
   (`ice:seaice`, `ice:icebrg`, `ice:lacice`, `ice:icelne` and others).
   Geometry is a direct `<gml:Polygon>`, `<gml:LineString>` or `<gml:Point>`
   child. The bundled portrayal catalogue was written for this shape.
2. **IHO 1.2.1 sample shape**, seen only in the `samples/` folder of the IHO
   `S-411-Product-Specification` repository. The root is a bare `<Dataset>`
   with one `<members>` container for all features, and feature class names
   are PascalCase (`SeaIce`, `Iceberg` and others). The dataset identification
   declares the specification with
   `<S100:productIdentifier>S-411</S100:productIdentifier>`.

The reader chooses by the root element. It keeps the parsed `XDocument` in
`S411Dataset.SourceDocument` and passes it unchanged to the XSLT portrayal, so
the catalogue sees the element names and namespaces it expects.

- The reader accepts both the `s100gml/1.0` and `s100gml/5.0` profile
  namespaces.
- Coordinates in `<gml:pos>` and `<gml:posList>` are latitude then longitude
  for `EPSG:4326` (S-100 Part 10b).

## License

The bundled S-411 specification assets in `EncDotNet.S100.Specifications` are
© JCOMM/IHO and are used under their open-publication terms; see
<https://github.com/iho-ohi/S-411-Product-Specification>.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders, ZIPs
  and exchange sets through the `EncDotNet.S100` package.
- [Reading product data](../../docs/reading-product-data.md): features,
  information types and typed models for each product.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.
- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  run the bundled rules and add your own.
