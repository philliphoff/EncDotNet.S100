# EncDotNet.S100.Core

Core abstractions and pipeline framework for working with S-100 based nautical chart data.

## Overview

This library provides the foundational types used across the EncDotNet.S100 libraries, including:

- **Asset sources** — `IAssetSource` abstraction for reading files from directories (`FileSystemAssetSource`) or ZIP archives (`ZipAssetSource`).
- **HDF5 abstractions** — `IHdf5File` and `IHdf5Group` interfaces for reading HDF5 data without binding to a specific HDF5 library.
- **Lua scripting abstractions** — `ILuaEngine` and `ILuaContext` interfaces for running sandboxed Lua portrayal scripts, plus the `S100LuaHost` host API.
- **Coverage pipeline** — `ICoverageSource`, `ICoverageRenderer<T>`, `CoveragePipeline`, and supporting types (`GridGeoreferencer`, `CoverageColorScheme`, `StyledCoverageLayer`) for rendering gridded data.
- **Vector pipeline** — `IVectorSource`, `IVectorPortrayalCatalogue`, `VectorPipeline`, and `DrawingInstruction` for rendering vector features.
- **Shared types** — `IPortrayalCatalogue`, `ICrsTransform`, `NavigationContext`, `Viewport`, `BoundingBox`, `RgbaColor`, `ColorPalette`.

## Installation

```sh
dotnet add package EncDotNet.S100.Core
```
