# Quantity types — Design Note

> Status: **Adopted (incremental rollout)**. The `Length`/`Depth`
> types with the `MarinerSettings` migration, and the `Angle`/`Speed`
> types with the `DynamicMotion` migration, are all in `main`. This
> note is the contract for how strongly typed physical quantities are
> modelled across the S-100 libraries.

## 1. Motivation

Before quantity types, physical values crossed the public API as bare
`double`s whose unit lived only in a name suffix or an XML comment
(`SafetyContour` "in metres", `SpeedOverGroundKn`, `HeadingDeg`,
`distanceMetres`). This produced three recurring problems:

1. **Unit drift** — the same concept was spelled inconsistently
   (`Kn` vs. `Knots`, `Deg` vs. `Degrees`, `Mm` vs. `mm`), and some
   members carried a suffix while sibling members did not.
2. **Silent unit errors** — nothing stopped a caller passing feet
   where metres were expected, or metres-per-second where knots were
   expected (own-ship reports in m/s, AIS in knots).
3. **Conversion sprawl** — ad-hoc `* 3.28084` / `/ 1.8288` literals
   appeared at each call site, each free to pick its own rounding.

Strongly typed quantities move the unit into the type system: the unit
is chosen explicitly at construction (`Depth.FromMetres(30)`) and read
explicitly (`depth.TotalFeet`), never implied.

## 2. The pattern

Every quantity type is a lightweight value type modelled on
`System.TimeSpan`:

- A `public readonly record struct` implementing
  `IComparable<TSelf>`.
- A **single canonical backing unit** stored in one `private readonly
  double` field (metres for `Length`/`Depth`, metres-per-second for
  `Speed`, degrees for `Angle`).
- A **private constructor**; instances are only created through
  `From<Unit>` factory methods.
- `From<Unit>` factories and `Total<Unit>` reader properties for every
  supported unit.
- Arithmetic operators (`+`, `-`, unary `-`, `*`/`/` by a
  dimensionless `double`) and full ordering (`<`, `>`, `<=`, `>=`,
  `CompareTo`).
- `static TSelf Zero => default;` — `default(TSelf)` is always the
  zero quantity.
- A diagnostic `ToString()` in invariant culture. **User-facing**
  presentation goes through a dedicated formatter (e.g.
  `DepthFormatting`), never `ToString()`.

### 2.1 Naming conventions (normative)

- **Factories**: `From<Unit>` (`FromMetres`, `FromFeet`, `FromKnots`,
  `FromDegrees`). This mirrors `TimeSpan.FromSeconds` and the wider
  .NET convention. **Do not** add terser aliases (`Depth.Metres(30)`);
  the `From<Unit>` form is the one true spelling.
- **Readers**: `Total<Unit>` (`TotalMetres`, `TotalFeet`,
  `TotalKnots`, `TotalDegrees`). The `Total` prefix is kept across the
  whole family — including angles and speeds that have no
  component/total distinction — purely for cross-type consistency, so
  the reader shape is predictable regardless of quantity.
- **No implicit conversion from `double`.** Allowing `Depth d = 30.0;`
  would reintroduce exactly the silent-unit problem the types exist to
  remove. The verbosity at construction sites (notably in tests) is an
  accepted, deliberate cost.

### 2.2 Conversion factors

Conversions use exact SI-derived factors, defined once as `public
const`s on the type and reused everywhere:

| Factor | Value | Basis |
|---|---|---|
| `Length.MetresPerFoot` | `0.3048` | exact international foot |
| `Length.MetresPerFathom` | `1.8288` | 6 international feet |
| `Length.MetresPerNauticalMile` | `1852.0` | exact |
| `Speed` knot | `1852.0 / 3600.0` m/s | nautical mile per hour |
| `Angle` radian | `π / 180` | — |

Legacy helpers that exposed rounded factors (e.g.
`DepthFormatting.FeetPerMetre`) now derive from these exact constants;
their public values changed only below display precision.

## 3. Boundary policy — where quantity types apply

