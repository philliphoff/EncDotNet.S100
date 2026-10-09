# EncDotNet.S100.Datasets.S125

`EncDotNet.S100.Datasets.S125` reads
[IHO S-125](https://iho.int/en/s-100-based-product-specifications) Marine Aids
to Navigation datasets: GML files (S-100 Part 10b, S-100 GML 5.0 profile) that
describe lights, buoys, beacons, daymarks, AIS aids and other aids to
navigation. S-125 replaces the aid-to-navigation feature classes of S-57 and
S-101 with a separate product specification. This package parses a dataset,
projects it into a typed model, validates it, and portrays it. Reference it when
you need typed access to S-125 aids or their validation rules. To open and
render any product, including S-125, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package, which includes this
one.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S125
```

## Read a dataset

Open the dataset, then project it into the typed model:

```csharp
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S125.DataModel;

var dataset = S125Dataset.Open("path/to/aton.gml");
var typed = S125AtonDataset.From(dataset, out var diagnostics);

foreach (var aid in typed.Aids)
{
    var status = aid.Status?.IsOperational switch
    {
        true => "operational",
        false => "non-operational",
        null => "no status",
    };
    Console.WriteLine($"{aid.FeatureType} {aid.Id}: {status}");

    if (aid is S125AisAton ais && ais.IsVirtual)
        Console.WriteLine("  virtual AIS aid, no physical presence");

    if (aid.HostStructure is { } host)
        Console.WriteLine($"  mounted on {host.FeatureType} {host.Id}");
}
```

## Main types

- **`S125Dataset`**: the parsed dataset, with its features, information types
  and dataset identification. `Open` takes a path or a stream.
  `ReadMetadata` (static, for a path or stream, or on an open dataset) returns
  the declared product specification and the WGS 84 extent of the feature
  geometry without running portrayal. The extent is `null` when no feature has
  geometry.
- **`S125Feature`**: a feature with its type code, geometry, attributes and
  information references. It implements `IS100Feature`. `AttributeTree` holds
  the attributes as an ordered tree that keeps every value, including repeated
  values (such as several `colour` values) and nested complex attributes (such
  as `sectorCharacteristics/lightSector`). `Attributes` and `ComplexAttributes`
  are flat views that keep the last value of each.
- **`S125InformationType`**: an information type, such as
  `AtonStatusInformation`. It implements `IS100InformationType`.
- **`S125InformationReference`**: a link from a feature to an information
  type, read from an `xlink:href` or `informationRef` attribute.
- **`S125ComplexAttribute`**: a complex attribute's sub-attributes. It
  implements `IS100ComplexAttribute`.
- **`S125AtonDataset`**: the typed model. See
  [Typed data model](#typed-data-model).
- **`S125AtonRules`**: the validation rule set. See [Validate](#validate).
- **`S125FeatureXmlSource`**: an `IFeatureXmlSource` that converts an
  `S125Dataset` to the S-100 Part 9 FeatureXML form (`Dataset/Features/*` and
  `Dataset/InformationTypes/*`) that the S-125 XSLT rules match against.
- **`S125PortrayalCatalogue`**: an `IVectorPortrayalCatalogue` that loads the
  catalogue's XSLT rules, symbols, line styles, area fills and colour palettes.
- **`S125AtonPortrayalProjection`**, **`S125AtonLuaDataProvider`** and
  **`S125AtonLuaRuleExecutor`**: portray the aids with the S-101 Lua rules. See
  [Portrayal](#portrayal).

The reader extracts concrete aid features (`Landmark`, `LateralBuoy`,
`CardinalBeacon`, `LightSectored`, `VirtualAISAidToNavigation` and others) and
information types (`AtonStatusInformation`, `SpatialQuality`). Feature geometry
reaches the renderers through the shared `FeatureGeometryProvider<TFeature>`
from `EncDotNet.S100.Core`.

## Typed data model

`S125AtonDataset`, in the `EncDotNet.S100.Datasets.S125.DataModel` namespace, is
a read-only projection of `S125Dataset`. Its `Aids` are typed shapes
(`S125Buoy`, `S125Beacon`, `S125Light`, `S125AisAton`, `S125Structure`,
`S125Equipment`) that share the `IS125Aid` interface. The projection resolves
two kinds of link into properties:

- Status bindings: an aid's `AtoNStatus` xlink to its `AtonStatusInformation`
  becomes `Status`.
- Equipment on a structure: an equipment feature's `parent` xlink to its host
  beacon or landmark becomes `HostStructure`.

Unresolved xlinks, attributes that don't parse and similar problems become
`ProjectionDiagnostic` entries instead of exceptions. `From` throws only when
the dataset has no features and no information types.

## Validate

`S125AtonRules.Default`, in the `EncDotNet.S100.Datasets.S125.Validation`
namespace, is a `ValidationRuleSet<S125AtonDataset>` that checks the typed
model. The `EncDotNet.S100` package's `dataset.Validate()` runs the same rule
set.

```csharp
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S125.DataModel;
using EncDotNet.S100.Datasets.S125.Validation;

var dataset = S125Dataset.Open("path/to/aton.gml");
var typed = S125AtonDataset.From(dataset, out _);
var report = S125AtonRules.Validate(typed);

foreach (var finding in report.Findings)
    Console.WriteLine($"[{finding.Severity}] {finding.RuleId} {finding.RelatedFeatureId}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---|---|---|
| `S125-R-1.1` | Error | Aid positions are within the WGS 84 latitude and longitude ranges (S-100 Part 10b §6.2). |
| `S125-R-1.2` | Error | Aid `gml:id` values are unique within the dataset. |
| `S125-R-2.1` | Error | AIS aids have a nine-digit `mMSICode`. |
| `S125-R-3.1` | Error | `AtonStatusInformation.changeTypes` is between 1 and 5. |
| `S125-R-3.2` | Error | Status date ranges have `dateStart` on or before `dateEnd`. |
| `S125-R-4.1` | Warning | Each `AtonAggregation` or `AtonAssociation` binds at least one aid. |
| `S125-R-5.1` | Warning | Each `AtonStatusIndication` has a point geometry. |

Problems found while projecting, such as unresolved xlinks and duplicate IDs,
aren't on the typed model, so the rules don't report them. Read them from the
`out IReadOnlyList<ProjectionDiagnostic>` of `S125AtonDataset.From`.

## Portrayal

S-125 portrays the aids themselves with the S-101 portrayal catalogue's Lua
rules, and status changes with the S-125 catalogue. The S-125 specification
doesn't define symbols for the aids, so the first part is a choice this library
makes, not part of the specification.

### What the specification defines

S-125's portrayal only flags changes in aid status, drawn over the ENC's own
aid symbols.

- **Product specification.** S-125 *Marine Aids to Navigation (AtoN)*, Edition
  1.0.0, December 2025
  ([release `ed1.0.0`](https://github.com/iho-ohi/S-125-Product-Specification-Development/releases/tag/ed1.0.0)).
  Clause 13 *Portrayal* refers to the portrayal catalogue in Annex D. Clause 1
  places S-125 as a Nautical Publication Information Overlay (NPIO) for ECDIS,
  the digital form of the extended list of lights.
- **Portrayal catalogue (Annex D).**
  - The release's XSLT `PortrayalCatalogue.zip` has rules only for
    `AtoNStatusIndication`, `AtoNStatusInformation` and `DataCoverage`
    (`main.xsl` dispatches to those three).
  - Its symbols are the CHNG* change symbols and `QUESMRK1`.
  - The Lua draft on the repository's `main` branch (`PC/1.0.0`) has the same
    scope, plus a `Default` rule that draws `QUESMRK1`.
  - Neither has a rule for any buoy, beacon, light, daymark, landmark or AIS
    feature type.
  - This library bundles the release catalogue unchanged.
- **Design intent.** The IHO NIPWG S-125 portrayal paper (NIPWG9-08.2A, 2022,
  [slide 8](https://iho.int/uploads/user/Services%20and%20Standards/NIPWG/NIPWG9/NIPWG9_2022_08.2A_EN_Presentation_rev1.pdf))
  says S-125 doesn't replace or duplicate existing aid symbology, that the
  status symbol flags the ENC symbol beneath without obscuring it, and that
  interoperability could be extended later.
- **S-98 Interoperability, Edition 2.0.0.**
  - Main document §6.1.3 lists S-125 among products for future editions.
  - Annex A §15.4 mentions suppressing S-101 aids to navigation by S-125 at
    interoperability level 2. That's an example of what an interoperability
    catalogue could do; no published catalogue does it.

So the specification neither includes full aid symbology nor refers to S-101 or
S-52 for it. It assumes an ENC is displayed underneath. On its own, S-125 data
portrays as status flags only, and a dataset of buoys and lights draws as an
empty chart.

### S-101 rules for the aids

To draw the aids, this library runs the bundled S-101 portrayal catalogue's Lua
rules on them. The rules are used unchanged. They cover:

- buoy shapes and colour patterns (`LateralBuoy`, `CardinalBuoy` and others);
- topmarks (`TOPMAR02`);
- daymarks and landmarks;
- light flares, characteristics and sector legs (`LightAllAround`,
  `LightSectored`, `LightFlareAndDescription`, `LITDSN02`);
- AIS aids, navigation lines and recommended tracks.

Both feature catalogues take these feature types and attributes from the IHO
GI Registry, so most features map one to one. Both share
`sectorCharacteristics`, `rhythmOfLight` and the listed values for colour and
shape.

**`S125AtonPortrayalProjection`** handles the differences:

- S-125 `Topmark` equipment features move onto their structure as S-101's
  `topmark` complex attribute. The structure is found through the
  `StructureEquipment` `parent` and `child` roles, or by position.
- `SyntheticAISAidToNavigation` is portrayed as `PhysicalAISAidToNavigation`.
- `maximalPermittedDraught` becomes `maximumPermittedDraught`.
- `orientation` becomes `orientationValue` where S-101 binds the simple
  attribute.
- The pre-1.0 spellings `objectName` and `MMSICode` are read as `featureName`
  and `mMSICode`.
- Features the S-125 catalogue portrays itself (`AtonStatusIndication`,
  `DataCoverage`), and meta and aggregation features, aren't projected.

**`S125AtonLuaDataProvider`** serves the projected features to the S-101 rules
through the S-100 Part 9A host API. It uses the S-101 feature catalogue for type
information. Point features at the same position share one synthetic point
record. That reproduces S-101's structure and equipment co-location, which the
light-flare direction and the stacking of light descriptions depend on.

**`S125DatasetProcessor`**, in `EncDotNet.S100.Datasets.Pipelines`, merges the
S-101 aid instructions with the S-125 catalogue's own output (status flags drawn
over the aid) into one layer. Both use the S-101 colour palette: the S-125
colour profile uses the same S-52 tokens, and the CHNG* symbols keep their
S-125 stylesheets. Without a Lua engine or the S-101 catalogues, the processor
draws only the S-125 catalogue's output.

S-125's `obscuredSector` (on `LightSectored`) has no S-101 counterpart, so it
isn't portrayed.

### Display over an ENC

When an ENC portrays the same physical aids (S-98 interoperability):

- S-125 is on the *other chart overlays* plane, above the ENC's base and
  standard chart planes.
- The S-101 instructions go through the same viewing-group, display-mode and
  display-plane filtering as an ENC. Under the mariner's ECDIS display
  category, S-125 aids, including names and light descriptions, show and hide
  the same way as the ENC's.
- Where S-125 and the ENC encode the same aid at the same position, the symbols
  and sector legs coincide. The S-125 status flags (CHNG*) then mark the ENC's
  aid, which is the overlay the specification describes.
- No S-98 interoperability catalogue suppression of either product is applied.

## Encoding notes

- The S-125 application schema namespace is `http://www.iho.int/S125/1.0`.
  Geometry uses the S-100 GML 5.0 profile namespace,
  `http://www.iho.int/s100gml/5.0`. The reader also reads older sample datasets
  that declare the S-100 GML 1.0 profile.
- Coordinates in `<gml:pos>` and `<gml:posList>` are latitude then longitude
  for `EPSG:4326` (S-100 Part 10b).
- Some features have no geometry, such as the abstract supertypes
  `AtonAggregation` and `AtonAssociation`. Code that reads features must allow
  for that.
- Time validity (`fixedDateRange`, `periodicDateRange`) is UTC. Don't convert
  it to local time when you read it.

## Test data

[`tests/datasets/S125/aton_us4va1bf.gml`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tests/datasets/S125/aton_us4va1bf.gml)
holds the 178 aids to navigation of NOAA ENC US4VA1BF (Chesapeake Bay entrance;
US public domain). It was derived by
[`derive-s125-from-enc.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tests/datasets/S125/derive-s125-from-enc.cs):

- The ENC cell is translated from S-57 to S-101 in memory, then written as
  S-125 1.0.0 GML.
- Attributes are limited to the S-125 feature catalogue's bindings.
- Topmarks are written as `Topmark` features.

It passes `S125AtonRules` with no findings. The other `aton_*.gml` files in that
folder are small synthetic samples.

## License

The bundled S-125 specification assets in `EncDotNet.S100.Specifications` are
© IHO and are used under their open-publication terms; see
<https://github.com/iho-ohi/S-125-Product-Specification-Development>.

## See also

- [Loading datasets](../../docs/loading-datasets.md): open files, folders, ZIPs
  and exchange sets through the `EncDotNet.S100` package.
- [Reading product data](../../docs/reading-product-data.md): features,
  information types and typed models for each product.
- [Typed data models](../../docs/typed-data-models.md): the typed
  root for each product and the shared diagnostic codes.
- [Custom catalogues and validation](../../docs/catalogues-and-validation.md):
  run the bundled rules and add your own.
- [`EncDotNet.S100.Datasets.S201`](../EncDotNet.S100.Datasets.S201/README.md):
  the IALA S-201 aids to navigation product, for exchange between authorities.
