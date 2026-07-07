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

## 3. Migration recipe (Immutable → read-only)

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
