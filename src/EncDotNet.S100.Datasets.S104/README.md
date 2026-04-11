# EncDotNet.S100.Datasets.S104

Reader and coverage pipeline for S-104 Water Level Information for Surface Navigation datasets.

## Overview

This library reads S-104 datasets from HDF5 files and provides time-series water level height and trend grids for the portrayal pipeline. Key types include:

- **`S104Dataset`** — root model containing horizontal CRS, data coding format, and time-step coverages.
- **`S104DatasetReader`** — reads an S-104 dataset from an `IHdf5File` (regular grid format).
- **`S104CoverageSource`** — `ICoverageSource` adapter for the coverage pipeline.
- **`WaterLevelCoverage`**, **`WaterLevelValue`** — water level data models.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S104
```
