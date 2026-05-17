# EncDotNet.S100.Datasets.S131

Reader and Lua portrayal pipeline for **IHO S-131 Marine Harbour
Infrastructure** datasets (GML encoding, S-100 Part 10b).

## Specification editions

| Asset | Edition | Notes |
|---|---|---|
| Feature Catalogue | 1.0.0 | 31 feature types, 14 information types |
| Portrayal Catalogue | 2.0.0 | 42 Lua scripts, 6 SVG symbols |
| GML Application Schema | 1.0.0 | Namespace `http://www.iho.int/S131/1.0` |

## Screenshot

S-131 Halifax Harbour sample rendered in the S-100 Viewer:

![S-131 Halifax Harbour](../../readme/s131-viewer.png)

## Architecture — GML + Lua hybrid

S-131 is **unique** in this codebase: it combines **GML data encoding**
(like S-122, S-124, S-125, S-127, S-128, S-411, S-421) with **Lua
portrayal** (like S-101). All other GML products use XSLT portrayal;
all other Lua products use ISO 8211 or HDF5 encoding. S-131 is the
first GML+Lua hybrid.

The bridge architecture:

```
S-131 GML → S131DatasetReader → S131Dataset
                                    ↓
                             S131LuaDataProvider (GML→Lua Host API)
                                    ↓
                             S131LuaRuleExecutor (MoonSharp Lua 5.2)
                                    ↓
                             DrawingInstructionParser (reused from S-101)
                                    ↓
                             VectorPipeline → MapsuiDisplayListRenderer
```

### Key design decisions

- **Custom `S131DatasetProcessor`**: Does not extend
  `GmlDatasetProcessorBase` (which hardwires XSLT). Instead follows
  the `S101DatasetProcessor` pattern with Lua infrastructure.
- **Numeric ID mapping**: GML string IDs (`gml:id`) are mapped to
  sequential numeric IDs for the Lua Host API contract.
- **Synthetic spatial records**: GML embeds geometry inline; the data
  provider synthesizes spatial association structures compatible with
  S-101's `HostGetSpatialData` contract.
- **FC-driven discrimination**: Feature vs. information type
  classification uses a set of known information type codes from the
  Feature Catalogue, handling S-131's unified `<S131:members>` container
  (no `<member>`/`<imember>` split).

## GML shape

```xml
<S131:Dataset xmlns:S131="http://www.iho.int/S131/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0">
  <S131:members>
    <!-- ALL features AND information types in one container -->
    <S131:Berth gml:id="f1">...</S131:Berth>
    <S131:ContactDetails gml:id="info1">...</S131:ContactDetails>
  </S131:members>
</S131:Dataset>
```

## Public API

| Type | Description |
|---|---|
| `S131Dataset` | Parsed dataset model (features + information types) |
| `S131Feature` | Feature with geometry, attributes, complex attributes, xlink refs |
| `S131InformationType` | Information type with attributes and xlink refs |
| `S131DatasetReader` | GML parser; namespace-driven feature recognition |
| `S131LuaDataProvider` | GML-to-Lua Host API bridge |
| `S131LuaRuleExecutor` | Lua portrayal executor (Part 9A) |
| `S131PortrayalCatalogue` | Portrayal catalogue (symbols, palettes, rules) |
| `DataModel.S131HarbourInfrastructureDataset` | **Typed projection** of `S131Dataset` (see below) |

## Typed DataModel

The `EncDotNet.S100.Datasets.S131.DataModel` namespace provides a
"Pass 2" strongly-typed projection layered on top of the raw
`S131Dataset` graph, following the same pattern used by the other
GML-encoded products (S-122 / S-124 / S-125 / S-127 / S-128 / S-129 /
S-201 / S-411 / S-421). The projection is **independent of
portrayal** — the Lua pipeline continues to consume the raw feature
graph unchanged.

### Entry point

```csharp
var raw = S131Dataset.Open("dataset.gml");
var typed = S131HarbourInfrastructureDataset.From(raw, out var diagnostics);

foreach (var berth in typed.LayoutFeatures.Where(l => l.Kind == S131LayoutKind.Berth))
    Console.WriteLine($"{berth.Id}: {berth.Geometry.Points.FirstOrDefault()}");

foreach (var authority in typed.Authorities)
    Console.WriteLine($"{authority.Id} → contact={authority.ContactDetails?.Id}");
```

### Family hierarchy

Concrete feature types are grouped into four families derived
statically from the FC supertype graph (FC Ed 1.0.0 §B.2 / §B.5).
The projection does **not** walk the FC at runtime — schema
introspection remains the job of the Feature Catalogue reader.

