# EncDotNet.S100.Datasets.S125

Library for reading and portraying [IHO S-125](https://iho.int/en/s-100-based-product-specifications) (Marine Aids to Navigation) datasets.

S-125 supersedes the AtoN (Aids to Navigation) feature classes from S-57 / S-101 with a stand-alone S-100-based product specification covering lights, buoys, beacons, daymarks, AIS aids, and other aids to navigation.

## Features

- Parse S-125 GML datasets (S-100 Part 10b encoding using the S-100 GML 5.0 profile)
- Extract concrete AtoN features (`Landmark`, `LateralBuoy`, `CardinalBeacon`, `LightSectored`, `VirtualAISAidToNavigation`, …) and information types (`AtonStatusInformation`, `SpatialQuality`)
- Preserve information bindings (`xlink:href` / `informationRef`) so the XSLT portrayal rules can resolve cross-references
- Project to the S-100 Part 9 FeatureXML neutral form (`Dataset/Features/*` plus `Dataset/InformationTypes/*`) consumed by the S-125 portrayal catalogue
- XSLT-based portrayal via the S-125 Portrayal Catalogue (AtoN status indications, data coverage)
- AtoN symbology (buoys, beacons, topmarks, daymarks, landmarks, lights and light sectors, AIS aids, navigation lines, recommended tracks) via the bundled S-101 Portrayal Catalogue's Lua rules — see [Portrayal](#portrayal)

## Overview

Key types:

- **`S125Dataset`** — root model containing parsed features, information types, and dataset identification. `ReadMetadata()` (plus static `ReadMetadata(path)` / `ReadMetadata(stream)`) is the phased-loading "peek" path (issue #460): it returns a `DatasetMetadata` with the declared spec and the raw WGS-84 extent folded from feature geometry (`null` when the dataset carries only geometry-less container features), skipping the XSLT portrayal pipeline.
- **`S125Feature`** — a geographic feature with type code, geometry, simple/complex attributes, and information references. Implements `IS100Feature`.
- **`S125InformationType`** — an information type instance (e.g. `AtonStatusInformation`). Implements `IS100InformationType`.
- **`S125InformationReference`** — a feature → information-type association captured from `xlink:href` / `informationRef` attributes.
- **`S125ComplexAttribute`** — a complex attribute group with sub-attribute values. Implements `IS100ComplexAttribute`.
- **`S100GeometryType`** — shared enum (from `EncDotNet.S100.Core`) describing the geometry primitive type of a feature.
- **`S125FeatureXmlSource`** — `IFeatureXmlSource` adapter that projects an `S125Dataset` into the synthesized `Dataset/Features/*` shape that S-125 XSLT rules match against.
- **Feature geometry** reaches the renderers through the shared `FeatureGeometryProvider<TFeature>` (`IFeatureGeometryProvider`, from `EncDotNet.S100.Core`), which the dataset processor builds over the parsed features.
- **`S125PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT rules, symbols, line styles, area fills, and color palettes.
- **`S125AtonPortrayalProjection`** / **`S125AtonLuaDataProvider`** / **`S125AtonLuaRuleExecutor`** — portray the aids themselves with the bundled S-101 AtoN Lua rules (see [Portrayal](#portrayal)).
- **`S125Feature.AttributeTree`** — the feature's attributes as an ordered, lossless tree (repeated values such as multi-colour `colour`, nested complexes such as `sectorCharacteristics/lightSector`); `Attributes` / `ComplexAttributes` are flat last-value views.

## Portrayal

### What the S-125 specification provides

S-125 does **not** define symbology for the aids themselves. Its portrayal is
deliberately limited to flagging AtoN *status changes* on top of the ENC's own
AtoN symbols:

- **Product Specification.** S-125 *Marine Aids to Navigation (AtoN)*,
  Edition 1.0.0, December 2025
  ([release `ed1.0.0`](https://github.com/iho-ohi/S-125-Product-Specification-Development/releases/tag/ed1.0.0)).
  Clause 13 *Portrayal* says only that "The Portrayal Catalogue is found at
  Annex D". Clause 1 positions S-125 as a Nautical Publication Information
  Overlay (NPIO) for ECDIS: "the digital equivalent of the extended list of
  lights".
- **Portrayal Catalogue (Annex D).**
  - The release's `PortrayalCatalogue.zip` (XSLT) has rules only for
    `AtoNStatusIndication`, `AtoNStatusInformation` and `DataCoverage`
    (`main.xsl` dispatches to those three).
  - Its symbols are the CHNG* change symbols and `QUESMRK1`.
  - The Lua draft on the repository's `main` branch (`PC/1.0.0`) has the
    same scope plus a `Default` rule that draws `QUESMRK1`.
  - Neither has a rule for any buoy, beacon, light, daymark, landmark or AIS
    feature type.
  - This library bundles the release catalogue unchanged.
- **Design intent.** IHO NIPWG's S-125 portrayal paper (NIPWG9-08.2A, 2022,
  [slide 8](https://iho.int/uploads/user/Services%20and%20Standards/NIPWG/NIPWG9/NIPWG9_2022_08.2A_EN_Presentation_rev1.pdf))
  says:
  - S-125 is "not replacing nor duplicating actual existing AtoN symbology";
  - the status symbol is "flagging and not obscuring" the ENC symbol beneath;
  - interoperability "could be enhanced at a later stage".
- **S-98 Interoperability, Edition 2.0.0.**
  - Main document §6.1.3 lists S-125 among products for future editions.
  - Annex A §15.4 mentions "suppression of S-101 navigation aids by S-125" at
    interoperability level 2. That is only an example of what an
    Interoperability Catalogue could do; no published catalogue does it.

So the specification **neither ships full AtoN symbology nor defers it by
reference to S-101 / S-52**. It assumes an ENC is displayed underneath, and S-125
data alone portrays as status flags only.

### Implementation choice: S-101 AtoN rules

On its own, that portrays a dataset of buoys and lights as an empty chart. This
library therefore **also portrays the aids themselves with the bundled S-101
Portrayal Catalogue's Lua rules**. This is an implementation choice, not part of
the S-125 specification.

- **The rules are reused as-is.** The S-101 rule set covers:
  - buoy shapes and colour patterns (`LateralBuoy`, `CardinalBuoy`, …);
  - topmarks (`TOPMAR02`);
  - daymarks and landmarks;
  - light flares, characteristics and sector legs (`LightAllAround`,
    `LightSectored`, `LightFlareAndDescription`, `LITDSN02`);
  - AIS aids, navigation lines and recommended tracks.

  Both feature catalogues draw these feature types and attributes from the IHO
  GI Registry, so most features map one-to-one. The two catalogues share
  `sectorCharacteristics`, `rhythmOfLight` and the listed values for colour and
  shape.
- **`S125AtonPortrayalProjection` bridges the differences:**
  - S-125 `Topmark` equipment features are lifted onto their structure (via
    the `StructureEquipment` `parent` / `child` roles, or co-location) as S-101's
    `topmark` complex attribute.
  - `SyntheticAISAidToNavigation` is portrayed as `PhysicalAISAidToNavigation`.
  - `maximalPermittedDraught` becomes `maximumPermittedDraught`.
  - `orientation` flattens to `orientationValue` where S-101 binds the simple
    attribute.
  - The pre-1.0 spellings `objectName` / `MMSICode` are read as `featureName` /
    `mMSICode`.
  - Features the S-125 catalogue portrays itself (`AtonStatusIndication`,
    `DataCoverage`) and meta / aggregation features are not projected.
- **`S125AtonLuaDataProvider` serves the projected features to the S-101 rules
  through the S-100 Part 9A host API:**
  - It uses the S-101 feature catalogue for type information.
  - Point features at the same position share one synthetic point record. This
    reproduces S-101 structure / equipment co-location, which the light-flare
    direction and the stacking of light descriptions depend on.
- **`S125DatasetProcessor` merges the S-101 AtoN instructions with the S-125
  catalogue's own output** (status indications drawn over the aid) into one
  layer.
  - The S-101 colour palette is used for both: the S-125 colour profile uses
    the same S-52 tokens, and the CHNG* symbols keep their S-125 stylesheets.
  - Without a Lua engine or the S-101 catalogues, the processor falls back to
    the S-125 catalogue output only.

**Gap: `obscuredSector`.** S-125's `obscuredSector` (on `LightSectored`) has no
S-101 counterpart and is not portrayed.

**ENC display.** Over an ENC that portrays the same physical aids (S-98
interplay):
- S-125 sits on the *other chart overlays* plane, above the ENC's base and
  standard chart planes.
- The S-101 instructions run through the same viewing-group, display-mode and
  display-plane filtering as an ENC. Under the mariner's ECDIS display
  category, S-125 aids show and hide (names and light descriptions included)
  exactly as the ENC's do.
- Where S-125 and the ENC encode the same aid at the same position, the
  symbols and sector legs coincide. The S-125 status flags (CHNG*) then mark
  the ENC aid, which is the overlay the specification intends.
- No S-98 Interoperability Catalogue suppression of either product is applied
  (see the Annex A example above).

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
- Renderers must tolerate geometry-less features — abstract supertypes such as `AtonAggregation` and `AtonAssociation` carry no geometry.
- Time validity (`fixedDateRange`, `periodicDateRange`) is interpreted as UTC; do not coerce to local time at the source.

## Test data

`tests/datasets/S125/aton_us4va1bf.gml` holds the 178 aids to navigation of
NOAA ENC US4VA1BF (Chesapeake Bay entrance; US public domain), derived by
`tests/datasets/S125/derive-s125-from-enc.cs`:
- The ENC cell goes S-57 → S-101 (in-memory translation) → S-125 1.0.0 GML.
- Attributes are limited to the S-125 Feature Catalogue bindings.
- Topmarks are written as `Topmark` features.

It validates clean against `S125AtonRules`. The other `aton_*.gml` fixtures are
small synthetic samples.

## License

The bundled S-125 specification assets in `EncDotNet.S100.Specifications` are © IHO and used in accordance with their open-publication terms; see <https://github.com/iho-ohi/S-125-Product-Specification-Development>.
