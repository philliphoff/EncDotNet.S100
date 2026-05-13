# EncDotNet.S100.Datasets.S131

Reader and Lua portrayal pipeline for **IHO S-131 Marine Harbour
Infrastructure** datasets (GML encoding, S-100 Part 10b).

## Specification editions

| Asset | Edition | Notes |
|---|---|---|
| Feature Catalogue | 1.0.0 | 31 feature types, 14 information types |
| Portrayal Catalogue | 2.0.0 | 42 Lua scripts, 6 SVG symbols |
| GML Application Schema | 1.0.0 | Namespace `http://www.iho.int/S131/1.0` |

## Screenshot

S-131 Halifax Harbour sample rendered in the S-100 Viewer:

![S-131 Halifax Harbour](../../readme/s131-viewer.png)

## Architecture — GML + Lua hybrid

S-131 is **unique** in this codebase: it combines **GML data encoding**
(like S-122, S-124, S-125, S-127, S-128, S-411, S-421) with **Lua
portrayal** (like S-101). All other GML products use XSLT portrayal;
all other Lua products use ISO 8211 or HDF5 encoding. S-131 is the
first GML+Lua hybrid.

The bridge architecture:

```
S-131 GML → S131DatasetReader → S131Dataset
                                    ↓
                             S131LuaDataProvider (GML→Lua Host API)
                                    ↓
                             S131LuaRuleExecutor (MoonSharp Lua 5.2)
                                    ↓
                             DrawingInstructionParser (reused from S-101)
                                    ↓
                             VectorPipeline → MapsuiDisplayListRenderer
```

### Key design decisions

- **Custom `S131DatasetProcessor`**: Does not extend
  `GmlDatasetProcessorBase` (which hardwires XSLT). Instead follows
  the `S101DatasetProcessor` pattern with Lua infrastructure.
- **Numeric ID mapping**: GML string IDs (`gml:id`) are mapped to
  sequential numeric IDs for the Lua Host API contract.
- **Synthetic spatial records**: GML embeds geometry inline; the data
  provider synthesizes spatial association structures compatible with
  S-101's `HostGetSpatialData` contract.
- **FC-driven discrimination**: Feature vs. information type
  classification uses a set of known information type codes from the
  Feature Catalogue, handling S-131's unified `<S131:members>` container
  (no `<member>`/`<imember>` split).

## GML shape

```xml
<S131:Dataset xmlns:S131="http://www.iho.int/S131/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0">
  <S131:members>
    <!-- ALL features AND information types in one container -->
    <S131:Berth gml:id="f1">...</S131:Berth>
    <S131:ContactDetails gml:id="info1">...</S131:ContactDetails>
  </S131:members>
</S131:Dataset>
```

## Public API

| Type | Description |
|---|---|
| `S131Dataset` | Parsed dataset model (features + information types) |
| `S131Feature` | Feature with geometry, attributes, complex attributes, xlink refs |
| `S131InformationType` | Information type with attributes |
| `S131DatasetReader` | GML parser; namespace-driven feature recognition |
| `S131LuaDataProvider` | GML-to-Lua Host API bridge |
| `S131LuaRuleExecutor` | Lua portrayal executor (Part 9A) |
| `S131PortrayalCatalogue` | Portrayal catalogue (symbols, palettes, rules) |

## Usage

```csharp
// Parse a dataset
var dataset = S131Dataset.Open("path/to/dataset.gml");

// Or from a stream
using var stream = File.OpenRead("dataset.gml");
var dataset = S131Dataset.Open(stream);

// Access features
foreach (var feature in dataset.Features)
{
    Console.WriteLine($"{feature.FeatureType}: {feature.Id}");
}
```

## Dependencies

- `EncDotNet.S100.Core` — GML abstractions, pipeline interfaces
- `EncDotNet.S100.Datasets.S101` — `DrawingInstructionParser` reuse
- `EncDotNet.S100.Features` — Feature Catalogue decoder
- `EncDotNet.S100.Portrayals` — Portrayal Catalogue types
