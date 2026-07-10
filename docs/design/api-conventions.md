# API design conventions

Cross-cutting conventions for the public and internal API surface of the
EncDotNet.S100 libraries. These are normative: new code should follow them,
and existing code is migrated toward them opportunistically.

> Companion documents: [`quantity-types.md`](quantity-types.md) covers the
> strongly-typed physical quantities (`Length`, `Depth`, `Angle`, `Speed`).

## 1. Collections on model and API surfaces

**Expose read-only sequences as `IReadOnlyList<T>` and read-only maps as
`IReadOnlyDictionary<TKey, TValue>`.** Do not expose
`System.Collections.Immutable` types (`ImmutableArray<T>`,
`ImmutableDictionary<TKey, TValue>`, `ImmutableList<T>`) in public or internal
API signatures.

### Why not the immutable types?

The data model is read-once, render-many, so the collections never need
structural-sharing edits. The immutable types were used out of habit, not
necessity — nothing underneath (GML/ISO 8211/HDF5 parsing) produces them; we
build them ourselves. Weighed against that, they carry real costs here:

- **`default(ImmutableArray<T>)` is a trap.** It is a struct whose default
  value throws `NullReferenceException` on enumeration, which is why the code
  had to sprinkle `= ImmutableArray<T>.Empty` initializers and
  `.IsDefaultOrEmpty` guards everywhere. `IReadOnlyList<T>` has an honest
  `null` and a plain `= []` default.
- **Neither immutable nor read-only collections give a `record` structural
  equality.** `ImmutableArray<T>.Equals` compares the underlying **array
  reference**; `IReadOnlyList<T>` fields fall back to `object.ReferenceEquals`.
  So the immutable types do not buy the value semantics one might assume — see
  §2.
- **Ergonomics.** `IReadOnlyList<IReadOnlyList<T>>` reads better than
  `ImmutableArray<ImmutableArray<T>>`, and construction needs no builders.

The genuine benefit the immutable types *do* provide — a hard guarantee the
consumer cannot cast-and-mutate — is low-value here: all consumers are
first-party, and a class exposing `IReadOnlyList<T>` backed by a privately-held
array is immutable in practice. If a specific hotspot later needs a hard
guarantee (or a `record` needs structural collection equality), reintroduce an
immutable/equatable type **at that site**, not across the board.

### Choosing the return type: `IEnumerable<T>` vs a read-only collection

The read-only vocabulary has three tiers; pick by what the result *is*:

| Return type | Use when |
|---|---|
| `IReadOnlyList<T>` | the result is a finite, ordered, already-materialized sequence (the common case for model data) |
| `IReadOnlyCollection<T>` / `IReadOnlySet<T>` / `IReadOnlyDictionary<K,V>` | the result is finite and materialized but is semantically a count-only bag, a set (membership/uniqueness), or a map |
| `IEnumerable<T>` | the result is a **genuinely deferred producer** (e.g. a rule that `yield return`s findings) or a **composable query fragment** the caller is expected to filter/aggregate further |

Reserve `IEnumerable<T>` for the last row — do not use it as a lazy hedge for
data you have already materialized. Conversely, do not force a producer or a
composable `.Where(...)`-style helper to allocate a list just to satisfy a
"no `IEnumerable`" rule. Two Core examples that intentionally stay
`IEnumerable<T>`: `IValidationRule<TModel>.Evaluate` (implementations `yield`)
and `ValidationReport.FindingsOfSeverity` (a composable severity filter over an
already-materialized `IReadOnlyList`).

### Do not leak a live mutable collection behind a read-only interface

Returning a mutable `HashSet<T>` / `Dictionary<K,V>` typed as
`IReadOnlySet<T>` / `IReadOnlyDictionary<K,V>` is a hole: a consumer can cast
the reference back to the concrete type and mutate it, bypassing the owner's
invariants (e.g. mutating a controller's state without raising its `Changed`
event). Wrap the live collection so it cannot be downcast:

- Maps: `new ReadOnlyDictionary<K, V>(dict)` (available on all target
  frameworks).
