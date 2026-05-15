# Strongly-typed data models

## Why a typed model on top of a feature bag?

The per-spec dataset types (`S101Dataset`, `S124Dataset`, `S421Dataset`,
etc.) deliberately expose datasets as schema-agnostic *feature bags*:
flat collections of `Feature` and `InformationType` instances keyed
by string codes, with weakly-typed attribute dictionaries.

This shape is well-suited for the portrayal pipeline — the XSLT and
Lua portrayal engines themselves walk attributes by name — but
inconvenient for client code that wants to inspect domain concepts
like routes, warnings, or aids to navigation directly.

The **strongly-typed data model** layer in
`EncDotNet.S100.Core.DataModel` provides the shared scaffolding for
spec-specific projections that turn a feature bag into a typed graph
organised around the spec's domain.

## When to use which

| Use case | Layer |
|---|---|
| Portrayal pipeline, drawing instructions, generic feature iteration. | Feature-bag dataset (`SxxxDataset`, `SxxxFeature`). |
| Client code that wants typed access to spec-defined entities (routes, warnings, AtoN, etc.). | Typed data model (`Sxxx{Root}.From(dataset, out diagnostics)`). |
| Programmatic editing / serialising back to GML. | Not supported — typed models are read-only projections. |

## Shared abstractions

All in `EncDotNet.S100.Core.DataModel` (namespace
`EncDotNet.S100.DataModel`):

- **`ProjectionDiagnostic`** — `Severity`, `Message`, `Code`,
  `RelatedId`, `RelatedAttribute`. Stable codes such as
  `xlink.unresolved`, `attribute.parse.int`, `feature.duplicate`,
  `feature.geometry.missing`.
- **`DiagnosticSeverity`** — `Info` / `Warning` / `Error`.
- **`GeoPosition(double Latitude, double Longitude)`** — readonly
  record struct. WGS-84 / EPSG:4326 lat-lon ordering per S-100 Part 10b
  §6.2.
- **`ProjectionContext`** — bundle of diagnostics list + xlink resolver
  passed by reference through projection methods.
- **`AttributeParser`** — `TryParseInt`, `TryParseDouble`,
  `TryParseBool`, `TryParseDateTimeOffset`. Invariant culture; ISO 8601
  round-trip per S-100 Part 5 §10. Failures emit
  `attribute.parse.{type}` diagnostics.
- **`XlinkResolver`** — `gml:id` lookup table. Strips the leading `#`
  from `xlink:href`; misses emit `xlink.unresolved`; type mismatches
  also emit a diagnostic.
- **`ExtraAttributes.ExcludeKnown(...)`** — preserves any source
  attributes the typed model did not consume.

The `GmlReference` type (in `EncDotNet.S100.Gml`) is the shared
representation of an `xlink:href` cross-reference, replacing the
per-spec `SxxxReference` types from earlier iterations.

## Contract for typed-model authors

When adding a typed model for a new product spec:

1. Place the types in `src/EncDotNet.S100.Datasets.Sxxx/DataModel/`
   under the namespace `EncDotNet.S100.Datasets.Sxxx.DataModel`.
2. Provide a static factory `SxxxRoot.From(SxxxDataset, out
   IReadOnlyList<ProjectionDiagnostic>)`.
3. Never throw except for "fully empty dataset" /
   "missing root entity" cases. Treat everything else as a
   diagnostic.
4. Build the xlink lookup via `XlinkResolver.Build(...)` from the
   dataset's features and information types. Pass it into a
   `ProjectionContext` that you carry through projection methods.
5. Parse primitive attributes via `AttributeParser.TryParse*`; never
   throw on parse failure.
6. Preserve unknown attributes via
   `ExtraAttributes.ExcludeKnown(attributes, ...known keys)` so
   extensions and future-edition fields round-trip verbatim.
7. Reuse `GeoPosition` for coordinates and `GmlReference` for xlinks.
8. Keep typed-model projection independent of the portrayal pipeline:
   portrayal must continue to run from the feature-bag dataset
   without invoking the typed model.

## Current consumers

| Spec | Typed root | Notes |
|---|---|---|
| S-421 | `S421RoutePlan` | Original precedent; refactored in Pass 1 to consume the shared abstractions. |
| S-124 | `S124NavigationalWarning` | Pass 1 second consumer. |
| S-128 | `S128CatalogueDataset` | Pass 2 — catalogue of nautical products. |
| S-125 | `S125AtonDataset` | Pass 2 — marine aids to navigation. |
| S-201 | `S201AtonDataset` | Pass 2 — IALA AtoN information. |
| S-127 | `S127MarineServicesDataset` | Pass 2 — marine resources and services. |

Other GML-encoded specs (S-122, S-129, S-131, S-411) are intentionally
deferred to a future pass — the shared abstractions continue to be
validated against new consumers before being rolled out further.
