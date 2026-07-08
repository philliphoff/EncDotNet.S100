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

## 4. Migration recipe (Immutable → read-only)

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