- Sets: `new ReadOnlySetView<T>(set)` (an internal Core wrapper — `ReadOnlySet<T>`
  / `HashSet<T>.AsReadOnly()` are .NET 9+ only and Core also targets .NET 8).

Both are live projections (they reflect the owner's later edits), O(1) to
construct. This is worth doing even though all consumers are first-party,
because the hazard here is not mere immutability but silently breaking an
observable invariant — a stronger reason than the general defensiveness §1
declines above.

## 2. `class` vs `record` for data models

- Use **`record` / `readonly record struct`** for small scalar *value types*
  where value equality is meaningful and there are no collection fields —
  e.g. `SpecRef`, `CatalogueRef`, and the quantity types.
- Use **`class`** for collection-bearing models that are built once and read —
  e.g. the `IS100Feature` family. A `record` here would *advertise* value
  equality it cannot honor (its collection fields compare by reference), which
  is a footgun; a `class` is honest.
- If a collection-bearing type genuinely needs structural value equality,
  introduce a dedicated `EquatableArray<T>` (a thin `readonly struct` over a
  backing array implementing `IReadOnlyList<T>` plus `IEquatable<>` with
  `SequenceEqual`/combined hash) and use it **only at that type**. Do not add
  such a primitive speculatively.
- Exception types, builders, and types that carry behavior (methods, mutable
  workflow state) are **always `class`**, never `record`, regardless of size.

### Applying the policy to existing code

This is a **forward rule**, applied opportunistically to existing code rather
than via a mass rewrite:

- Pure-scalar value types that were `sealed class` and want value equality have
  been converted to `record` — notably `BoundingBox` and `Viewport` (also gains
  `with`). Other minor value-ish candidates (`GmlReference`,
  `ProjectionDiagnostic`, `ContourStyle`) may be converted opportunistically
  when a file is next touched.
- Existing collection-bearing `record` types (e.g. the per-product `DataModel`
  families, `ValidationReport`, `DynamicFeature`, and the MCP request/result
  DTOs) are **grandfathered as records**. Their collection fields compare by
  reference, but these types are read-once/render-many and are not compared by
  value anywhere, so the mismatch is dormant. Do **not** churn them wholesale;
  when adding a *new* collection-bearing model, make it a `class` per the rule
  above. For MCP/JSON DTOs a `record` remains acceptable (value equality is
  never exercised and `record` is idiomatic for payloads).

## 3. Missing-value and lookup-failure strategy

When a lookup, resolution, or mapping can fail to produce a value, pick the
signalling strategy by *what the failure means*, not by habit:

| Situation | Strategy | Example |
|---|---|---|
| **Expected absence** the caller is meant to handle inline | return nullable `T?` (or `bool TryGet(out T)` for value types) | `IFeatureGeometryProvider.GetGeometry` (renderer skips features it can't place); `CoverageColorScheme.Resolve(float)` (a value→colour map where "no band" is routine) |
| **Absence while projecting untrusted data**, where the pipeline must continue *and* the problem must be reported | return nullable **and** emit a diagnostic; never throw | `XlinkResolver.Resolve<T>` (unresolved `xlink:href` → `null` + `xlink.unresolved` warning) |
| **Contract violation / packaging defect** with no sensible recovery | throw a **specific** domain exception | portrayal-catalogue asset/rule lookups (`IPortrayalAssetSource.Get*Async`, `IXsltRuleSource.GetCompiledRuleAsync`) throw `PortrayalAssetNotFoundException` |

Rules of thumb:

- **Do not use `KeyNotFoundException`** (or other framework "not found"
  exceptions) for a domain miss. A caller guarding an ordinary dictionary
  lookup should not accidentally swallow a portrayal-packaging defect. Define a
  domain exception deriving from `Exception` (the recommended base for custom
  exceptions) carrying the useful context — e.g. `PortrayalAssetNotFoundException`
  exposes `AssetKind` and `AssetName`.
- **Throw only when the caller is not expected to guard.** These lookups have no
  cheap pre-check (`Contains`) on their interface, so a miss is genuinely
  exceptional. When a caller *does* need to tolerate absence (e.g. the render
  pre-warm treats any lookup miss as "not in catalogue"), it catches the domain
  exception explicitly.
- **Nullable + diagnostic** is the right shape whenever the input is
  externally-authored data and swallowing-with-a-report beats aborting the
  whole projection.

## 4. Construction and factory methods

Pick a construction entry point by intent, and name it so the caller knows
whether it can fail:

| Form | Use for | Examples |
|---|---|---|
| Public constructor / `required init` object initializer / positional record | plain value or model data whose construction cannot fail | `BoundingBox`, `Viewport`, most records |
| `static From<Unit>(…)` | a **unit or value conversion** into the type | `Length.FromMetres`, `Depth.FromFathoms`, `Angle.FromDegrees`, `RgbaColor.FromHex` |
| `static From<Source>(…)` | **adapting/parsing** another representation into the type | `S101Dataset.FromDocument`, `S100FeatureCatalogue.FromStream`, `GridRegion.FromViewport` |
| `static Create(…)` | **infallible** construction of a service/wrapper (often composing dependencies) | `McpServerTool.Create`, `FileSystemAssetSource.Create`, `S100PipelineHost.Create` |
| `static TryCreate(…)` returning `T?` | construction that **can legitimately fail** and returns `null` instead of throwing | `SpecVersionAssessment.TryCreate`, `TimeAwareDatasetFactory.TryCreate`, `BasemapLayerFactory.TryCreate` |
| `static Default` / `static Empty` | the canonical or empty singleton of an immutable type | `ColorPalette.Default`, `ValidationRuleSet<T>.Default`, `ValidationReport.Empty` |

Rules:

- **`Create` must not return `null`.** If a factory can fail or yield nothing,
  name it `TryCreate` and return `T?` (this repo uses the nullable-return
  `TryCreate` shape rather than a `bool TryCreate(out T)` for reference types).
  `From<X>` methods *may* return `null` when the source genuinely carries no
  value (e.g. `ChromeTheme.FromVariant(null)`), but document that case on the
  method.
- **`Default` vs `Empty`:** `Default` is "the standard instance"; `Empty` is
  "the zero-element instance." Don't use one to mean the other.
- Prefer **one** construction style per shape and stay with it; do not offer a
  constructor *and* a `Create` that do the same thing.
- Keep the .NET `From*` / `Default` / `Empty` vocabulary — do not invent terser
  factory names.

## 5. `CancellationToken` on synchronous methods

A **synchronous** method (non-`Task`/`ValueTask` return) may accept a
`CancellationToken` when, and only when, it performs **bounded CPU-bound work
that it observes the token within** — typically a loop that calls
`cancellationToken.ThrowIfCancellationRequested()`. This is the idiomatic .NET
way to make a long in-memory computation abandonable; it is *not* a mistake and
must not be "corrected" by wrapping the method in `Task`/`ValueTask`.

Rules:

- **Do not** add a `CancellationToken` to a synchronous method that never
  observes it — an ignored token is misleading; drop the parameter instead.
- **Do not** change a synchronous CPU method to return `Task`/`ValueTask` just
  to carry a token. Async return types signal *I/O or offloaded* work; these
  methods are neither.
- **Do** state in the XML docs that the method is synchronous and CPU-bound and
  that the token allows cooperative cancellation, so callers are not surprised
  by a non-`Async` method taking a token.

Sanctioned examples: `ICoverageSource.Sample` (a resident-grid copy that checks
the token per cell) and `IFeatureXmlSource.GetFeatureXml` (projects in-memory
features to FeatureXML). Both are already documented to this standard.

## 6. `ValueTask` vs `Task`

Both appear deliberately; pick by call-shape:

- **`Task` / `Task<T>`** for a **one-shot genuinely-asynchronous** operation —
  opening, reading, or parsing a dataset; disk/network I/O that runs once per
  call. This is the default; reach for `Task` unless you have the specific
  reason below.
- **`ValueTask` / `ValueTask<T>`** for a **hot-path method that frequently
  completes synchronously** — a memoized/cached lookup where allocating a `Task`
  on every (usually cache-hit) call would be wasteful. In this codebase that is
  the portrayal asset/rule/Lua-source resolution surface
  (`IPortrayalAssetSource.Get*Async`, `IXsltRuleSource.GetCompiledRuleAsync`,
  `ILuaRuleSource.Get*Async`): the first access does I/O, subsequent accesses
  return through the synchronous `ValueTask` fast path.
- Also use `ValueTask` where the framework prescribes it —
  `IAsyncDisposable.DisposeAsync`, `Stream.ReadAsync(Memory<byte>, …)`.

**Consuming a `ValueTask` safely** (these constraints are the cost of the
allocation saving):

- `await` it **exactly once**, directly. Do not `await` the same `ValueTask`
  twice, store it in a field, or hand it to two consumers.
- Do not block on it (`.Result` / `.GetAwaiter().GetResult()`) unless
  `IsCompletedSuccessfully` is already true.
- If you need to await multiple times, combine (`Task.WhenAll`), or otherwise
  hold the pending operation, convert once with `.AsTask()`.

If none of the `ValueTask` reasons apply, use `Task` — it is more forgiving for
callers.

## 7. Migration recipe (Immutable → read-only)

When converting a file:

| From | To |
|---|---|
| `ImmutableArray<T>` (surface type) | `IReadOnlyList<T>` |
| `ImmutableArray<ImmutableArray<T>>` | `IReadOnlyList<IReadOnlyList<T>>` |
| `ImmutableDictionary<K, V>` (surface type) | `IReadOnlyDictionary<K, V>` |
| `= ImmutableArray<T>.Empty` (default) | `= []` |
| `= ImmutableDictionary<K, V>.Empty` (default) | `= ReadOnlyDictionary<K, V>.Empty` |
| `ImmutableArray<T>.Empty` (value) | `[]` or `Array.Empty<T>()` |
| `xs.ToImmutableArray()` | `xs.ToArray()` |
| `xs.ToImmutableDictionary(...)` | `xs.ToDictionary(...)` |
| `arr.IsDefaultOrEmpty` / `arr.IsEmpty` | `arr.Count == 0` |
| `arr.IsDefault` | (drop; field defaults to `[]` and is non-null) |

Then remove now-unused `using System.Collections.Immutable;` /
`using System.Collections.ObjectModel;` as appropriate, and rebuild.

`ReadOnlyDictionary<K, V>.Empty` lives in
`System.Collections.ObjectModel` (available since .NET 8).

## 8. Geographic coordinates: one canonical type

A single type represents a geographic position everywhere in the codebase:

```csharp
public readonly record struct GeoPosition(double Latitude, double Longitude);
```

`GeoPosition` lives in `EncDotNet.S100.Core` (namespace
`EncDotNet.S100.DataModel`). Latitude and longitude are decimal degrees on
WGS-84 (EPSG:4326). It is a `readonly record struct`, so it has value
equality, deconstructs to `(Latitude, Longitude)`, and costs no heap
allocation — which is why it can replace bare tuples without a performance
penalty.

**Rule: do not use a raw `(double, double)` / `(double Latitude, double
Longitude)` / `(double Lat, double Lon)` tuple to carry a lat/lon pair.**
Use `GeoPosition` on every model, source, pipeline, renderer, tool, and test
surface. This keeps latitude/longitude ordering unambiguous (a positional
tuple silently tolerates a swap; a named record does not) and gives one type
to search for, document, and validate.

### Deliberate, documented exceptions

These are **not** lat/lon coordinate pairs and stay as tuples (or their own
named types) on purpose:

- **Projected / screen space** — `(double X, double Y)` mercator-metre or
  pixel pairs (e.g. `VectorSceneBuilder.Project`, `WorldToScreen`,
  `MercatorOffset.ToMercator`). These are not geographic positions.
- **`(double Longitude, double Latitude)` projection bridges** — helpers that
  mirror a third-party lon/lat contract (`WebMercator.ToLonLat`,
  `CompositeViewportBuilder.ToLonLat`, feeding `Mapsui.SphericalMercator` /
  NTS `Coordinate(x, y)`). The reversed order is dictated by the external API;
  keeping the tuple makes the mismatch explicit at the boundary.
- **ISO 8211 raw grid coordinates** — `(int Y, int X)` intermediate
  coordinates in `S101Document` are record-format integers, not degrees.
- **Composite payloads** — tuples that carry more than a position, e.g.
  soundings `(double Lat, double Lon, double Depth)` in test fakes, or the
  dash-pattern `(double Offset, double Length)`.
- **Two-parameter method signatures** — `Expand(double lat, double lon)`,
  `IsValidLatLon(double lat, double lon)`, `Click(double lat, double lon)`,
  xUnit `[Theory]` parameters, etc. are ordinary parameter lists, not tuple
  types, and are left as-is.

### Two other coordinate-bearing types are kept intentionally

- `GeoPoint` (`EncDotNet.S100.Mcp.Tools`) — a JSON-bound record used at the
  MCP wire boundary. It is a serialization DTO, deliberately decoupled from
  the core model so the wire contract can evolve independently.
- `PickLocation` (`EncDotNet.S100.Viewer`) — an internal viewer view-model
  value. Kept internal and separate from the model layer.

Both convert to/from `GeoPosition` at their boundary rather than propagating a
second coordinate type inward.

## 9. Explicit enum values only where the number is a contract

An enum's underlying numeric value matters only when something outside the
type observes it. Assign **explicit** values when — and only when — the
number is a contract:

- **Persisted / cached numerically.** The disk-backed portrayal cache
  (`DrawingInstructionSerializer` behind `DiskPortrayalInstructionCache`)
  writes some enums by ordinal, so `DisplayPlane`, `TextHorizontalAlignment`,
  `TextVerticalAlignment`, and `LinePlacementMode` carry explicit values.
  Their XML docs say so, a `PersistedEnumValues_AreStable` test pins them, and
  any deliberate change must bump `DrawingInstructionSerializer.FormatVersion`.
- **Serialized to a wire / file format** as a number, or mapped to an external
  code list (e.g. S-100 Part 10b geometric-primitive codes on
  `S100GeometryType`, the S-98 draw-order codes on `S98DisplayPlane`).

For a purely in-memory enum whose value is never observed as a number, leave
the ordinals implicit — adding `= 0, = 1, …` that merely restate the default
is noise. `DiagnosticSeverity` and `ValidationSeverity` are compared only by
equality (`== Error`), never by ordinal, and are never persisted, so they stay
implicit.

When an enum crosses a **JSON** boundary (MCP tools, settings files), prefer
serializing it *as a string* via `JsonStringEnumConverter` rather than relying
on the numeric value at all — that removes the ordering hazard entirely and
keeps the wire self-describing.

## 10. Disposal: pair `IDisposable` with `IAsyncDisposable`

Resource-owning abstractions that already expose an async I/O surface should
implement **both** `IDisposable` and `IAsyncDisposable`, so callers can write
`await using` without giving up the synchronous path.

`IAssetSource` is the reference example. It implements both, and provides a
**default interface method** for `DisposeAsync` that forwards to `Dispose()`:

```csharp
public interface IAssetSource : IDisposable, IAsyncDisposable
{
    Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default);

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
```

Guidance for implementations:

- **Leaf sources** whose resources dispose synchronously (a `ZipArchive`, a
  directory root, embedded resources) need **no** `DisposeAsync` — the default
  is correct. `ZipArchive` isn't `IAsyncDisposable`, so there is no genuine
  async path to add.
- **Owning wrappers** that hold another `IAssetSource` (or a truly
  async-disposable resource) should **override** `DisposeAsync` to propagate it
  without blocking — e.g. `CachingAssetSource.DisposeAsync() => _inner.DisposeAsync()`.
  When ownership is conditional, honour the flag:
  `DecryptingAssetSource.DisposeAsync() => _ownsInner ? _inner.DisposeAsync() : ValueTask.CompletedTask`.
- **Non-owning decorators** (e.g. `NonDisposingAssetSource`) keep their no-op
  `Dispose`; the default `DisposeAsync` then correctly does nothing to the
  borrowed inner source.

The default interface method is only reachable through the interface (or an
`await using` over an interface-typed variable). A concrete wrapper that expects
to be `await using`-d directly should therefore also declare a public
`DisposeAsync`, which the owning wrappers above already do.
