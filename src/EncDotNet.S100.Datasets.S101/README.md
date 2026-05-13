# EncDotNet.S100.Datasets.S101

Reader and Lua portrayal pipeline for S-101 Electronic Navigational Chart (ENC) datasets.

## Overview

This library reads S-101 datasets encoded in ISO 8211 format and executes the S-100 Part 9A Lua portrayal pipeline to produce drawing instructions. Key types include:

- **`S101Dataset`** — parsed ENC dataset containing features from ISO 8211 records.
- **`S101Document`**, **`S101DocumentReader`** — low-level ISO 8211 record parsing.
- **`S101LuaRuleExecutor`** — `ILuaRuleExecutor` implementation that orchestrates the Lua portrayal pipeline (loads `main.lua`, calls `PortrayalMain()`, parses and post-processes the emitted instructions).
- **`S101PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT/Lua rules, symbols, line styles, area fills, and color palettes.
- **`S101VectorSource`** — `IVectorSource` implementation for the vector pipeline.
- **`DrawingInstructionParser`** — parses the semicolon-separated key:value strings emitted by the Lua portrayal pipeline into the unified `DrawingInstruction` hierarchy from `EncDotNet.S100.Core`. Honours text alignment (`TextAlignHorizontal` / `TextAlignVertical`), mm offsets (`LocalOffset`), foreground / background colour with optional transparency, line placement, and the `AugmentedPoint:GeographicCRS,…` anchor override used by SOUNDG / DepthNoBottomFound rules. Augmented line geometry (`AugmentedRay`, `ArcByRadius`, `AugmentedPath`) is fully supported — sector-light limit lines and arcs, directional-light rays, and all-around-light circles are tessellated into polylines and carried through `LineInstruction.CoordinatesOverride` to the renderer.

## Record types

`S101DocumentReader` parses the following ISO 8211 record types:

| Tag | Record type | Notes |
|-----|-------------|-------|
| DSID | Dataset identification | Version, edition, product spec |
| DSSI | Dataset structure info | COMF / SOMF scaling factors |
| PRID | Point | Single 2D coordinate |
| MRID | MultiPoint | 3D sounding arrays via C3IL field (VCID leader + YCOO/XCOO/ZCOO repeating group) |
| CRID | Curve segment | Ordered coordinate sequences |
| CCID | Composite curve | References to curve segments |
| SRID | Surface | Ring-based polygon geometry |
| FRID | Feature | Object class, attributes, spatial associations |
| IRID | Information type | Metadata records referenced by features |

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S101
```
