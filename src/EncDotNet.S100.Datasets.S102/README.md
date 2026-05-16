# EncDotNet.S100.Datasets.S102

Reader and coverage portrayal pipeline for S-102 Bathymetric Surface datasets.

## Overview

This library reads S-102 datasets from HDF5 files and provides coverage data (depth and uncertainty grids) for the portrayal pipeline. Key types include:

- **`S102Dataset`** — root model containing horizontal CRS and bathymetric coverages.
- **`S102DatasetReader`** — reads an S-102 dataset from an `IHdf5File`.
- **`S102CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S102PortrayalCatalogue`** — coverage portrayal catalogue for depth shading.
- **`BathymetryCoverage`**, **`BathymetryValue`** — bathymetric data models.

## Portrayal

Portrayal is **PC-driven**: `S102PortrayalCatalogue` executes the
bundled `BathymetryCoverage.lua` rule (S-102 Edition 3.0.0, Annex B)
through an `ILuaEngine` and resolves the emitted `CoverageColor`
tokens (`DEPDW`, `DEPMD`, `DEPMS`, `DEPVS`, `DEPIT`, `NODTA`) against
the bundled `ColorProfiles/colorProfile.xml`. The Day, Dusk, and
Night palettes are all loaded; `SwitchPalette` selects the active
mood.

The four S-102 context parameters flow in via `MarinerSettings`:

- `FourShades` — toggle between two-band (DEPVS / DEPDW split at
  `SafetyContour`) and four-band (DEPVS / DEPMS / DEPMD / DEPDW split
  at `ShallowContour` / `SafetyContour` / `DeepContour`) shading.
- `SafetyContour`, `ShallowContour`, `DeepContour` — depth boundaries
  in metres.

Invariants (`ShallowContour ≤ SafetyContour ≤ DeepContour`) are
clamped (with a diagnostic) rather than throwing — the viewer
settings panel surface area means transient out-of-order slider
values must not crash the render pipeline.

Cells whose depth equals `S102CoverageSource.FillValue` (1,000,000f)
are painted with the active palette's `NODTA` colour via
`CoverageColorScheme.NoDataColor`.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S102
```
