# EncDotNet.S100.Datasets.S111

This package reads S-111 Surface Currents datasets from HDF5 files: current
grids per time step, and time series at stations or mesh nodes. It also draws
them as arrows with the S-111 portrayal catalogue and validates them against
the S-111 Edition 2.0.0 checklist. Reference it when you need current speeds
and directions directly. To open and render a dataset without wiring
catalogues yourself, use the [`EncDotNet.S100`](../EncDotNet.S100/README.md)
package instead.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.S111
dotnet add package EncDotNet.S100.Hdf5.PureHdf
```

`EncDotNet.S100.Hdf5.PureHdf` provides the HDF5 reader the example uses.

## Read a dataset

A file holds either a regular grid per time step or a series of values per
station. `ReadAny` returns whichever the file contains:

```csharp
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Hdf5.PureHdf;

using var file = PureHdfFile.Open("path/to/dataset.h5");

switch (S111DatasetReader.ReadAny(file))
{
    case S111DatasetData.GriddedCoverage gridded:
        foreach (SurfaceCurrentCoverage step in gridded.Dataset.Coverages.Take(3))
            Console.WriteLine($"{step.TimePoint:u}: {step.Values.Length} cells");
        break;

    case S111DatasetData.StationSeries stations:
        foreach (SurfaceCurrentStation station in stations.Dataset.Stations)
            Console.WriteLine($"{station.Identifier}: {station.NumberOfTimes} samples");
        break;
}
```

Speeds are in knots on every path, as S-111 encodes `surfaceCurrentSpeed`:
`SurfaceCurrentValue.Speed` for grids and `SurfaceCurrentStation.SpeedsKnots`
for stations. Nothing is converted on read. Directions are in degrees true,
the direction the current flows towards. For more, see
[Reading product data](../../docs/reading-product-data.md).

## Main types

- **`S111Dataset`**: a gridded dataset, with its horizontal CRS, depth, data
  coding format and one coverage per time step.
- **`S111DatasetReader`**: reads data coding formats 1, 2, 3 and 8. For
  format 1, each `Group_NNN/timePoint` is the timestamp, and the time-major
  values are transposed into per-node series. The reader ignores instance
  cadence metadata that conflicts with those timestamps, which occurs in
  production IC-ENC files.
- **`S111ReadOptions`**: read options. `DeferValueReads` reads the values of
  each time step only when you first use them, for format 2 grids. See
  [Read values on demand](#read-values-on-demand).
- **`SurfaceCurrentCoverage`**, **`SurfaceCurrentValue`** and
  **`SurfaceCurrentStation`**: a grid at one time step, its values, and one
  station's series.
- **`S111CoverageSource`**: the `ICoverageSource` for the coverage pipeline.
- **`S111PortrayalCatalogue`**: the arrow portrayal. See
  [Portrayal](#portrayal).
- **`S111SpeedBandReader`**: reads the nine speed bands and three scale
  constants from the bundled `Rules/select_arrow.xsl`.

The `S111DatasetProcessor` that renders, samples and validates a dataset is in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md).

## Read values on demand

With `DeferValueReads`, the reader reads only the instance metadata up front:

```csharp
var data = S111DatasetReader.ReadAny(file, new S111ReadOptions { DeferValueReads = true });
```

Each time step's `values` compound is read the first time you access it. Time
points are calculated as `dateTimeOfFirstRecord` + i × `timeRecordInterval`
(S-111 Edition 2.0.0 §10.2.6), so opening the file doesn't read every
`Group_NNN`. `SurfaceCurrentCoverage.Values` decodes the array under a lock on
first access and keeps it. Deferral applies to format 2 grids. Station series
(formats 1, 3 and 8) are always read in full.

Keep the file open while you use deferred values. `S111DatasetProcessor`
defers format 2 reads, so it keeps the HDF5 file or stream open and implements
`IDisposable`. Dispose the processor to release the file. A processor for a
station series reads everything and closes the file straight away.

## Portrayal

`S111PortrayalCatalogue` uses the IHO portrayal catalogue bundled in
`content/S111/pc/` of `EncDotNet.S100.Specifications`:

- **Speed bands and scale constants** come from `Rules/select_arrow.xsl`.
  `S111SpeedBandReader` parses it on first use, and each catalogue instance
  caches the result. The stylesheet defines the nine
  `SurfaceCurrentSpeedBand{N}` ranges, which map to colour tokens `SCBN{N}` and
  SVG symbols `SCAROW0{N}`, and the `scaleFloor`, `scaleCeiling` and
  `scaleFactorIntermediate` variables. None of these values are in C#.
- **Day, Dusk and Night palettes** come from `ColorProfiles/colorProfile.xml`.
  `SwitchPaletteAsync(PaletteType)` selects one, and `ResolveSymbolScheme` and
  `ActivePalette` reflect the change straight away. Call it before
  `ResolveSymbolScheme`, which needs the speed bands it loads.

`ResolveColorScheme` returns `null`: the catalogue defines arrows only, with no
`<coverageFill>` instruction for `surfaceCurrentSpeed`. A colour fill
synthesised from the speed bands would hide the S-101 chart underneath. Each
band's colour travels with its arrow SVG through the `fSCBN{N}` CSS class, and
`MapsuiCoverageArrowRenderer` resolves that token against the active palette.

### Arrows

Every data coding format is drawn with the catalogue's `SCAROW` arrows:

- Format 2 grids go through `MapsuiCoverageArrowRenderer` in the viewer and
  `SkiaCoverageArrowRenderer` in headless rendering (`s100 render`).
- Station series and ungeorectified meshes (formats 1, 3 and 8) go through the
  glyph sub-layer of `S111DatasetProcessor`.

Both draw one symbol per node that survives thinning, rasterised at screen DPI
so arrows stay sharp and keep the same on-screen size at every zoom (S-111
§9.2.4 sizes arrows in millimetres on the display). Opening an S-111 dataset
needs the S-111 portrayal catalogue; there's no fallback symbology without it.

### Conformance to the specifications

How this package follows S-111 Edition 2.0.0, its portrayal catalogue, and
S-98 Edition 2.0.0:

| Rule | Source | Implementation |
|---|---|---|
| Arrow shape, pivot at the symbol's centre, black border | §9.2.1, Figure 9-1, Annex H Rule 1 | The bundled `SCAROW01`–`09.svg`, centred on the node. |
| Direction is where the current flows towards, clockwise from true north | §9.2.2, Rule 7 | Rotation is `surfaceCurrentDirection` (clockwise) on a north-up Mercator display, and turns with a rotated map. |
| Nine speed bands, each with a symbol and colour token `SCBN1`–`9` | §9.2.3, Rules 3 and 5, `select_arrow.xsl` | Bands are read from `select_arrow.xsl`; colours come from the active palette in `colorProfile.xml`. |
| Day, dusk and night colours from the portrayal catalogue | Rule 4, Annex F | `SwitchPaletteAsync`. Composite rendering (`--layer`) uses the palette too. |
| Size `H = Href · min(max(Slow, S), Shigh) / Sref` (Href 10 mm, Sref 5 kn, Slow 2 kn, Shigh 13 kn), the same for every data source | §9.2.4, Eqn 9.1, Rule 6 | The catalogue's scale factors, 0.40, 0.20 × S and 2.60, times the 10 mm arrow, for every data coding format. |
| No arrow when speed or direction is null | Rule 2 | Fill values and `NaN` are skipped on every path. |
| Fewer symbols when zoomed out | S-98 §13.1, S-111 §9.3.2 | Zoom-dependent thinning on every path (below). |
| Regular grids: every n-th row and column, `n = 1 + fix(Lsmax / (D · Rmax))`, seeded so the largest vector is drawn | §9.3.2 Eqn 9.2/9.3, Annex H Rule 11, S-98 Appendix G-1.1 | `SymbolThinning.ThinGrid`, with `D` the on-screen cell diagonal and `Lsmax` the largest arrow in the displayed field. |
| Irregular data and ungeorectified grids: remove overlapping points one by one, or use an implementer's method | §9.3.2, §9.3.3 | `SymbolThinning.ThinPoints`. |
| No spatial interpolation when zoomed in | §9.3.1 | Arrows are drawn only at nodes, so zooming in shows fewer arrows. |
| Viewing group 33060, display plane UnderRadar, drawing priority 10 | `SurfaceCurrent.xsl` | Arrows for every format go to the `DynamicArrows` plane at priority 10. S-98 names no plane for arrows (Main §9.2.1 layer 6 only ranks surface currents as on-demand data), so this plane is this package's choice. |

### Implementation choices

Where the specifications leave a choice to the implementer:

- **`Rmax` = 0.5** (`SymbolThinning.DefaultMaxSymbolToSpacingRatio`) for grids
  and points. This is the value S-111 and S-98 recommend. The drawn cell
  diagonal is then at least twice the largest arrow, so arrows don't overlap.
- **Seed point.** The thinning lattice is seeded at the fastest current among
  the largest arrows in view, not at the first largest arrow in row order. The
  two only differ in breaking ties: below 2 kn every arrow has the `Slow` size,
  so a seed in row order would make the lattice jump whenever panning changed
  the top-left cell. S-98 G-1.1 allows adapting the seed.
- **Displayed field.** `Lsmax` and the seed are taken over the view grown by
  half the largest arrow, so arrows whose pivot is slightly off screen still show.
- **Point clearance.** S-111 §9.3.2 doesn't define "overlap". A kept arrow of
  length `L` (the longer of the pair) clears a radius of `L / (Rmax · √2)`,
  the nearest-neighbour spacing of a thinned square grid. Meshes and grids
  therefore thin to the same minimum spacing, about 1.41 arrow lengths, and
  arrows don't overlap. Points are visited fastest first. Thinning runs over
  every node, not only the visible ones, so the selection changes with zoom but
  not while panning.
- **Millimetres to pixels.** Arrow lengths are converted at 96 DPI
  (`SymbolThinning.PixelsPerMillimetre`), the scale at which Mapsui and
  Svg.Skia rasterise the SVGs, which are sized in millimetres.
- **Symbol scale.** `RenderContext.SymbolScale` multiplies `Href`. It's the
  **Symbol Scale** setting in SoundCharts and `--symbol-scale` in `s100
  render`. Annex H Rule 12 suggests letting the user choose `Href` and `Sref`.
  Thinning uses the scaled length, so larger arrows are also spaced further
  apart.
- **No extra transparency.** §9.2.6 asks for alpha 0.4 at dusk and 0.2 at
  night over an ENC. The catalogue's dusk and night colours already include
  Annex F's luminance reduction, and Rule 4 says to use the catalogue colours,
  so no extra alpha is applied. Applying both would make night arrows almost
  invisible.
- **Speed 0.** Band 1 is `[0.00, 0.50)` in `select_arrow.xsl`, so a node with
  a speed of exactly 0 is drawn with the band 1 arrow, in its encoded
  direction.

## Validation

The bundled rule pack, `EncDotNet.S100.Datasets.S111.Validation.S111SurfaceCurrentRules.Default`,
checks an `S111Dataset` against the S-111 Edition 2.0.0 checklist and returns a
`ValidationReport`. `S111DatasetProcessor.Validate()` runs it for you. To run it
directly:

```csharp
using EncDotNet.S100.Datasets.S111.Validation;

