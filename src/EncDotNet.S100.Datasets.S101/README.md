# EncDotNet.S100.Datasets.S101

This package reads and writes S-101 Electronic Navigational Chart (ENC)
datasets, which are ISO 8211 files (S-100 Part 10a), and runs the S-100
Part 9A Lua portrayal rules that turn their features into drawing
instructions. It also applies sequential update files and validates a cell
against the S-101 Edition 2.0.0 checklist. Reference it when you need the
cell's records, features, updates or validation directly. To open and render a
dataset without wiring catalogues yourself, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package instead.

The same package also reads and portrays S-401 inland ENC datasets, which
share the Part 10a encoding and the Part 9A portrayal model. See
[S-401 inland ENCs](#s-401-inland-encs).

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S101
```

## Read a dataset

```csharp
using EncDotNet.S100.Datasets.S101;

S101Dataset cell = S101Dataset.Open("path/to/dataset.000");
Console.WriteLine($"{cell.DatasetName}: {cell.FeatureCount} features");

foreach (var feature in new S101VectorSource(cell).GetFeatures().Take(5))
    Console.WriteLine($"{feature.Id} {feature.FeatureType} ({feature.GeometryType})");
```

`S101Dataset` holds the ISO 8211 records as they're encoded. `S101VectorSource`
resolves them into features with WGS 84 geometry and named attributes.
[Reading product data](../../docs/reading-product-data.md) covers this in more
detail.

## Main types

- **`S101Dataset`**: a parsed cell. `Open` reads a file or stream,
  `OpenWithUpdates` applies update files (see
  [Apply sequential updates](#apply-sequential-updates)), and `FromDocument`
  wraps an `S101Document` you already have.
- **`S101Dataset.ReadMetadata`**: reads a cell's `DatasetMetadata` without
  running the portrayal. It's available as a static method for a path or a
  stream, and as an instance method. The metadata holds the declared product
  specification, the geographic extent from a scan of the geometry (`null`
  when the cell has no coordinates), and the `DisplayScale` window from the
  `DataCoverage` `minimumDisplayScale` and `maximumDisplayScale` attributes.
- **`S101Document`** and **`S101DocumentReader`**: the ISO 8211 records. See
  [Record types](#record-types).
- **`S101DocumentWriter`**: encodes an `S101Document` back to ISO 8211. See
  [Write a dataset](#write-a-dataset).
- **`S101VectorSource`**: the `IVectorSource` for the vector pipeline. Surfaces
  resolve both the exterior ring (`Feature.Coordinates`) and any interior rings
  (`Feature.InteriorRings`) from the `RIAS` field (`USAG` = 2). A depth area
  encoded around islands (S-100 Part 10a surface topology) renders with the
  land cut out.
- **`S101PortrayalCatalogue`**: the `IVectorPortrayalCatalogue`. It loads the
  rules, symbols, line styles, area fills and colour palettes.
- **`S101LuaRuleExecutor`**: the `ILuaVectorRuleExecutor` for S-101. It wraps
  the product-neutral `LuaRuleExecutor` from `EncDotNet.S100.Core` and supplies
  the S-101 parts: the `S101LuaDataProvider` host bridge, the bindings from
  mariner settings to context parameters, a feature-anchor provider for
  augmented line geometry, and the `SAFCON` contour label transform.
- **`S101SoundingSampler`**: returns the charted sounding nearest to a WGS 84
  position. S-101 stores soundings as multipoint records (`RCNM` = 115) with
  many depths per feature, so the search compares individual depth points, not
  whole features. The viewer's depth pick report uses it.
- **`S101UpdateApplicator`** and **`S101Document.ApplyChanges`**: sequential
  update support.
- **`S101LegacyFeatureNames`**: maps feature class names from editions before
  2.0.0. See [Feature names from earlier editions](#feature-names-from-earlier-editions).

The `S101DatasetProcessor` that renders, picks and validates an S-101 cell is
in [`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md).

