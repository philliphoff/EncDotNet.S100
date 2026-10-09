# EncDotNet.S100.Datasets.S102

This package reads S-102 Bathymetric Surface datasets from HDF5 files and
portrays their depth grids with the S-102 portrayal catalogue. It also
validates a dataset against the S-102 Edition 3.0.0 checklist. Reference it
when you need the depth and uncertainty grids directly. To open and render a
dataset without wiring catalogues yourself, use the
[`EncDotNet.S100`](../EncDotNet.S100/README.md) package instead.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S102
dotnet add package EncDotNet.S100.Hdf5.PureHdf
```

`EncDotNet.S100.Hdf5.PureHdf` provides the HDF5 reader the example uses.

## Read a dataset

```csharp
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Hdf5.PureHdf;

using var file = PureHdfFile.Open("path/to/dataset.h5");
S102Dataset bathymetry = S102DatasetReader.Read(file);

BathymetryCoverage grid = bathymetry.Coverages[0];
Console.WriteLine($"EPSG:{bathymetry.HorizontalCRS}, " +
                  $"{grid.NumPointsLatitudinal} x {grid.NumPointsLongitudinal} cells");
```

`grid.Values` is row-major from the grid origin. Depths are in metres, and
cells without data hold the fill value 1,000,000. Origin and spacing are in the
dataset's horizontal CRS, which may be projected. For a full walk through the
grid, see [Reading product data](../../docs/reading-product-data.md).

## Main types

- **`S102Dataset`**: the dataset, with its horizontal CRS, vertical datum and
  bathymetry coverages.
- **`S102DatasetReader`**: reads an `S102Dataset` from an `IHdf5File`.
  - The horizontal CRS comes from the Edition 3.0.0 `horizontalCRS` root
    attribute. If that's missing, the reader uses the Edition 2.1
    `horizontalDatumValue` attribute, so both editions are georeferenced
    correctly.
  - The `verticalDatum` root attribute (S-102 Ed 3.0.0 §12.3) becomes
    `S102Dataset.VerticalDatum`, an S-100 register code. `S102CoverageSource`
    resolves it to a label through `EncDotNet.S100.DataModel.VerticalDatums`.
  - `ReadMetadata(file)` returns a `DatasetMetadata` with the declared product
    specification, the grid extent and `HorizontalCrsEpsg` (for UTM
    reprojection). It reads only the georeferencing attributes, not the
    `depth` and `uncertainty` values.
- **`BathymetryCoverage`** and **`BathymetryValue`**: a grid and its values.
  `BathymetryCoverage.GroupPath` is the HDF5 path of the coverage, such as
  `/BathymetryCoverage/BathymetryCoverage.01`. Validation findings use it as
  `RelatedFeatureId`.
- **`S102CoverageSource`**: the `ICoverageSource` for the coverage pipeline.
  See [Downsampling](#downsampling).
- **`S102PortrayalCatalogue`**: the coverage portrayal catalogue that shades
  depths. See [Portrayal](#portrayal).

The `S102DatasetProcessor` that renders, samples and validates a dataset is in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md).

## Portrayal

`S102PortrayalCatalogue` runs the bundled `BathymetryCoverage.lua` rule (S-102
Edition 3.0.0, Annex B) through an `ILuaEngine`. It resolves the colour tokens
the rule emits (`DEPDW`, `DEPMD`, `DEPMS`, `DEPVS`, `DEPIT`, `NODTA`) against
the bundled `ColorProfiles/colorProfile.xml`. The Day, Dusk and Night palettes
are all loaded, and `SwitchPaletteAsync` selects one. Call `SwitchPaletteAsync`
before `ResolveColorScheme`: it loads the Lua rule, and `ResolveColorScheme`
throws `InvalidOperationException` until it has.

The four S-102 context parameters come from `MarinerSettings`:

- `FourShades` switches between two depth bands (`DEPVS` and `DEPDW`, split
  at `SafetyContour`) and four (`DEPVS`, `DEPMS`, `DEPMD` and `DEPDW`, split at
  `ShallowContour`, `SafetyContour` and `DeepContour`).
- `SafetyContour`, `ShallowContour` and `DeepContour` are depth boundaries in
  metres.

If the contours are out of order, the catalogue clamps them to
`ShallowContour ≤ SafetyContour ≤ DeepContour` and logs a diagnostic instead
of throwing. Settings sliders can pass through out-of-order values while you
drag them, and that mustn't stop rendering.

Cells whose depth equals `S102CoverageSource.FillValue` (1,000,000) are painted
with the active palette's `NODTA` colour through
`CoverageColorScheme.NoDataColor`. Set
`S102PortrayalCatalogue.RenderNoDataFill = false` to leave them transparent.
Do this when S-102 is drawn over another layer, such as an S-101 ENC, so the
unsurveyed part of the rectangular grid doesn't hide the chart underneath.

## Downsampling

When `S102CoverageSource` draws a grid at a smaller scale, it reduces blocks of
cells to one value. Both its overview pyramid and its viewport sampling keep
the shallowest depth (minimum) and the largest uncertainty (maximum) in each
block. Every source cell belongs to a block, so a narrow shoal can't fall
between sample points. The blocks are balanced across the sampled area so the
reduced grid's georeferencing stays exact, and the pyramid stops before a
level would need a partial 2×2 block at an edge. Coverage read trace spans
report `s100.coverage.reducer=min`.

## Validation

The bundled rule pack, `EncDotNet.S100.Datasets.S102.Validation.S102DatasetRules.Default`,
checks an `S102Dataset` against the S-102 Edition 3.0.0 checklist and returns a
`ValidationReport`. `S102DatasetProcessor.Validate()` runs it for you. To run it
directly:

```csharp
using EncDotNet.S100.Datasets.S102.Validation;

