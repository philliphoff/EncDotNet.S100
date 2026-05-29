# Own-ship vessel symbology — Design Note

> Status: **Design accepted** — see plan in
> `philliphoff/own-ship-vessel-symbology`. Implementation lands in
> the same PR as this note.

## 0. Scope

Upgrade the own-ship overlay from the generic
`DefaultDynamicFeatureRenderer` (disc + bare heading line) to a
purpose-built renderer that:

1. Draws a **true-scale hull outline** when the viewport is zoomed in
   far enough for the vessel to be distinguishable.
2. Falls back to a **disc pictogram** when zoomed out.
3. Decorates the heading vector with an **arrowhead** in both modes.
4. Honours user-configured vessel dimensions and the IEC 62388 CCRP
   offsets (GPS antenna position relative to bow and port side).

Out of scope: AIS / PR-D3; sleeping-or-lost styling; vessel-type
palettes (deferred to `KindMatchingRenderer`); COG-vs-HDG dual-vector
split; IEC 62388 conformance audit (this is an indicator, not
certified ECDIS).

This note also pre-stages the data model for PR-D3: the vessel
geometry sidecar introduced here is shaped so that an AIS adapter
can populate it directly from AIS Type 5 messages.

---

## 1. Q1 — Where do vessel dimensions live?

**Decision: a new `DynamicVesselGeometry` sidecar record on
`DynamicFeature`** (option (b) of the task brief). The renderer reads
dims from the feature, not from DI.

```csharp
namespace EncDotNet.S100.DynamicSources;

public sealed record DynamicVesselGeometry
{
    public required double LengthMetres { get; init; }   // bow to stern
    public required double BeamMetres   { get; init; }   // port to starboard
    public required double BowOffsetMetres  { get; init; } // GPS antenna distance aft of bow
    public required double PortOffsetMetres { get; init; } // GPS antenna distance starboard of port side
}

public sealed record DynamicFeature
{
    // ... existing fields ...
    public DynamicVesselGeometry? VesselGeometry { get; init; }
}
```

### Rationale

| Concern | Sidecar (b) | Renderer-config-only (a) |
|---|---|---|
| Own-ship today | one provider feeds one feature | works |
| AIS tomorrow | per-target dims published with each fix | needs a parallel side-channel |
| Renderer purity | renderer reads everything from the feature | renderer must be injected with a registry keyed by id |
| Core surface area | one additive nullable record | none |

The sidecar costs one nullable record in
`EncDotNet.S100.Core` and removes a future DI plumbing problem when
PR-D3 lands. It mirrors `DynamicMotion`'s sidecar shape exactly so
the surface stays predictable.

### AIS Type 5 mapping (forward-look only)

| AIS Type 5 field | Sidecar field | Semantics |
|---|---|---|
| `dimA` (bow → reference) | `BowOffsetMetres` | distance aft of bow |
| `dimB` (reference → stern) | `LengthMetres - BowOffsetMetres` | |
| `dimC` (port → reference) | `PortOffsetMetres` | distance starboard of port side |
| `dimD` (reference → starboard) | `BeamMetres - PortOffsetMetres` | |

So `LengthMetres = dimA + dimB` and `BeamMetres = dimC + dimD`; the
sidecar normalises the AIS payload to the smaller (length, beam,
fore-aft offset, athwartships offset) tuple that a renderer actually
needs. PR-D3 owns the conversion.

### Validation

Validation is **documented but not enforced at the record level**:
`init`-only doubles with no constructor checks. The settings UI is
the single guard against bad input (`> 0`,
`BowOffset ≤ Length`, `PortOffset ≤ Beam`). A renderer that receives
nonsensical dims still produces output — clamped to a degenerate
quad — and a debug-log warning. This matches how `DynamicMotion`
tolerates partial / out-of-range data.

---

## 2. Q2 — Pixel-threshold switch between outline and pictogram

**Decision: emit both shapes, with mutually-exclusive style-level
resolution gates** (option (a) of the task brief). No
`IDynamicFeatureRenderer` signature change; Mapsui filters per frame.

### Threshold

```
MinVesselPixels = 22   // ≈ 6 mm at 96 dpi — matches ECDIS practice
```

Pinned as `OwnShipRenderer.MinVesselPixels`.

### Crossover resolution

Mapsui's "resolution" is metres-per-pixel at the equator in Web
Mercator. At latitude φ the on-screen pixel size of a real-world
metre is `1 / (resolution * cos φ)`. We want the outline visible
when `LengthMetres * cos φ / resolution ≥ MinVesselPixels`, i.e.

```
R_switch = LengthMetres * cos(latitude) / MinVesselPixels
```

