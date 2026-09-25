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

## Using a typed model

Open the dataset, then call the typed root's `From` method. It returns the
projection and a list of diagnostics:

```csharp
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S124.DataModel;

var dataset = S124Dataset.Open("navwarn_mixed.gml");
var warning = S124NavigationalWarning.From(dataset, out var diagnostics);

Console.WriteLine($"{warning.Preamble?.GeneralArea}, {warning.Preamble?.Locality}: {warning.Parts.Count} part(s)");
foreach (var diagnostic in diagnostics)
    Console.WriteLine($"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}");
```

`From` throws only when the dataset is completely empty or lacks the root
entity. Everything else it can't read (an unresolved reference, an attribute
that doesn't parse, a feature without geometry) becomes a diagnostic, and the
projection keeps going. Always check the diagnostics: a projection with warnings
may be missing the entities they name. Each typed object keeps any source
attributes it didn't consume in `ExtraAttributes`.

[Reading product data](reading-product-data.md) shows typed models alongside
the other ways to read each product.

## Typed roots by product

| Spec | Typed root | Notes |
|---|---|---|
| S-421 | `S421RoutePlan` | Original precedent; refactored in Pass 1 to consume the shared abstractions. |
| S-124 | `S124NavigationalWarning` | Pass 1 second consumer. |
| S-128 | `S128ProductCatalogue` | Pass 2 — catalogue of nautical products, with resolved `Supersedes` / `SupersededBy` navigation. |
| S-125 | `S125AtonDataset` | Pass 2 — marine aids to navigation. |
| S-201 | `S201AtonInventory` | Pass 2 — IALA AtoN information. |
| S-122 | `S122MarineProtectedAreaDataset` | Pass 2 — catalogue of MPAs / restricted areas / VTS areas with typed information-type bindings. |
| S-127 | `S127MarineServicesDataset` | Pass 2 — marine resources and services. |
| S-129 | `S129UnderKeelClearancePlan` | A single under-keel-clearance management plan. |
| S-131 | `S131HarbourInfrastructureDataset` | Marine harbour infrastructure. |
| S-411 | `S411SeaIceInventory` | An inventory of sea-ice and lake-ice features. |

Every GML-encoded product now has a typed root, each built with
`Sxxx{Root}.From(dataset, out diagnostics)`. S-101 (ISO 8211) and the HDF5
coverage products (S-102, S-104, S-111) have none; read them through their
dataset types.

## Shared abstractions

All in the `EncDotNet.S100.Core` package, namespace
`EncDotNet.S100.DataModel`:

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

The `GmlReference` type (namespace `EncDotNet.S100.Features`, in the
`EncDotNet.S100.Core` package) is the shared
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
