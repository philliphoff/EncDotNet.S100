# EncDotNet.S100.Datasets.S101

Reader and Lua portrayal pipeline for S-101 Electronic Navigational Chart (ENC) datasets.

## Overview

This library reads S-101 datasets encoded in ISO 8211 format and executes the S-100 Part 9A Lua portrayal pipeline to produce drawing instructions. Key types include:

- **`S101Dataset`** — parsed ENC dataset containing features from ISO 8211 records.
- **`S101Document`**, **`S101DocumentReader`** — low-level ISO 8211 record parsing.
- **`S101DocumentWriter`** — the symmetric inverse of `S101DocumentReader`: encodes an `S101Document` back to an ISO/IEC 8211 S-101 dataset (`byte[]` / stream / file). Used by the S-57 → S-101 conversion path (`s100 s57 convert`). It emits a Data Descriptive Record (DDR) plus one data record per dataset / spatial / feature / information object; a document read from a `.000` and written back round-trips through the reader. See below.
- **`S101LuaRuleExecutor`** — `ILuaVectorRuleExecutor` implementation that wraps the product-agnostic `LuaRuleExecutor` from `EncDotNet.S100.Core`, supplying the S-101 seams: the `S101LuaDataProvider` host bridge, the mariner→context-parameter bindings, a feature-anchor provider for augmented line tessellation, and the SAFCON contour-label transform.
- **`S101PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT/Lua rules, symbols, line styles, area fills, and color palettes.
- **`S101VectorSource`** — `IVectorSource` implementation for the vector pipeline. Surface geometry resolves both the exterior ring (`Feature.Coordinates`) and any interior rings / holes (`Feature.InteriorRings`) from the RIAS field (USAG = 2), so a sea/depth area encoded around islands (S-100 Part 10a surface topology) renders with the land cut out instead of painting solidly over every `LandArea`.
- **`S101UpdateApplicator`**, **`S101Document.ApplyChanges`** — sequential update support (see below).
- **`DrawingInstructionParser`** (in `EncDotNet.S100.Core`) — parses the semicolon-separated key:value strings emitted by the Lua portrayal pipeline into the unified `DrawingInstruction` hierarchy. Honours text alignment (`TextAlignHorizontal` / `TextAlignVertical`), mm offsets (`LocalOffset`), foreground / background colour with optional transparency, line placement, and the `AugmentedPoint:GeographicCRS,…` anchor override used by SOUNDG / DepthNoBottomFound rules. Augmented line geometry (`AugmentedRay`, `ArcByRadius`, `AugmentedPath`) is fully supported — sector-light limit lines and arcs, directional-light rays, and all-around-light circles are tessellated into polylines and carried through `LineInstruction.CoordinatesOverride` to the renderer.

## Bundled-adapter Lua patches

`content/S101/pc/` stays **byte-identical** to the upstream IHO S-101
portrayal catalogue. When upstream Lua has a defect that breaks
real-world cells, the `S101LuaDataProvider` ships a small adapter patch
(via its ordered `PostLoadScripts`) that monkey-patches the offending
global rather than editing the bundled catalogue. Current patches:

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

## Portrayal diagnostics and trace output

The S-100 Part 9A rules emit `Debug.Trace` diagnostics for **expected,
spec-compliant fallbacks** — most visibly the `OBSTRN07` rule raising
*"Neither valueOfSounding or defaultClearanceDepth have a value"* for an
`Obstruction` / `Wreck` / `UnderwaterAwashRock` feature that legitimately
carries no depth value, after which `main.lua` substitutes Default
symbology and the cell still renders. These are **not** errors.

`S101LuaDataProvider` therefore routes all Lua/host diagnostic trace
output through an injectable sink (the optional `trace` constructor
parameter). When omitted, messages go to `System.Diagnostics.Trace`,
which is silent on standard output unless a listener is attached — so
these high-volume, benign fallbacks no longer pollute render/validation
output. Pass an explicit `Action<string>` to capture or surface them
(for example behind a verbose/debug flag or in tests). The bundled CLI
wires this to `--debug`: `s100 render … --debug` mirrors the
`[Lua]`/`[Host]` diagnostics to **stderr** while keeping stdout (and the
PNG result) unchanged.

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
2.0.0 equivalents so the correct rule runs. This covers both the
word-reordered buoy/beacon classes (`BuoyLateral` → `LateralBuoy`,
`BeaconCardinal` → `CardinalBeacon`, …) and classes that were **merged**
in 2.0.0: `RestrictedAreaNavigational` / `RestrictedAreaRegulatory` →
`RestrictedArea`, `TrafficSeparationZone` / `TrafficSeparationLine` →
`SeparationZoneOrLine`, and `BuoyEmergencyWreckMarking` /
`BuoyNewDangerMarking` → `EmergencyWreckMarkingBuoy`. Because simple
attribute names are stable across these editions, only the feature
**class** name needs remapping. The shim is applied **only** at the
portrayal boundary (`S101LuaDataProvider.HostFeatureGetCode`); feature
names are left as-authored everywhere else (document reader, vector
source, validation, info panels).

`MooringWarpingFacility` was structurally removed in 2.0.0, so it is
mapped conditionally on `categoryOfMooringWarpingFacility`
(dolphin → `Dolphin`, bollard → `Bollard`, post/pile → `Pile`,
mooring buoy → `MooringBuoy`); categories without a clean 2.0.0
equivalent — and instances with an absent or empty category — are
routed to the `Default` rule module so the dispatcher's `require`
always resolves (DEFAULT symbology) instead of throwing
`module 'MooringWarpingFacility' not found`. These conditional
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

Every record-id field also carries `RVER` (record version) and `RUIN`
(record update instruction), and the feature/information association and
attribute fields carry their inline per-element update instructions
(`SAUI` / `FAUI` / `IUIN` / `ATIN`). These are read into the model to
drive sequential update application.

## Writing datasets

`S101DocumentWriter` is the inverse of `S101DocumentReader`: it serializes an
`S101Document` to an ISO/IEC 8211 S-101 dataset.

```csharp
byte[] bytes = S101DocumentWriter.Write(document);
S101DocumentWriter.WriteToFile("cell.000", document);
await S101DocumentWriter.WriteToFileAsync("cell.000", document);
```

The writer authors a Data Descriptive Record (DDR) covering every field it
emits (field tags, subfield names, and binary formats matching the canonical
S-101 encoding), followed by a DSID record (identification, structure info, and
the feature/attribute/information/association code catalogues), the spatial
records (`PRID`, `MRID`, `CRID`, `CCID`, `SRID`), the feature records (`FRID`,
`FOID`, `ATTR`, `SPAS`, `INAS`), and the information records (`IRID`, `ATTR`).
A document read from a real `.000` and written back is equivalent when read
again. Feature-to-feature associations (`FASC`) are not emitted — the S-57
translator produces none and the reader does not surface them.

This is the encoder behind the `s100 s57 convert` CLI command, which translates
an S-57 base cell to `S101Document` and writes it as a base S-101 cell
(application profile `1`).


## Sequential updates

Like S-57, an S-101 cell is distributed as a base dataset (`….000`,
application profile `1`) plus ordered update files (`….001`, `….002`, …,
application profile `2`). Updates carry record- and element-level
insert / delete / modify instructions (S-100 Part 10a) that must be
applied in sequence to obtain the up-to-date cell.

- **`S101Document.ApplyChanges(update)`** merges one update document into
  a new document (pure; the design mirrors `EncDotNet.S57.S57Document.ApplyChanges`).
- **`S101UpdateApplicator.Apply(base, orderedUpdates, out report)`** folds
  an ordered update list onto a base cell and returns an `S101UpdateReport`.
  Application is **best-effort**: an unreadable file, or an invalid /
  non-contiguous update, is recorded in the report and never prevents the
  (partially) updated dataset from being used.
- **`S101Dataset.OpenWithUpdates(basePath, updatePaths)`** opens a base
  cell and applies its update files, exposing the outcome via
  `S101Dataset.UpdateReport`.

Within an exchange set, `ExchangeSetLoader` groups each S-101 base cell
with the update files that target it **in the same set**
(`S101ExchangeSetUpdatePlan`) and emits a single up-to-date processor per
cell; updates with no in-set base surface as a best-effort warning.
Cross-exchange-set application is not yet supported.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S101
```
