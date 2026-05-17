# EncDotNet.S100.Datasets.S104

Reader and coverage pipeline for S-104 Water Level Information for Surface Navigation datasets.

## Overview

This library reads S-104 datasets from HDF5 files and provides time-series water level height and trend grids for the portrayal pipeline. Key types include:

- **`S104Dataset`** — root model containing horizontal CRS, data coding format, and time-step coverages.
- **`S104DatasetReader`** — reads an S-104 dataset from an `IHdf5File` (regular grid format).
- **`S104CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S104PortrayalCatalogue`** — viewer-parity heatmap catalogue with hand-coded Day / Dusk / Night band tables (see *Portrayal* below).
- **`WaterLevelCoverage`**, **`WaterLevelValue`** — water level data models.

## Portrayal

S-104 Edition 2.0.0 **does not define an official portrayal catalogue** — the spec
treats water-level data as input to ECDIS depth adjustment rather than a visual
layer. `S104PortrayalCatalogue` therefore ships **hand-coded** Day / Dusk / Night
band tables synthesised for viewer parity with the other coverage products
(S-102, S-111):

| Palette | Band styling                                                                                  | NoData fill                |
|---------|-----------------------------------------------------------------------------------------------|----------------------------|
| Day     | ColorBrewer-style diverging blue (below datum) → green (above datum), preserved byte-for-byte | transparent (`#00000000`)  |
| Dusk    | Day with saturation × 0.70 and lightness × 0.85                                               | dim cool grey (`#4A4A4AFF`)|
| Night   | ECDIS night-mode dark navy / olive, all luminance < 0.2                                       | darker dim grey (`#1A1A1AFF`)|

`SwitchPalette(PaletteType)` actually swaps the active band table (the pre-PR-H
implementation was a no-op). `ResolveColorScheme` populates
`CoverageColorScheme.NoDataColor` so the renderer paints S-104 fill cells
(`S104CoverageSource.FillValue`, `-9999f`) with the active palette's no-data
colour rather than leaving them transparent.

If IHO publishes an official S-104 portrayal catalogue, the bundled
`content/S104/pc/` directory (today `.gitkeep`-only by design) will be the
landing point and this catalogue will be re-wired against it.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S104
```