var report = S111SurfaceCurrentRules.Default.Run(dataset);
foreach (var finding in report.Findings)
    Console.WriteLine($"{finding.RuleId} {finding.Severity}: {finding.Message}");
```

| Rule ID | Severity | Checks |
|---------|----------|--------|
| `S111-R-1.1` | Error | Each coverage's `Values.Length` equals `NumPointsLatitudinal × NumPointsLongitudinal`. |
| `S111-R-2.1` | Warning | `Coverages` are in strictly increasing `TimePoint` order, and the intervals between them are within ±10% of the median interval. |
| `S111-R-3.1` | Warning | `SurfaceCurrentDepth`, when set, is within 1500 m of its reference level. Depths below the sea surface are negative (`depthTypeIndex`). |
| `S111-R-3.2` | Warning | `TypeOfCurrentData`, when set, is in the S-111 enumeration 1–6. |
| `S111-R-4.1` | Warning | Speeds other than the fill value are within [0, 15] m/s (about 29 kn), after converting from knots. Fill values, `NaN` and ±`Infinity` are skipped. |
| `S111-R-4.2` | Error | Directions other than the fill value are within [0, 360) degrees true. |
| `S111-PROJ-SCHEMA` | Error | The HDF5 dataset failed schema parsing inside `Validate()`. |
| `S111-PROJ-UNSUPPORTED` | Error | The reader raised `S100DatasetNotSupportedException` inside `Validate()`. |
| `S111-STATION-SHAPE` | Error | A station's timestamps, speeds, directions and declared sample count disagree. |
| `S111-STATION-TIME` | Error | A station's explicit timestamps aren't strictly increasing. |
| `S111-STATION-SPEED` | Error | A station has a negative or non-finite speed. |
| `S111-STATION-DIRECTION` | Error | A station has a direction outside 0–360 degrees. |

`S111DatasetProcessor` checks station series itself with the `S111-STATION-*`
rules, because the rule pack works on gridded `S111Dataset`s. `S111-R-2.1`
uses the same time-axis checks as the S-104 rule pack, so the two
time-varying products are checked the same way.

For the validation API and your own rules, see
[Custom catalogues and validation](../../docs/catalogues-and-validation.md).

## See also

- [Reading product data](../../docs/reading-product-data.md): read grids and
  station series for S-111 and S-104.
- [Command-line rendering](../../docs/cli.md): render S-111 time steps with
  `s100 render`.
- [S-98 interoperability](../../docs/design/s98-interoperability.md): how
  arrows are layered with other products.