| Family | Typed record | Enum | FC supertype |
|---|---|---|---|
| `HarbourInfrastructure` | `S131HarbourInfrastructure` | `S131HarbourInfrastructureKind` | `HarbourPhysicalInfrastructure` (Bollard, Dolphin, DryDock, FloatingDock, Gridiron, HarbourFacility, LockBasin, LockBasinPart, MooringBuoy, OnshorePowerFacility, ShipLift, StraddleCarrier, AutomatedGuidedVehicle) |
| `Layout` | `S131LayoutFeature` | `S131LayoutKind` | `Layout` (AnchorBerth, AnchorageArea, Berth, BerthPosition, DockArea, DumpingGround, FenderLine, HarbourAreaAdministrative, HarbourAreaSection, HarbourBasin, MooringWarpingFacility, OuterLimit, PilotBoardingPlace, SeaplaneLandingArea, Terminal, TurningBasin, WaterwayArea) |
| `Metadata` | `S131MetadataFeature` | `S131MetadataKind` | Standalone (DataCoverage, QualityOfNonBathymetricData, SoundingDatum, TextPlacement, VerticalDatumOfData) |
| `Unknown` | `S131OtherFeature` | — | Forward-compat catch-all |

### Information-type hierarchy

| Typed record | Source code(s) | Notes |
|---|---|---|
| `S131Authority` | `Authority` | Container; resolves `contactDetails` / `applicability` xlinks to typed peers via shortcut properties |
| `S131ContactDetails` | `ContactDetails` | |
| `S131Applicability` | `Applicability` | |
| `S131AvailablePortServices` | `AvailablePortServices` | |
| `S131Entrance` | `Entrance` | |
| `S131ServiceHours` | `ServiceHours` | |
| `S131NonStandardWorkingDay` | `NonStandardWorkingDay` | |
| `S131SpatialQuality` | `SpatialQuality` | |
| `S131RxNInformation` | `NauticalInformation` / `Recommendations` / `Regulations` / `Restrictions` | Discriminated by `S131RxNKind`; mirrors the FC `AbstractRxN` subtree |
| `S131OtherInformationType` | — | Forward-compat catch-all |

Information types in S-131 never carry geometry; the
`S131Authority` shortcuts (`ContactDetails`, `Applicability`) are
typed for the common Authority-binding pattern.

### Geometry

`S131Geometry` wraps the four parallel coordinate collections on
`S131Feature` into a single record with `GeometryType` (`None`,
`Point`, `Curve`, `Surface`). Coordinates are
`GeoPosition(Latitude, Longitude)` in decimal degrees per S-100 Part
10b §6.2.

### Cross-references

`xlink:href` references on role-bearing child elements (e.g.
`<S131:applicability xlink:href="#info1"/>`) are resolved into
`S131ResolvedReference { Role, TargetRef, Target }` entries on the
`ResolvedReferences` property of both features and information
types. Unresolved references carry a `null` `Target` plus a paired
`xlink.unresolved` / `s131.reference.dangling` diagnostic.

### Diagnostic codes

The projection reports issues via `ProjectionDiagnostic` rather
than throwing (the only fatal condition is an empty source dataset):

| Code | Severity | Meaning |
|---|---|---|
| `xlink.unresolved` | Warning | `xlink:href` target not present in the dataset (from `XlinkResolver`) |
| `s131.reference.dangling` | Warning | An `S131Reference` ended up with `Target == null` — raised at the role level for easier filtering |
| `s131.feature.unknown` | Info | Feature type code not in the FC enumeration — falls back to `S131OtherFeature` |
| `s131.information.unknown` | Info | Information type code not in the FC enumeration — falls back to `S131OtherInformationType` |
| `s131.id.duplicate` | Warning | Two source objects share a `gml:id` |
| `attribute.parse.{int,double,bool,datetime}` | Warning | From `AttributeParser` (currently unused — present for forward compatibility) |

### Feature Catalogue note

The typed enum lists are derived from the standalone FC at
`content/S131/fc/FeatureCatalogue.xml` (FC Ed 1.0.0). The PC-bundled
FC at `content/S131/pc/.../131_FC_2.0.0.20251025.xml` may contain
additional codes — any divergence surfaces as
`s131.feature.unknown` / `s131.information.unknown` diagnostics, and
the affected objects still project cleanly as
`S131OtherFeature` / `S131OtherInformationType`. The projection has
no runtime FC dependency.

## Usage

```csharp
// Parse a dataset
var dataset = S131Dataset.Open("path/to/dataset.gml");

// Or from a stream
using var stream = File.OpenRead("dataset.gml");
var dataset = S131Dataset.Open(stream);

// Access features
foreach (var feature in dataset.Features)
{
    Console.WriteLine($"{feature.FeatureType}: {feature.Id}");
}
```

## Dependencies

- `EncDotNet.S100.Core` — GML abstractions, pipeline interfaces
- `EncDotNet.S100.Datasets.S101` — `DrawingInstructionParser` reuse
- `EncDotNet.S100.Features` — Feature Catalogue decoder
- `EncDotNet.S100.Portrayals` — Portrayal Catalogue types
