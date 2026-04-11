# EncDotNet.S100.Datasets.S111

Reader and coverage portrayal pipeline for S-111 Surface Current datasets.

## Overview

This library reads S-111 datasets from HDF5 files and provides time-series current speed and direction grids for the portrayal pipeline. Key types include:

- **`S111Dataset`** — root model containing horizontal CRS, depth, data coding format, and time-step coverages.
- **`S111DatasetReader`** — reads an S-111 dataset from an `IHdf5File` (regular grid format).
- **`S111CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S111PortrayalCatalogue`** — coverage portrayal catalogue for current arrow rendering.
- **`SurfaceCurrentCoverage`**, **`SurfaceCurrentValue`** — surface current data models.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S111
```