`DrawingInstructionParser`, in `EncDotNet.S100.Core`, parses the
`key:value;…` strings the Lua rules emit into `DrawingInstruction` objects. It
handles text alignment (`TextAlignHorizontal`, `TextAlignVertical`), offsets in
millimetres (`LocalOffset`), foreground and background colour with optional
transparency, line placement, and the `AugmentedPoint:GeographicCRS,…` anchor
that the sounding and `DepthNoBottomFound` rules use. Augmented line geometry
(`AugmentedRay`, `ArcByRadius`, `AugmentedPath`) is tessellated into polylines
and passed to the renderer through `LineInstruction.CoordinatesOverride`. That
covers sector light limits and arcs, directional light rays, and all-round
light circles.

## Apply sequential updates

An S-101 cell is issued as a base dataset (`….000`, application profile `1`)
and ordered update files (`….001`, `….002`, …, application profile `2`).
Updates carry insert, delete and modify instructions at record and element
level (S-100 Part 10a). You apply them in order to get the up-to-date cell.

- **`S101Dataset.OpenWithUpdates(basePath, updatePaths)`** opens a base cell,
  applies its update files, and reports the outcome in
  `S101Dataset.UpdateReport`.
- **`S101UpdateApplicator.Apply(baseDocument, orderedUpdates, out report)`**
  applies an ordered list of update documents to a base document and returns
  an `S101UpdateReport`.
- **`S101Document.ApplyChanges(update)`** applies one update and returns a new
  document. It mirrors `EncDotNet.S57.S57Document.ApplyChanges`.

Application is best-effort. An unreadable file, or an invalid or
non-contiguous update, is recorded in the report, and you can still use the
partly updated cell.

In an exchange set, `ExchangeSetLoader` groups each base cell with the update
files for it in the same set (`S101ExchangeSetUpdatePlan`) and returns one
up-to-date processor per cell. An update whose base cell isn't in the set is
reported as a warning. Updates aren't applied across exchange sets. See
[Loading datasets](../../docs/loading-datasets.md).

## Write a dataset

`S101DocumentWriter` is the inverse of `S101DocumentReader`. It encodes an
`S101Document` as an ISO 8211 S-101 dataset:

```csharp
using EncDotNet.S100.Datasets.S101;

byte[] bytes = S101DocumentWriter.Write(document);
S101DocumentWriter.WriteToFile("path/to/output.000", document);
await S101DocumentWriter.WriteToFileAsync("path/to/output.000", document);
```

The writer emits a Data Descriptive Record (DDR) covering every field it writes,
with field tags, subfield names and binary formats that match the S-101
encoding. Then it writes:

1. A `DSID` record: identification, structure information, and the feature,
   attribute, information and association code catalogues.
2. The spatial records: `PRID`, `MRID`, `CRID`, `CCID` and `SRID`.
3. The feature records: `FRID`, `FOID`, `ATTR`, `SPAS`, `FACS` and `INAS`.
4. The information records: `IRID` and `ATTR`.

A document read from a `.000` file and written back reads as the same document.
Feature-to-feature associations (`FACS`) are written too. The S-57 translator
produces them for bridge and range-system aggregations.

`s100 s57 convert` uses this writer: it translates an S-57 base cell to an
`S101Document` and writes it as a base S-101 cell (application profile `1`).
See [Bringing S-57 into the pipeline](../../docs/s57-to-s101.md).

## Portrayal

The bundled portrayal catalogue is S-101 Edition 2.0.0. Its Lua rules run
through `S101LuaRuleExecutor` and `S101LuaDataProvider`.

### Patches to the bundled Lua rules

The files in `content/S101/pc/` in `EncDotNet.S100.Specifications` are
byte-identical to the IHO S-101 portrayal catalogue. When an upstream rule has
a defect that breaks real cells, `S101LuaDataProvider` patches the affected
global function after loading, through its ordered `PostLoadScripts`, instead
of editing the catalogue. The current patches are:

- **`contains`**: defines a global function that the upstream rules use but
  don't define.
