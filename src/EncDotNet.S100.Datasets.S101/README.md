# EncDotNet.S100.Datasets.S101

Reader and Lua portrayal pipeline for S-101 Electronic Navigational Chart (ENC) datasets.

## Overview

This library reads S-101 datasets encoded in ISO 8211 format and executes the S-100 Part 9A Lua portrayal pipeline to produce drawing instructions. Key types include:

- **`S101Dataset`** — parsed ENC dataset containing features from ISO 8211 records.
- **`S101Document`**, **`S101DocumentReader`** — low-level ISO 8211 record parsing.
- **`S101LuaPortrayal`** — orchestrates the Lua portrayal pipeline (loads `main.lua` and calls `PortrayalMain()`).
- **`S101PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT/Lua rules, symbols, line styles, area fills, and color palettes.
- **`S101VectorSource`** — `IVectorSource` implementation for the vector pipeline.
- **`DrawingInstructionParser`** — parses the semicolon-separated key:value strings emitted by the Lua portrayal pipeline into the unified `DrawingInstruction` hierarchy from `EncDotNet.S100.Core`. Honours text alignment (`TextAlignHorizontal` / `TextAlignVertical`), mm offsets (`LocalOffset`), foreground / background colour with optional transparency, line placement, and the `AugmentedPoint:GeographicCRS,…` anchor override used by SOUNDG / DepthNoBottomFound rules. Augmented line geometry (`AugmentedRay`, `ArcByRadius`, `AugmentedPath`) is recognised but not yet rendered — sector lights and all-around-light circles will log a warning.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S101
```