Quantity types are for **scalar real-world physical quantities that
appear on model/API surfaces**. They are deliberately *not* pushed
into every `double`.

**Use a quantity type for:**

- Mariner/setting values (`MarinerSettings` depths).
- Motion/geometry model fields (`DynamicMotion` course, heading,
  speed).
- Scalar inputs/outputs of geodesy and navigation calculations where
  the signature is not dominated by coordinates.

**Keep plain `double` (or `float`) for:**

- **Display-surface millimetres** in `DrawingInstruction` and
  portrayal space — these are device/paper measurements, not physical
  world lengths.
- **Bulk coverage arrays** (S-102/104/111 `float[]`/`double[]` grids)
  — per-cell wrapping would be a real allocation/throughput cost.
- **Interop seams** — Lua context parameters, HDF5 datasets, GML text,
  and raw wire payloads (e.g. AIS JSON fields). Convert at the seam:
  unwrap to the canonical unit (`.TotalMetres`) when handing values
  *out*, and wrap into a quantity (`Angle.FromDegrees(...)`) when
  decoding a value *in* to one of our model/message records. The raw
  numeric buffer stays `double`; the decoded record carries the
  quantity type.
- **Latitude/longitude** — angular coordinates are handled as a pair
  by a future `GeoPosition` effort, not as two `Angle`s, and
  low-level geodesy helpers (`GeodesicHelper`) keep `double` lat/lon
  so a signature is never half-typed.

## 4. Equality

Because the backing store is `double`, record-struct `==` is **exact
floating-point equality** and is subject to the usual rounding
caveats. For computed values, compare with `CompareTo` and an explicit
tolerance rather than `==`.

## 5. Type catalogue

| Type | Canonical unit | Notes |
|---|---|---|
| `Length` | metres | General linear distance. |
| `Depth` | metres | Specialization of `Length` (composition + implicit `Length` conversion). Sign preserved (drying heights). Core hydrographic concept, hence its own type. |
| `Angle` | degrees | Marine bearings (clockwise from true north). Offers `Normalized()` to `[0, 360)`. |
| `Speed` | metres/second | `FromKnots` / `FromMetresPerSecond` / `FromKilometresPerHour`. Unifies AIS (knots) and own-ship (m/s) feeds. |

All live in namespace `EncDotNet.S100.Quantities`
(`src/EncDotNet.S100.Core/Quantities/`).

## 6. Migration playbook

1. Change the model field type (`double` → quantity) and its default
   (`= Depth.FromMetres(30.0)`).
2. At every **read** that feeds an interop seam or numeric algorithm,
   append the canonical reader (`.TotalMetres`, `.TotalDegrees`,
   `.TotalKnots`).
3. At every **construction** site that has a raw `double`, wrap it
   (`Depth.FromMetres(x)`, `Angle.FromDegrees(x)`).
4. Leave display/persistence layers (view-model bindable `double`
   properties, JSON DTOs) as `double`; convert only at the point they
   build/read the model type.

### Example — `MarinerSettings`

```csharp
// before
public double SafetyContour { get; init; } = 30.0;
new("SafetyContour", m => m.SafetyContour, LuaValueSerializers.Number)

// after
public Depth SafetyContour { get; init; } = Depth.FromMetres(30.0);
new("SafetyContour", m => m.SafetyContour.TotalMetres, LuaValueSerializers.Number)
```

## 7. Rollout status

| Area | Type(s) | Status |
|---|---|---|
| `Length`, `Depth` | — | Done |
| `DepthFormatting`, `ContourStyle` | `Depth` | Done |
| `MarinerSettings` + consumers | `Depth` | Done |
| `Angle`, `Speed` | — | Done (this note) |
| `DynamicMotion` + consumers | `Angle`, `Speed` | Done |
| `OwnShipPosition` + consumers | `Angle`, `Speed` | Done |
| `AisPositionReport` + driver | `Angle`, `Speed` | Done (ROT kept `double` — no `AngularRate` type) |
| `GeodesicHelper` | `Length`, `Angle` | Not planned (coordinate-dominated signatures kept `double`) |
