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
  the chosen palette; `ResolveColorScheme` and `ResolveSymbolScheme`
  reflect the change immediately.
- **NoData fill** — `CoverageColorScheme.NoDataColor` is populated from the
  active palette using the token-preference chain
  `NODTA → CHBLK → #00000000` (transparent). The bundled S-111 colour
  profile does not define `NODTA`, so cells with the HDF5 fill value
  (`-9999f`) render with the palette's `CHBLK`.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S111
```
