# EncDotNet.S100.Core

Core abstractions and pipeline framework for working with S-100 based nautical chart data.

## Overview

This library provides the foundational types used across the EncDotNet.S100 libraries, including:

- **Asset sources** — `IAssetSource` abstraction for reading files from directories (`FileSystemAssetSource`) or ZIP archives (`ZipAssetSource`).
- **HDF5 abstractions** — `IHdf5File` and `IHdf5Group` interfaces for reading HDF5 data without binding to a specific HDF5 library.
- **HDF5 reader exceptions** — `S100DatasetSchemaException` (a required attribute/group is missing or malformed) and `S100DatasetNotSupportedException` (the file uses an optional spec feature the reader doesn't yet implement). Both carry product, file, group path, spec reference, and a `.WithFile(...)` helper used by processor layers to attach the source file name. `Hdf5RequiredAttributeExtensions` provides `ReadRequiredDoubleAttribute` / `ReadRequiredInt64Attribute` / `ReadRequiredStringAttribute` that translate backend "missing attribute" failures into these typed exceptions.
- **Lua scripting abstractions** — `ILuaEngine` and `ILuaContext` interfaces for running sandboxed Lua portrayal scripts, plus the `S100LuaHost` host API.
- **Coverage pipeline** — `ICoverageSource`, `ICoverageRenderer<T>`, `CoveragePipeline`, and supporting types (`GridGeoreferencer`, `CoverageColorScheme`, `StyledCoverageLayer`) for rendering gridded data.
- **Vector pipeline** — `IVectorSource`, `IVectorPortrayalCatalogue`, `VectorPipeline`, and the `DrawingInstruction` hierarchy (`AreaInstruction`, `LineInstruction`, `PointInstruction`, `TextInstruction`) modelled directly on the S-100 Part 9 display list.
- **Validation framework** (`EncDotNet.S100.Validation`) — spec-agnostic types for expressing normative-clause checks against the typed data models: `IValidationRule<TModel>`, `ValidationRuleSet<TModel>` (lint-pass runner that collects all findings and traps per-rule exceptions), `ValidationFinding` (rule id, severity, message, optional `GeoPosition`/`BoundingBox`, related feature id), `ValidationSeverity`, `ValidationContext` (carries `ReferenceTime` and an opaque `IServiceProvider?` for Tier-3 cross-dataset rules), `ValidationReport`, and a fluent `ValidationRuleBuilder` (`RuleFor<T>("rule-id").Check(predicate, msg).Build()` and `.Yield(producer)` for multi-finding rules). Per-spec rule packs live in the respective `EncDotNet.S100.Datasets.Sxxx/Validation/` folder rather than in separate projects.
- **`Part9DisplayListReader`** — parses the Part 9 display-list XML produced by XSLT-based portrayal pipelines (S-124 / S-129 / S-421) into the same unified `DrawingInstruction` hierarchy that S-101's Lua pipeline emits, so a single renderer can consume both.
- **Shared types** — `IPortrayalCatalogue`, `ICrsTransform`, `Viewport`, `MarinerSettings` (S-100 Part 9 §4.2 mariner selections, including the four depth contours and S-101 portrayal toggles such as `FourShades`, `SimplifiedSymbols`, `RadarOverlay`, `NationalLanguage`), `DepthUnit` and the `DepthFormatting` helper for locale-invariant depth conversion / formatting / parsing across metres, feet, fathoms, and combined fathoms-and-feet, `BoundingBox`, `RgbaColor`, `ColorPalette`.

## Installation

```sh
dotnet add package EncDotNet.S100.Core
```