So the outline gets `MaxVisible = +∞`, `MinVisible = 0` filtered
against `R_switch` such that the outline shows when
`currentResolution ≤ R_switch`; the pictogram is the inverse.
Concretely:

```csharp
var r = LengthMetres * Math.Cos(lat * Math.PI / 180.0) / MinVesselPixels;
outlineStyle.MaxVisible  = r;   // outline visible when resolution <= r
pictogramStyle.MinVisible = r;  // pictogram visible when resolution >= r
```

The exact-equal edge produces both (one-pixel flicker) but that
window is a single zoom step in practice. We accept it for v1.

### Why not extend `IDynamicFeatureRenderer` with viewport?

Touching the interface forks every existing implementor
(`DefaultDynamicFeatureRenderer`, `KindMatchingRenderer`,
`CompositeDynamicFeatureRenderer`) and the overlay host that drives
them. Style-level gates are a built-in Mapsui mechanism; we'd be
re-implementing what's already there.

---

## 3. Q3 — Outline shape

**Decision: 5-vertex hull polygon in vessel-local metres**, with a
bow-taper constant of `0.7`.

Local frame (x = starboard, y = forward):

```
       bow tip (0, +Length * (1 - taper) +  taper*Length/?)   <- single vertex at +Length, 0
       /\
      /  \
   (-B/2, +Length*taper)  ----  (+B/2, +Length*taper)
      |                                |
      |                                |
   (-B/2, 0)              ----    (+B/2, 0)   <- stern
```

Concretely, with `L = LengthMetres`, `B = BeamMetres`, `t = 0.7`:

```
v0 = (   0 , +L          )   bow tip
v1 = (+B/2 , +L*t        )   starboard shoulder
v2 = (+B/2 ,    0        )   starboard stern corner
v3 = (-B/2 ,    0        )   port stern corner
v4 = (-B/2 , +L*t        )   port shoulder
```

Rotation by heading θ (degrees true, clockwise from north) is the
standard rotation `(x', y') = (x cos θ + y sin θ, -x sin θ + y cos θ)`.
After rotation each vertex is offset from the GPS antenna position
by `(–PortOffsetMetres + B/2, –(–BowOffsetMetres + ...))` — see
§3.1 for the algebra.

### 3.1 Georeferencing the hull

The GPS antenna sits at vessel-local `(−B/2 + PortOffsetMetres,
+L − BowOffsetMetres)`. The published lat/lon is the antenna
position. So each hull vertex `v` is translated into world frame as
`v − antenna_local`, then rotated by heading, then projected through
the small-angle metres→degrees helper (see §3.3).

The helper is good to ~1 m for vessel-scale offsets at any
non-polar latitude; we are not designing for tankers near 89° N.

### 3.2 Heading fallback

If `HeadingDeg` is `null`, use `CourseOverGroundDeg`. If both are
null (zero-speed startup), draw the hull aligned to north — better
than not drawing it at all.

### 3.3 Local-metres → world helper

Extract a `MercatorOffset` static helper in
`EncDotNet.S100.Renderers.Mapsui.DynamicSources`:

```csharp
internal static (double Lat, double Lon) FromLocalMetres(
    double refLat, double refLon, double eastMetres, double northMetres);
```

Implementation: small-angle approximation —
`Δlat = northMetres / 111_320`,
`Δlon = eastMetres / (111_320 * cos refLat)`.
Adequate for vessel-scale offsets; not used elsewhere as a general
geodesy primitive.

### 3.4 Colours

Single palette for v1 — reuse the existing `DefaultStroke` /
`DefaultFill` blue. S-52 vessel-shape colours (four-shade per
display mode) are out of scope; we'll thread the colour from the
active palette in a future PR if it reads poorly under dusk/night.

---

## 4. Q4 — Arrowhead on heading vector

**Decision: filled triangle arrowhead via `SymbolStyle`, fixed pixel
size**.

The heading vector remains a `LineString` (start at antenna, end at
the 6-minute-predictor point); the arrowhead is a separate point
feature at the line's end with a triangular `SymbolStyle` rotated to
the heading. Sizing constant:

```
HeadingArrowPx = 10
```

Pixel-space styling means the arrowhead looks correct at all zoom
levels — a world-metres-sized arrowhead would shrink to invisible
when zoomed out and overshoot the predictor end when zoomed in.

If Mapsui's `SymbolType.Triangle` rotation isn't tunable enough to
align cleanly with the heading, we fall back to a 2-segment chevron
`LineString` — same `LineString` cost, same visual reading. Decided
at implementation time.

---

## 5. Q5 — CCRP cross

**Decision: small `+` symbol at the antenna position, emitted only
when the outline is shown** (same `MinVisible`/`MaxVisible` gate as
the hull).

