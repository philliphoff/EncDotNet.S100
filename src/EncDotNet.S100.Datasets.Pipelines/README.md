# EncDotNet.S100.Datasets.Pipelines

Per-spec `IDatasetProcessor` implementations, the S-98 interoperability
authority, and the validation runner consumed by the viewer and the
MCP server.

## Overview

Each supported product ships an `IDatasetProcessor` that owns a parsed
dataset and exposes a uniform surface for rendering, picking,
enumerating features, and validating:

| Processor | Spec | Pipeline |
|---|---|---|
| `S101DatasetProcessor` | S-101 | Vector (Lua portrayal) |
| `S102DatasetProcessor` | S-102 | Coverage (Lua portrayal) |
| `S104DatasetProcessor` | S-104 | Coverage (hand-coded palette) |
| `S111DatasetProcessor` | S-111 | Coverage (arrow symbology) |
| `S122DatasetProcessor` | S-122 | Vector (XSLT portrayal) |
| `S124DatasetProcessor` | S-124 | Vector (XSLT portrayal) |
| `S125DatasetProcessor` | S-125 | Vector (XSLT portrayal) |
| `S127DatasetProcessor` | S-127 | Vector (XSLT portrayal) |
| `S128DatasetProcessor` | S-128 | Vector (XSLT portrayal) |
| `S129DatasetProcessor` | S-129 | Vector (XSLT portrayal) |
| `S131DatasetProcessor` | S-131 | Vector (Lua portrayal) |
| `S201DatasetProcessor` | S-201 | Vector (XSLT portrayal) |
| `S411DatasetProcessor` | S-411 | Vector (XSLT portrayal) |
| `S421DatasetProcessor` | S-421 | Vector (XSLT portrayal) |
| `S57DatasetProcessor`  | S-57 (legacy) | Translates to S-101, then delegates to the S-101 vector pipeline |

`DatasetPipelineFactory` discriminates an input file by extension,
HDF5 signature, or GML application namespace and returns the matching
processor wrapped in an `IDatasetProcessor`. `ExchangeSetLoader`
walks an S-100 exchange-set catalogue and yields one processor per
dataset entry.

## Validation

Every processor implements `IDatasetProcessor.Validate()`:

```csharp
ValidationReport? Validate();
```

The contract is uniform across coverage and vector products:

- **Lazy + cached.** The first call runs the spec's normative rule
  pack (from the matching `EncDotNet.S100.Datasets.Sxxx.Validation`
  namespace) against the parsed dataset and caches the resulting
  `ValidationReport` on a private field. Subsequent calls return the
  cached report. Validation does not depend on the current palette,
  opacity, or selected time step, so the cache is correct for the
  processor's lifetime.
- **Pure function of the parsed dataset.** Findings carry rule id,
  severity, message, an optional `GeoPosition` / `BoundingBox`, and a
  `RelatedFeatureId` (the FOID for vector features, the HDF5 group
  path for coverage records).
- **`null` means "no rule pack"; `ValidationReport.Empty` means
  "rules evaluated, nothing found".** All fifteen supported products
  now ship a rule pack, so `null` is exotic; the distinction matters
  for client UIs that want to show "not validated" rather than
  "clean".
- **Schema failures degrade gracefully.** Coverage processors wrap
  the rule run in a `try` / `catch (S100DatasetSchemaException)` and
  surface a single `Sxxx-PROJ-SCHEMA` finding carrying the offending
  `GroupPath`, attribute name, and spec reference. Vector processors
  reserve `Sxxx-PROJ-PARSE` for the same purpose.

### `S57DatasetProcessor` — pre-translation + delegation

`S57DatasetProcessor` is the only processor that produces a composite
report. It runs two passes:

1. **Pre-translation** rules over the raw `EncDotNet.S57.S57Document`
   (`S57PreTranslationRules.Default`) — things that don't survive
   translation, e.g. DSID / DSPM presence, `M_COVR` coverage.
2. **Post-translation** rules over the translated S-101 document via
   the standard `S101DatasetRules.Default`.

The two reports are joined by the internal `ConcatReports.Concat`
helper, which preserves finding order, sums counters, and optionally
**rebadges** the second report's rule ids with a prefix. The
processor uses `rebadgePrefix: "S101-as-S57/"` so a finding from
S-101 rule `S101-R-2.1` surfaces as `S101-as-S57/S101-R-2.1` and the
user can tell at a glance which layer of the pipeline a problem came
from. Pre-translation findings keep their `S57-*` ids verbatim.

`ConcatReports` is internal to this assembly and shared with the
matching test project via `InternalsVisibleTo`.

### `ValidationRunner`

`ValidationRunner` is the spec-agnostic entry point used by the
viewer and the MCP server: given an `IDatasetProcessor` it calls
`Validate()` and translates the result into the host's preferred
shape (UI rows, MCP tool response, etc.) without each consumer
needing to know the spec-specific rule namespaces.

## S-98 interoperability

`Interoperability/` houses the S-98 inter-product plumbing
(`InteroperabilityAuthority`, `LayerStackBuilder`, `S98RuleContext`,
`S98DefaultRules`, plus the load-order `LoadOrderInteroperabilityAuthority`
fallback). The authority assigns each layer a display plane (Under
Radar / Standard / Over Radar / Dynamic Arrows) and a within-plane
priority, then evaluates a set of inter-product rules (R-101-102,
R-101-124, R-104, R-111) to drop or transform layers that other
loaded products supersede. The viewer consumes the resulting
ordered `LayerStackEntry` list to compose its paint stack.

See [`docs/design/s98-interoperability.md`](../../docs/design/s98-interoperability.md)
for the full design rationale.

## Other utilities

- `EcdisDisplaySettings`, `FeatureInfoBuilder`, `PickAttribute`,
  `CoveragePickHelper`, `StationTimeSeriesSnapshot` — shared building
  blocks for the per-processor `Render` / `GetFeatureInfo` /
  `GetCoverageInfo` paths.
- `GmlDatasetProcessorBase` — common base for the GML-encoded vector
  processors (S-122 / S-124 / S-125 / S-127 / S-128 / S-129 / S-131 /
  S-201 / S-411 / S-421).
- `AssetSourceHelpers` — exchange-set + loose-dataset bootstrapping.
- `Diagnostics/` — `ActivitySource` / `Meter` instrumentation
  consumed by the OpenTelemetry exporter (see
  [`docs/observability.md`](../../docs/observability.md)).
