# EncDotNet.S100.Datasets.S101

Reader and Lua portrayal pipeline for S-101 Electronic Navigational Chart (ENC) datasets.

## Overview

This library reads S-101 datasets encoded in ISO 8211 format and executes the S-100 Part 9A Lua portrayal pipeline to produce drawing instructions. Key types include:

- **`S101Dataset`** — parsed ENC dataset containing features from ISO 8211 records.
- **`S101Document`**, **`S101DocumentReader`** — low-level ISO 8211 record parsing.
- **`S101LuaRuleExecutor`** — `ILuaRuleExecutor` implementation that orchestrates the Lua portrayal pipeline (loads `main.lua`, calls `PortrayalMain()`, parses and post-processes the emitted instructions).
- **`S101PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT/Lua rules, symbols, line styles, area fills, and color palettes.
- **`S101VectorSource`** — `IVectorSource` implementation for the vector pipeline.
- **`DrawingInstructionParser`** — parses the semicolon-separated key:value strings emitted by the Lua portrayal pipeline into the unified `DrawingInstruction` hierarchy from `EncDotNet.S100.Core`. Honours text alignment (`TextAlignHorizontal` / `TextAlignVertical`), mm offsets (`LocalOffset`), foreground / background colour with optional transparency, line placement, and the `AugmentedPoint:GeographicCRS,…` anchor override used by SOUNDG / DepthNoBottomFound rules. Augmented line geometry (`AugmentedRay`, `ArcByRadius`, `AugmentedPath`) is fully supported — sector-light limit lines and arcs, directional-light rays, and all-around-light circles are tessellated into polylines and carried through `LineInstruction.CoordinatesOverride` to the renderer.

## Bundled-adapter Lua patches

`content/S101/pc/` stays **byte-identical** to the upstream IHO S-101
portrayal catalogue. When upstream Lua has a defect that breaks
real-world cells, `S101LuaRuleExecutor` ships a small adapter patch
that monkey-patches the offending global rather than editing the
bundled catalogue. Current patches:

- **`contains`** — restores a missing global the upstream catalogue
  relies on without defining.
- **`GetFeatureName` / `PortrayFeatureName`** — upstream gates name
  selection on both `name` AND `nameUsage`, but the S-101 FC declares
  `nameUsage` with multiplicity `0..1`. Cells that omit it are
  FC-conformant but rendered nameless. The patch treats a missing
  `nameUsage` as the default `1` while preserving the original
  language-matching semantics, so area / point feature names
  (BuiltUpArea, SeaAreaNamedWaterArea, churches, …) emit correctly.

If upstream fixes a defect, the corresponding patch is dropped.

## Legacy feature-name compatibility

The bundled Portrayal Catalogue is **S-101 Edition 2.0.0**, whose Lua
rule modules use the 2.0.0 (word-reversed) feature class names —
`LateralBuoy.lua` defining `function LateralBuoy`, dispatched by
`main.lua` via `require(feature.Code)` then `_G[feature.Code](...)`.
Datasets authored against an earlier edition of the S-101 Feature
Catalogue report the **pre-2.0.0** names (`BuoyLateral`,
`BeaconCardinal`, `MooringWarpingFacility`, …). Those names match no
2.0.0 rule module, so the dispatcher's `require` fails and the feature
falls back to **DEFAULT** (`QUESMRK1`) symbology.

`S101LegacyFeatureNames.Normalize` maps the legacy class names to their
2.0.0 equivalents so the correct rule runs. Because simple attribute
names are stable across these editions, only the feature **class** name
needs remapping. The shim is applied **only** at the portrayal boundary
(`S101LuaDataProvider.HostFeatureGetCode`); feature names are left
as-authored everywhere else (document reader, vector source,
validation, info panels).

`MooringWarpingFacility` was structurally removed in 2.0.0, so it is
mapped conditionally on `categoryOfMooringWarpingFacility`
(dolphin → `Dolphin`, bollard → `Bollard`, post/pile → `Pile`,
mooring buoy → `MooringBuoy`); categories without a clean 2.0.0
equivalent are left unchanged and stay on DEFAULT. These conditional
targets are approximations — only the class name is aliased, not the
attributes the 2.0.0 rule reads — so symbology may be generic, and a
target rule that rejects the feature's geometric primitive simply
errors inside the dispatcher's `pcall` and falls back to DEFAULT
(no regression versus today).

## Validation

A bundled rule pack
(`EncDotNet.S100.Datasets.S101.Validation.S101DatasetRules.Default`)
evaluates a typed view over an `S101Document` against the S-101
Edition 2.0.0 checklist and emits a `ValidationReport` of findings.
The view types under `Validation/` (`S101DatasetView`,
`S101FeatureView`, `S101AttributeView`) are the **spec-aligned façade**
the pack reads from — they keep rule code decoupled from the raw
`S101FeatureRecord` shape so a future typed `DataModel` projection
can replace them without rewriting the rules.

The pack is invoked automatically by `S101DatasetProcessor.Validate()`
and can also be run directly:

```csharp
var view = S101DatasetView.From(document, decoder);
var report = S101DatasetRules.Default.Run(view);
```

| Rule id            | Severity | Checks                                                                                                              |
|--------------------|----------|---------------------------------------------------------------------------------------------------------------------|
| `S101-R-1.1`       | Error    | Feature type code resolves to an FC acronym.                                                                        |
| `S101-R-1.2`       | Error    | Attribute code resolves AND is bound to the host feature class (walks the FC `SuperType` chain).                    |
| `S101-R-2.1`       | Error    | FOID uniqueness — one finding per duplicate, with the first occurrence as anchor.                                   |
| `S101-R-3.1`       | Error    | Spatial associations resolve into the correct record dictionary (point, curve, surface, composite curve).           |
| `S101-R-3.2`       | Error    | Surface ring closure plus rejection of rings with fewer than three distinct points.                                 |
| `S101-R-3.3`       | Error    | Composite curve continuity (end of segment N equals start of segment N+1).                                          |
| `S101-R-4.1`       | Warning  | Enumerated attribute values fall in the FC-declared domain.                                                         |
| `S101-R-5.1`       | Warning  | Resolved (lat, lon) coordinates lie in WGS-84 ranges.                                                               |
| `S101-R-5.2`       | Warning  | Information associations resolve to a known information record.                                                     |
| `S101-PROJ-PARSE`  | —        | Placeholder reserving the namespace for future parser-diagnostic findings; body intentionally empty.                |

The same `S101DatasetRules.Default` entry point is reused by S-57
post-translation delegation (see
[`EncDotNet.S100.Datasets.S57`](../EncDotNet.S100.Datasets.S57/README.md)),
with findings rebadged as `S101-as-S57/<rule-id>` so the user can
tell which layer of the pipeline a problem came from.

## Record types

`S101DocumentReader` parses the following ISO 8211 record types:

| Tag | Record type | Notes |
|-----|-------------|-------|
| DSID | Dataset identification | Version, edition, product spec |
| DSSI | Dataset structure info | COMF / SOMF scaling factors |
| PRID | Point | Single 2D coordinate |
| MRID | MultiPoint | 3D sounding arrays via C3IL field (VCID leader + YCOO/XCOO/ZCOO repeating group) |
| CRID | Curve segment | Ordered coordinate sequences |
| CCID | Composite curve | References to curve segments |
| SRID | Surface | Ring-based polygon geometry |
| FRID | Feature | Object class, attributes, spatial associations |
| IRID | Information type | Metadata records referenced by features |

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S101
```