The CCRP indicator is informational — drawing it at the disc-only
zoom would be both visually noisy and not meaningfully informative
(the disc already centres on the antenna). Gating it to outline-mode
keeps it useful where it matters.

Implementation: a `SymbolStyle` cross (two short crossed line
segments rendered via a small `Polygon` or a custom symbol) ~6 px
across. Constant `CcrpCrossPx = 6`.

---

## 6. Q6 — Settings persistence + UI

**Decision: new `OwnShipSettings` sub-object on `ViewerSettings`.**

```csharp
public sealed class OwnShipSettings
{
    public double LengthMetres      { get; set; } = 50;
    public double BeamMetres        { get; set; } = 10;
    public double BowOffsetMetres   { get; set; } = 25;   // amidships
    public double PortOffsetMetres  { get; set; } =  5;   // centreline
}

public sealed class ViewerSettings
{
    // ... existing ...
    public OwnShipSettings? OwnShip { get; set; }   // null = use defaults
}
```

### Migration

Pure additive — no legacy field to rescue. `ViewerSettings.Load`
leaves `OwnShip` `null` when the JSON has no entry; the consuming
provider materialises a `new OwnShipSettings()` when needed. Saving
writes whatever is currently on the POCO.

The reason for nullable-with-defaulting rather than always-present-
with-defaults: it mirrors how the mariner-settings block already
handles the same scenario (nullable fields, defaults applied at the
provider). Keeps the JSON minimal for users who never touched the
panel.

### UI

A new **Own Vessel** section in the Settings panel. Four
`NumericUpDown` inputs in metres:

- Length (≥ 1)
- Beam (≥ 1)
- Bow offset (0 ≤ x ≤ Length)
- Port offset (0 ≤ x ≤ Beam)

Validation: invalid input is clamped on commit; tooltip explains
the constraint. All labels and tooltips through `Resources/Strings.resx`
per `viewer.instructions.md`. Writes go through the existing
debounced settings saver.

### Settings → source repropagation

```
SettingsView edits
  ⇒ ViewerSettings mutated + debounced save
  ⇒ IOwnShipVesselGeometryProvider raises Changed
  ⇒ OwnShipSource re-projects its current fix and raises
    DynamicSourceChangeKind.Updated
  ⇒ overlay host re-renders
```

The provider is the abstraction edge — `OwnShipSource` doesn't
know about `ViewerSettings`. PR-D3 reuses the
`IOwnShipVesselGeometryProvider` shape (renamed for AIS) by keying
on MMSI rather than the singleton.

---

## 7. Q7 — Renderer registration

**Decision: `OwnShipRenderer` registered under
`RendererKey = "ownship"`** via the existing
`AddDynamicFeatureRenderer<TRenderer>(string)` helper.
`OwnShipSource.Metadata.RendererKey` set to `"ownship"`. The XML-doc
remark in `OwnShipSource` that defers the renderer "until a second
source coexists" is removed.

### Pictogram-only fallback

`OwnShipRenderer` always emits the pictogram + heading + arrowhead.
The outline branch is taken only when `feature.VesselGeometry is
not null`. This means an AIS adapter that publishes targets with
unknown dimensions (Class B without dimensions, fresh Type 1 before
the first Type 5) still gets a sensible pictogram from the same
renderer once we re-key the AIS source to `"ownship"` or to a
shared `"vessel"` key in PR-D3.

---

## 8. Test surface

| Test project | Cases |
|---|---|
| `EncDotNet.S100.Core.Tests` | `DynamicVesselGeometry` record equality; `DynamicFeature` `with` expressions preserving the sidecar. |
| `EncDotNet.S100.Renderers.Mapsui.Tests` (or pipelines tests) | Hull vertex placement: heading 0°/90°/180°, antenna at bow vs amidships, assert vertex lat/lon to 5 decimals. Resolution-gate values: outline `MaxVisible` and pictogram `MinVisible` agree on the computed `R_switch`. Pictogram-only fallback when `VesselGeometry == null`. Arrowhead omitted when no heading. CCRP cross emitted iff outline. |
| `EncDotNet.S100.Viewer.Tests` | `OwnShipSettings` JSON round-trip + defaulting when absent. Settings → provider → source: assert source raises `Updated` on geometry mutation. |

---

## 9. Open items (not blocking)

- Bow-taper ratio user-settable? **Default: no.** Hardcoded `0.7`.
- Double-stroke the outline at very small effective widths? **Default:
  no.** Revisit if it reads poorly on dusk/night palettes.
- `SymbolType.Triangle` rotation precision — decide at impl time
  whether to keep it or fall back to a chevron `LineString`.
- Palette adaptation to dusk/night (S-52 colour tokens) — future.
