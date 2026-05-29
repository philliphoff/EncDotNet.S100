# EncDotNet.S100.Datasets.S111

Reader and coverage portrayal pipeline for S-111 Surface Current datasets.

## Overview

This library reads S-111 datasets from HDF5 files and provides time-series current speed and direction grids for the portrayal pipeline. Key types include:

- **`S111Dataset`** — root model containing horizontal CRS, depth, data coding format, and time-step coverages.
- **`S111DatasetReader`** — reads an S-111 dataset from an `IHdf5File` (regular grid format).
- **`S111CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S111PortrayalCatalogue`** — coverage portrayal catalogue for current arrow rendering (see *Portrayal* below).
- **`S111SpeedBandReader`** — parses the 9 surface-current speed bands and the three scale constants from the bundled `Rules/select_arrow.xsl`.
- **`SurfaceCurrentCoverage`**, **`SurfaceCurrentValue`** — surface current data models.

## Portrayal

`S111PortrayalCatalogue` is driven by the bundled IHO portrayal catalogue under
`EncDotNet.S100.Specifications`' `content/S111/pc/` tree:

- **Speed bands & scale constants** — parsed from `Rules/select_arrow.xsl` on
  first use by `S111SpeedBandReader` and cached per-catalogue instance. The
  XSLT supplies the 9 `SurfaceCurrentSpeedBand{N}` ranges (mapped to colour
  tokens `SCBN{N}` and SVG symbols `SCAROW0{N}`) plus the `scaleFloor`,
  `scaleCeiling` and `scaleFactorIntermediate` variables — no values are
  hard-coded in C#.
- **Day / Dusk / Night palettes** — read from
  `ColorProfiles/colorProfile.xml`. `SwitchPalette(PaletteType)` activates
  the chosen palette; `ResolveSymbolScheme` and `ActivePalette` reflect the
  change immediately.

### No coverage colour fill

`ResolveColorScheme` returns `null`. The bundled portrayal catalogue
(`content/S111/pc/Rules/select_arrow.xsl`) defines arrow symbology only —
there is no `<coverageFill>` instruction on `surfaceCurrentSpeed`. Synthesising
a continuous heatmap from the speed-band table (as an earlier viewer prototype
did) actively obscured the underlying S-101 chart, so the colour-band sub-layer
has been removed. Per-band colour now travels with the arrow SVG itself via its
`fSCBN{N}` CSS class; `MapsuiCoverageArrowRenderer` resolves that token via the
active palette.

### Per-feature arrow rendering

`MapsuiCoverageArrowRenderer` emits **one Mapsui `PointFeature` per selected
grid cell** carrying an `ImageStyle` that wraps the bundled SCAROW SVG via the
`svg-content://` URI scheme. Mapsui re-rasterises the symbol on every viewport
change so arrows stay **sharp at every zoom** and at a **stable on-screen
size** — the convention used by ECDIS-style symbology in S-100 Part 9 §11.
A previous implementation rasterised every arrow into a single georeferenced
PNG sized to the dataset extent; that bitmap was downscaled at low zoom
(arrows shrank to a few pixels and were hard to read) and upscaled at high
zoom (arrows pixelated). Per-feature symbols avoid both pathologies.

Per-band scaling follows the bundled catalogue
(`content/S111/pc/Rules/select_arrow.xsl`): bands 1-3 share
`scaleFloor = 0.40`; bands 4-8 use `scaleFactorIntermediate = 0.20`
multiplied by `surfaceCurrentSpeed`; band 9 uses `scaleCeiling = 2.60`.
Those per-band factors multiply the renderer's `BaseSymbolScale` (default
`1.0` — which `S111DatasetProcessor` overrides with the user's
`RenderContext.SymbolScale` so the viewer's Symbol Scale slider tunes
arrow size) to produce each feature's `ImageStyle.SymbolScale`.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S111
```