var report = S102DatasetRules.Default.Run(dataset);
foreach (var finding in report.Findings)
    Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---------|----------|--------|
| `S102-R-1.1` | Error | Each coverage's `Values.Length` equals `NumPointsLatitudinal × NumPointsLongitudinal`. |
| `S102-R-2.1` | Error | The fill value in `Depth` and `Uncertainty` is exactly `1_000_000f`. `NaN` and ±`Infinity` aren't allowed as substitutes. |
| `S102-R-3.1` | Warning | `HorizontalCRS`, when set, is a recognised EPSG code: 4326, 4269 or a WGS 84 UTM zone. |
| `S102-R-3.2` | Warning | `IssueDate`, when set, is an ISO 8601 date or date-time, basic (`20260902`) or extended (`2026-09-02`). |
| `S102-R-3.3` | Warning | `IssueTime`, when set, is an ISO 8601 time of day, basic (`105406+0000`) or extended (`10:54:06Z`). |
| `S102-R-4.1` | Error | Each coverage's origin is valid in `HorizontalCRS`: latitude in [-90, 90] and longitude in [-180, 180] when geographic; within UTM easting and northing bounds, and reprojectable to WGS 84, when projected. |
| `S102-R-4.2` | Error | Each coverage's far corner is valid in `HorizontalCRS` in the same way. A geographic extent mustn't cross the antimeridian. |
| `S102-R-5.1` | Warning | Depths other than the fill value are within [-50, 12 000] m. There's one finding per coverage with values outside the range. |
| `S102-PROJ-SCHEMA` | Error | The HDF5 dataset failed schema parsing inside `Validate()`. |

For the validation API and your own rules, see
[Custom catalogues and validation](../../docs/catalogues-and-validation.md).

## See also

- [Reading product data](../../docs/reading-product-data.md): read the depth
  grid cell by cell.
- [Render S-102 to PNG](../../docs/scenarios/render-s102-to-png.md): render a
  dataset from .NET or the command line.
- [Compose S-101 and S-102](../../docs/scenarios/compose-s101-s102.md): draw
  bathymetry under an ENC.
