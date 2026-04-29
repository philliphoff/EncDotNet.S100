# EncDotNet.S100.Core

Core abstractions and pipeline framework for working with S-100 based nautical chart data.

## Overview

This library provides the foundational types used across the EncDotNet.S100 libraries, including:

- **Asset sources** — `IAssetSource` abstraction for reading files from directories (`FileSystemAssetSource`) or ZIP archives (`ZipAssetSource`).
- **HDF5 abstractions** — `IHdf5File` and `IHdf5Group` interfaces for reading HDF5 data without binding to a specific HDF5 library.
- **Lua scripting abstractions** — `ILuaEngine` and `ILuaContext` interfaces for running sandboxed Lua portrayal scripts, plus the `S100LuaHost` host API.
- **Coverage pipeline** — `ICoverageSource`, `ICoverageRenderer<T>`, `CoveragePipeline`, and supporting types (`GridGeoreferencer`, `CoverageColorScheme`, `StyledCoverageLayer`) for rendering gridded data.
- **Vector pipeline** — `IVectorSource`, `IVectorPortrayalCatalogue`, `VectorPipeline`, and the `DrawingInstruction` hierarchy (`AreaInstruction`, `LineInstruction`, `PointInstruction`, `TextInstruction`) modelled directly on the S-100 Part 9 display list.
- **`Part9DisplayListReader`** — parses the Part 9 display-list XML produced by XSLT-based portrayal pipelines (S-124 / S-129 / S-421) into the same unified `DrawingInstruction` hierarchy that S-101's Lua pipeline emits, so a single renderer can consume both.
- **Shared types** — `IPortrayalCatalogue`, `ICrsTransform`, `NavigationContext`, `Viewport`, `BoundingBox`, `RgbaColor`, `ColorPalette`.

## Installation

```sh
dotnet add package EncDotNet.S100.Core
```
