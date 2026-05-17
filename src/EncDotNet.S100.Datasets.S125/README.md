# EncDotNet.S100.Datasets.S125

Library for reading and portraying [IHO S-125](https://iho.int/en/s-100-based-product-specifications) (Marine Aids to Navigation) datasets.

S-125 supersedes the AtoN (Aids to Navigation) feature classes from S-57 / S-101 with a stand-alone S-100-based product specification covering lights, buoys, beacons, daymarks, AIS aids, and other aids to navigation.

## Features

- Parse S-125 GML datasets (S-100 Part 10b encoding using the S-100 GML 5.0 profile)
- Extract concrete AtoN features (`Landmark`, `LateralBuoy`, `CardinalBeacon`, `LightSectored`, `VirtualAISAidToNavigation`, …) and information types (`AtonStatusInformation`, `SpatialQuality`)
- Preserve information bindings (`xlink:href` / `informationRef`) so the XSLT portrayal rules can resolve cross-references
- Project to the S-100 Part 9 FeatureXML neutral form (`Dataset/Features/*` plus `Dataset/InformationTypes/*`) consumed by the S-125 portrayal catalogue
- XSLT-based portrayal via the S-125 Portrayal Catalogue

## Overview

Key types:

- **`S125Dataset`** — root model containing parsed features, information types, and dataset identification.
- **`S125Feature`** — a geographic feature with type code, geometry, simple/complex attributes, and information references. Implements `IGmlFeature`.
- **`S125InformationType`** — an information type instance (e.g. `AtonStatusInformation`). Implements `IGmlInformationType`.
- **`S125InformationReference`** — a feature → information-type association captured from `xlink:href` / `informationRef` attributes.
- **`S125ComplexAttribute`** — a complex attribute group with sub-attribute values. Implements `IGmlComplexAttribute`.
- **`GmlGeometryType`** — shared enum (from `EncDotNet.S100.Core`) describing the geometry primitive type of a feature.
- **`S125FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S125Dataset` into the synthesized `Dataset/Features/*` shape that S-125 XSLT rules match against.
- **`S125FeatureGeometryProvider`** — `IFeatureGeometryProvider` adapter for the unified Mapsui display-list renderer.
- **`S125PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.

## Strongly-typed data model

The `EncDotNet.S100.Datasets.S125.DataModel` namespace provides a
read-only projection of `S125Dataset` into a domain-shaped object graph
rooted at `S125AtonDataset`. Aids are exposed as typed shapes
(`S125Buoy`, `S125Beacon`, `S125Light`, `S125AisAton`, `S125Structure`,
`S125Equipment`) under the common `IS125Aid` interface. AtoN status
bindings (`AtoNStatus` xlink → `AtonStatusInformation`) and
equipment-on-structure relationships (`parent` xlink → host beacon /
landmark) are resolved into navigable properties so callers can ask
domain questions without walking the feature bag.

The projection is permissive — unresolved xlinks, attribute parse
failures, and similar issues surface as `ProjectionDiagnostic` entries
rather than exceptions. Only a fully empty dataset (no features and no
information types) causes `From(...)` to throw.

### Quick start (typed model)

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
        Console.WriteLine("  (virtual AIS — no physical presence)");

    if (aid.HostStructure is { } host)
        Console.WriteLine($"  mounted on {host.FeatureType} {host.Id}");
}
```

## Validation

The `EncDotNet.S100.Datasets.S125.Validation` namespace exposes
`S125AtonRules.Default`, a `ValidationRuleSet<S125AtonDataset>` of
normative checks built on the typed `S125AtonDataset` projection. The
pilot pack covers:

| Rule | Severity | Summary |
| ---- | -------- | ------- |
| `S125-R-1.1` | Error   | Aid position lat/lon in WGS-84 ranges (S-100 Part 10b §6.2) |
| `S125-R-1.2` | Error   | Aid `gml:id` unique within the dataset |
| `S125-R-2.1` | Error   | AIS aid carries a 9-digit `mMSICode` |
| `S125-R-3.1` | Error   | `AtonStatusInformation.changeTypes` in {1..5} |
| `S125-R-3.2` | Error   | Status date range `dateStart ≤ dateEnd` |
| `S125-R-4.1` | Warning | `AtonAggregation` / `AtonAssociation` binds ≥ 1 aid |
| `S125-R-5.1` | Warning | `AtonStatusIndication` has a point geometry |

```csharp
var raw = S125Dataset.Open("aids.gml");
var typed = S125AtonDataset.From(raw, out _);
var report = S125AtonRules.Validate(typed);
foreach (var f in report.Findings)
    Console.WriteLine($"{f.Severity} {f.RuleId} {f.RelatedFeatureId}: {f.Message}");
```

Projection-time issues (unresolved xlinks, duplicate ids) are not
exposed on the typed model and so are not surfaced as rules; capture
the `out IReadOnlyList<ProjectionDiagnostic>` from
`S125AtonDataset.From` if you need them.

## Notes

- S-125 application schema namespace is `http://www.iho.int/S125/1.0`; geometry uses the S-100 GML 5.0 profile namespace `http://www.iho.int/s100gml/5.0`. Older sample datasets that still declare the S-100 GML 1.0 profile are read transparently.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` follows the S-100 Part 10b convention of **lat lon** for `EPSG:4326`.
- The bundled portrayal catalogue is the development edition from [`iho-ohi/S-125-Product-Specification-Development`](https://github.com/iho-ohi/S-125-Product-Specification-Development); at the time of writing it ships only AtoN status indication, AtoN status information, and DataCoverage rules. Per-feature-class symbology is therefore sparse and renderable output is limited until the upstream catalogue is fleshed out.
- Renderers must tolerate geometry-less features — abstract supertypes such as `AtonAggregation` and `AtonAssociation` carry no geometry.
- Time validity (`fixedDateRange`, `periodicDateRange`) is interpreted as UTC; do not coerce to local time at the source.

## License

The bundled S-125 specification assets in `EncDotNet.S100.Specifications` are © IHO and used in accordance with their open-publication terms; see <https://github.com/iho-ohi/S-125-Product-Specification-Development>.
