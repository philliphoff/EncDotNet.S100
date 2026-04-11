# EncDotNet.S100.Datasets.S102

Reader and coverage portrayal pipeline for S-102 Bathymetric Surface datasets.

## Overview

This library reads S-102 datasets from HDF5 files and provides coverage data (depth and uncertainty grids) for the portrayal pipeline. Key types include:

- **`S102Dataset`** — root model containing horizontal CRS and bathymetric coverages.
- **`S102DatasetReader`** — reads an S-102 dataset from an `IHdf5File`.
- **`S102CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`S102PortrayalCatalogue`** — coverage portrayal catalogue for depth shading.
- **`BathymetryCoverage`**, **`BathymetryValue`** — bathymetric data models.
- **`CoverageLayer`**, **`CoverageLayerBuilder`** — coverage grid construction utilities.
- **`DepthShading`** — depth-to-color mapping.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S102
```