- **`GetFeatureName` and `PortrayFeatureName`**: the upstream rules only pick a
  name when both `name` and `nameUsage` are present, but the S-101 feature
  catalogue declares `nameUsage` as optional (`0..1`). Cells that omit it are
  valid but render without names. The patch treats a missing `nameUsage` as the
  default `1` and keeps the language matching, so area and point feature names
  (built-up areas, named sea areas, churches, and so on) are drawn.

A patch is removed when upstream fixes the defect.

### Portrayal trace messages

The Part 9A rules write `Debug.Trace` messages for expected fallbacks that the
specification allows. For example, the `OBSTRN07` rule reports "Neither
valueOfSounding or defaultClearanceDepth have a value" for an `Obstruction`,
`Wreck` or `UnderwaterAwashRock` with no depth value. `main.lua` then uses the
default symbology and the cell still renders. These messages aren't errors.

`S101LuaDataProvider` sends all Lua and host trace output to the optional
`trace` constructor parameter, an `Action<string>`. If you don't pass one,
messages go to `System.Diagnostics.Trace`, which prints nothing unless a
listener is attached. Pass your own action to capture them, for example behind
a debug option or in tests. `s100 render … --debug` writes the `[Lua]` and
`[Host]` messages to standard error and leaves standard output and the PNG
unchanged.

### Feature names from earlier editions

The S-101 Edition 2.0.0 Lua rules use the 2.0.0 feature class names: for
example, `LateralBuoy.lua` defines `function LateralBuoy`. `main.lua` runs a
feature's rule with `require(feature.Code)` and then `_G[feature.Code](...)`.
Cells made against an earlier S-101 feature catalogue use the older names
(`BuoyLateral`, `BeaconCardinal`, `MooringWarpingFacility`, …). No 2.0.0 rule
matches them, so `require` fails and the feature is drawn with the default
symbol (`QUESMRK1`).

`S101LegacyFeatureNames.Normalize` maps the older class names to their 2.0.0
equivalents so the right rule runs. It covers:

- Buoy and beacon classes whose words were reordered: `BuoyLateral` →
  `LateralBuoy`, `BeaconCardinal` → `CardinalBeacon`, and so on.
- Classes merged in 2.0.0: `RestrictedAreaNavigational` and
  `RestrictedAreaRegulatory` → `RestrictedArea`; `TrafficSeparationZone` and
  `TrafficSeparationLine` → `SeparationZoneOrLine`; `BuoyEmergencyWreckMarking`
  and `BuoyNewDangerMarking` → `EmergencyWreckMarkingBuoy`.

Simple attribute names didn't change between these editions, so only the
feature class name is mapped. The mapping applies only where the portrayal
asks for the feature code (`S101LuaDataProvider.HostFeatureGetCode`). The
reader, vector source, validation and feature info keep the names as encoded.

`MooringWarpingFacility` was removed in 2.0.0, so its mapping depends on
`categoryOfMooringWarpingFacility`: dolphin → `Dolphin`, bollard → `Bollard`,
post or pile → `Pile`, mooring buoy → `MooringBuoy`. Other categories, and a
missing or empty category, map to the `Default` rule, so `require` always
finds a module and the feature gets the default symbology instead of failing
with `module 'MooringWarpingFacility' not found`. These mappings are
approximate. Only the class name changes, not the attributes the 2.0.0 rule
reads, so the symbol may be generic. If the target rule rejects the feature's
geometry type, the error is caught by the `pcall` in `main.lua` and the
feature falls back to the default symbology.

## S-401 inland ENCs

S-401 is the Inland ENC Harmonization Group (IEHG) inland ENC product. It
uses the same encoding and portrayal model as S-101, so this package reads it
and `S101DatasetProcessor` portrays it with the bundled S-401 catalogues. The
processor takes the catalogue's product specification as a parameter.
`S101VectorSource` reports the product that the dataset's `DSID` record
declares. Everything in this package except the validation rule pack applies
to S-401 too. For the bundled S-401 catalogues, see
[S-401 bundled catalogues](../EncDotNet.S100.Specifications/content/S401/README.md).

## Record types

`S101DocumentReader` reads these ISO 8211 record types:

