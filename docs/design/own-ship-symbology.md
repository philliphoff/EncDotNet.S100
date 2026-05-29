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

## 1. Standards survey

The decisions in §2 onward (Q1–Q7) are grounded in four published
standards. Each is cited inline below; the design either adopts the
standard's convention verbatim or notes a deviation with a one-line
justification. This survey was added retroactively (see Roadmap-
review addendum on PR #136) — it is the contractual reference for
Q3 (outline), Q4 (heading vector), and Q5 (CCRP cross).

### 1.1 IHO S-52 Annex A (Presentation Library)

**Edition 4.0 / S-52 Ed 6.1.** Free IHO publication;
[iho.int Standards & Specifications](https://iho.int/en/standards-and-specifications).

- **§7.4.5 + §13.2.7 — minimum on-screen own-ship dimension:**
  the scaled own-ship outline is displayed only when its
  on-screen length is at least **6 mm**. Below that, the simple
  symbol (double-circle / pictogram) is shown instead.
  → **Adopted verbatim.** `MinVesselPixels = 22` is 6 mm × 96 dpi
  ÷ 25.4 = 22.68 px, rounded down. See Q2.

- **§8.3 + symbol library — SY(OWNSHP01) "simple" symbol:**
  pictogram (double-circle for S-52; we use a single coloured disc
  as a v1 simplification — the second ring carries no information
  that the disc colour doesn't already convey at our scale).
  → **Adopted with minor deviation:** single disc instead of
  double-circle. Visual fidelity for a future "second AIS target
  nearby" case can re-introduce the second ring.

- **§8.3 + symbol library — SY(OWNSHP02) "scaled" symbol:**
  polygon at true vessel dimensions, rotated by heading, origin
  at the CCRP. The S-52 vertex coordinates are IHO-copyrighted
  and not reproduced here, but the canonical form is a small
  vertex-count polygon (≈5 points: stern-port, stern-starboard,
  starboard-shoulder, bow-tip, port-shoulder) parameterised by
  Length + Beam with a bow taper.
  → **Adopted:** our 5-vertex hull with `BowTaperRatio = 0.7`
  matches this structural form. See Q3. The user's kickoff hint
  about a "canonical 7-point hull" is not supported by S-52 — the
  spec is silhouette-shaped, not literal-hull-shaped.

- **§8.3.1 — CCRP cross:** small `+` mark drawn inside the
  scaled outline at the CCRP / GPS-antenna position, visible only
  when the scaled outline is shown.
  → **Adopted:** Q5 specifies a cross. v1 implementation initially
  used a `SymbolType.Rectangle` placeholder; this addendum replaces
  it with two crossed `LineString` segments to draw a real `+`.

- **Heading line vs course vector vs speed vector:**
  S-52 distinguishes three vectors with different styling:
  - **Heading line** — solid line in the direction the bow
    is pointing. **No arrowhead, no tick marks.** Represents
    *orientation*, not motion.
  - **Course-over-ground (COG) vector** — line in the direction
    of motion. **Arrowhead at the tip.**
  - **Speed-over-ground vector** — same direction as COG.
    **Arrowhead at the tip plus tick marks at minute intervals**
    (e.g. 1-min ticks along a 6-min predictor).

  Our v1 source synthesises a single line from `CourseOverGroundDeg`
  (mirrored to `HeadingDeg`); the renderer draws it as
  **arrowhead-only** — i.e. a COG vector, not a heading line and
  not a tick-marked speed vector.
  → **Adopted with documented conflation:** the single line is
  styled per S-52's COG-vector convention. A true heading line
  (no arrowhead) and a true tick-marked speed vector are out of
  scope for v1 (the OP brief explicitly defers HDG-vs-COG
  splitting). The renderer XML doc and Q4 call this out so a
  future PR that lands a real gyro-heading source knows which
  vector to add.

### 1.2 IEC 62388 — Shipborne radar performance

**IEC 62388:2022.** Paywalled IEC standard; the relevant CCRP
material is summarised in public OpenBridge / OpenECDIS
references and in IEC's freely-available abstract.

- **Consistent Common Reference Point (CCRP):** a fixed point on
  the vessel used by all bridge systems (radar, ECDIS, ARPA, AIS)
  as a unified positional reference. The radar antenna's
  `bow_offset` (distance aft of bow along the longitudinal axis)
  and `port_offset` (distance starboard of the port side along the
  lateral axis) are configured per installation so all derived
  positions are reported relative to the CCRP.
  → **Adopted:** `DynamicVesselGeometry.BowOffsetMetres` /
  `PortOffsetMetres` field names track IEC 62388. The CCRP is
  conventionally near the conning position; we let the user
  configure it freely (defaults to amidships).

### 1.3 IEC 61174 — ECDIS performance standard

**IEC 61174:2015 (Ed 4.0).** Paywalled; public summaries via IMO
MSC.232(82) §4.7 / §4.8 and OpenECDIS.

- **Clause 6.1.3 — own ship display:** the vessel symbol and its
  heading indication must always be clearly visible on the display
  during navigation; vessels >50 m must use a scaled outline
  oriented to true heading; vessels ≤50 m may use the simple
  symbol.
  → **Adopted:** the source always publishes a feature when
  enabled; the renderer always emits at least one visible style
  (disc or hull). The 50 m rule is honoured by `OwnShipSettings`
  defaulting to `LengthMetres = 50` — at the boundary; users with
  larger vessels (>50 m) edit the setting and get the scaled
  outline gated by §1.1's 6 mm pixel rule. We do not enforce the
  ≤50 m → simple-only rule because the user's choice of dimensions
  is itself the answer.

### 1.4 ITU-R M.1371 — AIS technical characteristics

**ITU-R M.1371-5.** Freely available at
[itu.int](https://www.itu.int/rec/R-REC-M.1371/en).

- **§3.3.8.2.3 / Annex 8, Table 11 — Message 5 "Ship Dimensions /
  reference for position":** four four-quadrant antenna offsets in
  metres, named A, B, C, D, all measured from the AIS GNSS antenna.
  - **A** = distance to bow (longitudinal forward)
  - **B** = distance to stern (longitudinal aft)
  - **C** = distance to port side
  - **D** = distance to starboard side

  Vessel length = A + B; beam = C + D.

  → **Adopted with field-name mapping:** an AIS adapter populates
  `DynamicVesselGeometry` as:

  | AIS field | `DynamicVesselGeometry` |
  |-----------|-------------------------|
  | A         | `BowOffsetMetres`       |
  | C         | `PortOffsetMetres`      |
  | A + B     | `LengthMetres`          |
  | C + D     | `BeamMetres`            |

  This is direct — no transformation, no information loss in
  either direction. Stern and starboard offsets are recoverable
  as `LengthMetres − BowOffsetMetres` and `BeamMetres −
  PortOffsetMetres`.

---

## 2. Q1 — Where do vessel dimensions live?

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

## 3. Q2 — Pixel-threshold switch between outline and pictogram

**Decision: emit both shapes, with mutually-exclusive style-level
resolution gates** (option (a) of the task brief). No
`IDynamicFeatureRenderer` signature change; Mapsui filters per frame.

### Threshold

```
MinVesselPixels = 22   // ≈ 6 mm at 96 dpi — S-52 Ed 6.1 §§7.4.5 / 13.2.7
```

(6 mm × 96 dpi ÷ 25.4 mm/in = 22.68 px, rounded down. See §1.1.)

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

## 4. Q3 — Outline shape

**Decision: 5-vertex hull polygon in vessel-local metres**, with a
bow-taper constant of `0.7`. Cites S-52 SY(OWNSHP02) — see §1.1.

The S-52 symbol library's `OWNSHP02` vertex coordinates are IHO
copyright and not reproduced here, but the canonical structural
form (small-vertex polygon parameterised by length + beam with a
bow taper, origin at CCRP, rotated by heading) maps directly onto
our 5-vertex implementation below. The kickoff's "canonical
7-point hull" hint was not borne out by the standards survey;
S-52's silhouette is a stylised hull form, not a literal one.

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
§4.1 for the algebra.

### 4.1 Georeferencing the hull

The GPS antenna sits at vessel-local `(−B/2 + PortOffsetMetres,
+L − BowOffsetMetres)`. The published lat/lon is the antenna
position. So each hull vertex `v` is translated into world frame as
`v − antenna_local`, then rotated by heading, then projected through
the small-angle metres→degrees helper (see §4.3).

The helper is good to ~1 m for vessel-scale offsets at any
non-polar latitude; we are not designing for tankers near 89° N.

### 4.2 Heading fallback

If `HeadingDeg` is `null`, use `CourseOverGroundDeg`. If both are
null (zero-speed startup), draw the hull aligned to north — better
than not drawing it at all.

### 4.3 Local-metres → world helper

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

### 4.4 Colours

Single palette for v1 — reuse the existing `DefaultStroke` /
`DefaultFill` blue. S-52 vessel-shape colours (four-shade per
display mode) are out of scope; we'll thread the colour from the
active palette in a future PR if it reads poorly under dusk/night.

---

## 5. Q4 — Arrowhead on the COG / speed vector

**Decision: filled triangle arrowhead via `SymbolStyle`, fixed pixel
size**. Cites S-52 vector conventions — see §1.1.

**Naming clarification (per standards survey):** S-52 distinguishes
three vectors — a true *heading line* (no arrowhead, no tick), a
*COG vector* (arrowhead at tip), and a *speed vector* (arrowhead
+ minute-interval tick marks). Our v1 source emits a single line
sourced from `CourseOverGroundDeg` and mirrored to `HeadingDeg`;
the renderer styles it per S-52's **COG-vector convention**
(arrowhead, no ticks). When a future PR introduces a real gyro
heading source distinct from COG, a separate arrowless heading
line should be added per S-52 — out of scope here.

The vector remains a `LineString` (start at antenna, end at the
6-minute-predictor point); the arrowhead is a separate point
feature at the line's end with a triangular `SymbolStyle` rotated
to the heading. Sizing constant:

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

## 6. Q5 — CCRP cross

**Decision: small `+` symbol at the antenna position, emitted only
when the outline is shown** (same `MinVisible`/`MaxVisible` gate as
the hull). Cites S-52 §8.3.1 — see §1.1.

The CCRP indicator is informational — drawing it at the disc-only
zoom would be both visually noisy and not meaningfully informative
(the disc already centres on the antenna). Gating it to outline-mode
keeps it useful where it matters.

Implementation: two short crossed `LineString` features (one
horizontal, one vertical) drawn in screen space, each spanning
`CcrpCrossPx = 6` per arm, gated by the same `MaxVisible` as the
hull. This addendum replaces an earlier v1 placeholder that used
a single `SymbolType.Rectangle`, which read as a square dot rather
than the S-52 `+` glyph.

---

## 7. Q6 — Settings persistence + UI

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

## 8. Q7 — Renderer registration

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

## 9. Test surface

| Test project | Cases |
|---|---|
| `EncDotNet.S100.Core.Tests` | `DynamicVesselGeometry` record equality; `DynamicFeature` `with` expressions preserving the sidecar. |
| `EncDotNet.S100.Renderers.Mapsui.Tests` (or pipelines tests) | Hull vertex placement: heading 0°/90°/180°, antenna at bow vs amidships, assert vertex lat/lon to 5 decimals. Resolution-gate values: outline `MaxVisible` and pictogram `MinVisible` agree on the computed `R_switch`. Pictogram-only fallback when `VesselGeometry == null`. Arrowhead omitted when no heading. CCRP cross emitted iff outline. |
| `EncDotNet.S100.Viewer.Tests` | `OwnShipSettings` JSON round-trip + defaulting when absent. Settings → provider → source: assert source raises `Updated` on geometry mutation. |

---

## 10. Open items (not blocking)

- Bow-taper ratio user-settable? **Default: no.** Hardcoded `0.7`.
- Double-stroke the outline at very small effective widths? **Default:
  no.** Revisit if it reads poorly on dusk/night palettes.
- `SymbolType.Triangle` rotation precision — decide at impl time
  whether to keep it or fall back to a chevron `LineString`.
- Palette adaptation to dusk/night (S-52 colour tokens) — future.
