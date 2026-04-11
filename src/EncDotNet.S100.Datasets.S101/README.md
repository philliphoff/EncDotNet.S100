# EncDotNet.S100.Datasets.S101

Reader and Lua portrayal pipeline for S-101 Electronic Navigational Chart (ENC) datasets.

## Overview

This library reads S-101 datasets encoded in ISO 8211 format and executes the S-100 Part 9A Lua portrayal pipeline to produce drawing instructions. Key types include:

- **`S101Dataset`** — parsed ENC dataset containing features from ISO 8211 records.
- **`S101Document`**, **`S101DocumentReader`** — low-level ISO 8211 record parsing.
- **`S101LuaPortrayal`** — orchestrates the Lua portrayal pipeline (loads `main.lua` and calls `PortrayalMain()`).
- **`S101PortrayalCatalogue`** — `IVectorPortrayalCatalogue` implementation that loads XSLT/Lua rules, symbols, line styles, area fills, and color palettes.
- **`S101VectorSource`** — `IVectorSource` implementation for the vector pipeline.
- **`DrawingInstructionParser`** — parses drawing instructions emitted by the Lua pipeline.

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S101
```