| Tag | Record type | Contents |
|-----|-------------|----------|
| DSID | Dataset identification | Version, edition, product specification |
| DSSI | Dataset structure information | `COMF` and `SOMF` scaling factors |
| PRID | Point | One 2D coordinate |
| MRID | Multipoint | 3D sounding arrays in the `C3IL` field (`VCID` leader and a repeating `YCOO`/`XCOO`/`ZCOO` group) |
| CRID | Curve | An ordered coordinate sequence |
| CCID | Composite curve | References to curves |
| SRID | Surface | Ring-based polygon geometry |
| FRID | Feature | Feature type, attributes, spatial associations |
| IRID | Information type | Records that features refer to |

Every record identifier field also carries `RVER` (record version) and `RUIN`
(record update instruction). The association and attribute fields carry their
per-element update instructions (`SAUI`, `FAUI`, `IUIN`, `ATIN`). The reader
keeps all of these so updates can be applied.

## Validation

The bundled rule pack, `EncDotNet.S100.Datasets.S101.Validation.S101DatasetRules.Default`,
checks a cell against the S-101 Edition 2.0.0 checklist and returns a
`ValidationReport`. `S101DatasetProcessor.Validate()` runs it for you. To run it
directly:

```csharp
using EncDotNet.S100.Datasets.S101.Validation;

var view = S101DatasetView.From(document, decoder);
var report = S101DatasetRules.Default.Run(view);
```

`decoder` is a `FeatureCatalogueDecoder` over the S-101 feature catalogue. The
rules read the cell through the view types in `Validation/` (`S101DatasetView`,
`S101FeatureView`, `S101AttributeView`), which use the feature catalogue's
names instead of the raw `S101FeatureRecord` shape. That keeps the rules
independent of the record layout.

| Rule ID | Severity | Checks |
|---------|----------|--------|
| `S101-R-1.1` | Error | The feature type code resolves to a feature catalogue acronym. |
| `S101-R-1.2` | Error | The attribute code resolves and is bound where it sits: a top-level row (`PAIX` 0) to the feature class (following the catalogue's `SuperType` chain), and a sub-attribute row to the complex attribute its `PAIX` points at. |
| `S101-R-2.1` | Error | Feature identifiers (`FOID`) are unique. Each duplicate is one finding, anchored on the first occurrence. |
| `S101-R-3.1` | Error | Spatial associations resolve to the right record type (point, curve, surface, composite curve). |
| `S101-R-3.2` | Error | Surface rings are closed and have at least three distinct points. Each ring is walked in its encoded orientation (ring association, composite curve component and curve). |
| `S101-R-3.3` | Error | Composite curves are continuous: the end of segment N equals the start of segment N+1. |
| `S101-R-4.1` | Warning | Enumerated attribute values are in the domain the feature catalogue declares. |
| `S101-R-5.1` | Warning | Resolved latitude and longitude are within WGS 84 ranges. |
| `S101-R-5.2` | Warning | Information associations resolve to a known information record. |
| `S101-PROJ-PARSE` | — | Reserved for future parser findings. It has no checks yet. |

A finding about a feature carries its location: the position of a single-point
feature, or else the WGS 84 envelope of its geometry. Geometry findings also
carry the ring's envelope and the vertex at the gap (`S101-R-3.2`) or the break
(`S101-R-3.3`), so a map overlay can mark them.

The S-57 pipeline runs the same pack on translated S-57 cells and prefixes the
rule IDs with `S101-as-S57/`, so you can tell which stage a finding came from.
See [`EncDotNet.S100.Datasets.S57`](../EncDotNet.S100.Datasets.S57/README.md#validation).

The pack doesn't run on S-401 datasets: `Validate()` returns `null` for them,
which means no rule pack is available.

For your own rules and the validation API, see
[Custom catalogues and validation](../../docs/catalogues-and-validation.md).

## See also

- [Reading product data](../../docs/reading-product-data.md): S-101 features
  alongside the other products.
- [Loading datasets](../../docs/loading-datasets.md): exchange sets and updates
  through the `EncDotNet.S100` facade.
- [Bringing S-57 into the pipeline](../../docs/s57-to-s101.md): read S-57 cells
  through this package's portrayal.
