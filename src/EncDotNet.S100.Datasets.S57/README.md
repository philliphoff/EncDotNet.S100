# EncDotNet.S100.Datasets.S57

Reader and S-101 translator for IHO S-57 Electronic Navigational Chart (ENC) datasets.

## Overview

This library reads S-57 (Edition 3.1) ENC base cells encoded in ISO 8211 format and translates them into the S-101 in-memory document model so the existing S-100 Part 9A Lua portrayal pipeline (provided by `EncDotNet.S100.Datasets.S101`) can render them. This is **not** an S-52 implementation — symbology is whatever the S-101 portrayal catalogue produces when fed the translated data.

Key types:

- **`S57Dataset`** — entry point; opens a `.000` file and produces a parsed S-57 document.
- **`S57Document`**, **`S57DocumentReader`** — low-level ISO 8211 record parsing (DSID, DSPM, FRID, FOID, ATTF, FSPT, VRID, VRPT, SG2D, SG3D).
- **`S57ToS101Translator`** — translates an `S57Document` into an `S101Document` (`EncDotNet.S100.Datasets.S101`) by remapping object/attribute codes, exploding multi-point soundings, and converting nodes/edges/area-rings into S-101 spatial primitives.
- **`S57S101Mapping`** — embedded code-mapping table sourced from IHO's S-57 → S-101 conversion guidance.

## Limitations (v1)

- **Base cells only.** Update files (`.001`, `.002`, …) are not applied; the reader rejects them with a clear error.
- **Breadth-first feature coverage.** Most common feature classes map to S-101 acronyms; uncommon classes pass through unmapped and may not portray.
- **Listed-value remapping is best-effort.** Some S-57 enumerated attribute values have different numeric codes in S-101.
- **Complex-attribute synthesis is minimal.** Sector lights and similar features that require S-101 nested complex attributes may not portray correctly.

## Usage

```csharp
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Datasets.S101;

var s57 = S57Dataset.Open("US5NY16M.000");
var s101Document = new S57ToS101Translator().Translate(s57);
var dataset = S101Dataset.FromDocument(s101Document);
// Use the existing S-101 pipeline from here:
// S101LuaRuleExecutor, S101FeatureGeometryProvider, etc.
```

## Installation

```sh
dotnet add package EncDotNet.S100.Datasets.S57
```
