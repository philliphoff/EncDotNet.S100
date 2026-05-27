# Dynamic Feature Source — Design Note

> Status: **Design / Research** — no production code in this PR. This
> document is the contract that the implementation PR (and the
> downstream own-ship and AIS-sample PRs) will build against. Open
> questions are collected in §9.

## 0. Scope of this note

Every feature drawn by the viewer today originates in a **static
dataset** — an S-101 ENC, an S-102/S-104/S-111 HDF5 grid, an
S-124/S-125/S-127/S-128/S-129/S-131/S-201/S-411/S-421 GML payload —
loaded once through a dataset processor and re-rendered as a snapshot
when the viewport, time-step, or selection changes. The pipeline is
**pull-shaped and snapshot-bound**: the dataset is the source of
truth, the processor produces a Mapsui layer (or a `DrawingInstruction`
list), and re-renders are driven by the host.

This note describes a small library-level abstraction for the other
half of the problem: **push-driven sources** that publish features in
real time and want to participate in the same rendering surface
without coupling the library to any specific feed protocol. Concrete
motivating consumers, **none of which is committed to ship by this
PR**:

1. Own-ship position + heading + speed.
2. AIS targets (1 Hz updates across many vessels; lifecycle:
   active / sleeping / lost).
3. Manually-drawn route-preview / what-if waypoints.
4. Sensor overlays (weather buoys, virtual ATON sensors).
5. Weather contours (polygon push).
6. Fleet-management track history (polyline push).

Everything else — NMEA decoding, an AIS adapter, S-52 vessel
symbology, own-ship UI, time-axis scrubbable replay of dynamic
feeds, persistence/replay, MCP exposure of a dynamic source — is
explicitly **out of scope** for this design and parked in §2.

---

## 1. Background & motivation

### 1.1 What the pipeline does today

Each dataset processor (`IDatasetProcessor` implementations in
`EncDotNet.S100.Datasets.Pipelines`) consumes a file, produces one or
more Mapsui `ILayer` instances, and hands them to the viewer's
`IMapHost.AddLayer` (the **dataset tier**). Two adjacent tiers
already exist:

- **Basemap tier** — beneath datasets, configured by the host.
- **Overlay tier** — above datasets. Two precedents:
  - `MeasureOverlayLayer` (a `MemoryLayer` carried by `MeasureTool`)
    for in-flight measurement chrome.
  - The in-flight **validation-findings overlay** (session
    `303c3372`) proposes extending `IMapHost` with explicit
    `AddOverlayLayer(ILayer)` / `RemoveOverlayLayer(ILayer)` to
    formalise this tier.

The S-100 portrayal engines (Lua via MoonSharp for S-101 / S-131;
XSLT for the GML-encoded products) sit **inside** dataset processors.
They are wired to S-100 Feature Catalogues, Portrayal Catalogues,
viewing groups, drawing priorities, and scale denominators. They are
not, and should not become, a generic display engine.

### 1.2 What's missing

There is no first-class way for an external, push-driven source to:

- Publish a feature, update its geometry / attributes a moment later,
  and have the change picked up by the rendering surface without
  reloading a dataset.
- Surface in the **Layer Stack UI** (#120) as a visibility-toggleable
  entry alongside dataset layers and overlays.
- Reuse the existing overlay tier rather than reach into
  `Map.Layers` from viewer-side glue.
- Be tested headlessly without standing up Avalonia / `MapControl`.

A consumer that wants any of the above today has to either pretend
to be a dataset processor (which inverts the snapshot/push semantics
and pollutes the time-axis) or reach into `MapControl.Map.Layers`
directly (which bypasses the host abstractions and the Layer Stack
UI). Neither is acceptable as a long-term path.

### 1.3 Push vs snapshot, framed

| Property                | Static dataset                    | Dynamic source                    |
| ----------------------- | --------------------------------- | --------------------------------- |
| Origin                  | File (ISO 8211, HDF5, GML)        | Live feed, sensor, user gesture   |
| Update cadence          | Reload-only                       | Sub-second to seconds             |
| Snapshot semantics      | The whole file is one snapshot    | "Current" = most recent reports   |
| Identity                | Spec-driven (record id, ordinal)  | Source-chosen (MMSI, GUID, name)  |
| Time axis participation | Yes (S-104/S-111 scrubable)       | No (v1 — always "now")            |
| Portrayal               | FC + PC + Lua/XSLT engine         | Adapter-supplied renderer         |
| Lifecycle               | Loaded / unloaded                 | Appear / update / age / lose      |

The abstraction proposed here belongs on the right-hand column and
must not be wedged into the left-hand pipeline. Pushing the two
together — by treating a dynamic source as a "live dataset" — would
either inherit Feature Catalogue assumptions that don't apply or
fork the dataset pipeline in ways that hurt both halves.

---

## 2. Non-goals

This abstraction explicitly does **not** try to be:

- **A feed-protocol library.** No NMEA-0183, NMEA-2000, AIS VDM/VDO,
  GPS, MQTT, WebSocket, or vendor-specific telemetry decoding. Those
  belong in adapters, samples, or third-party packages.
- **An S-52 / IEC 62388 symbology engine.** Vessel-triangle styling,
  sleeping-vs-lost target chrome, AIS-class colouring, anti-grounding
  indicators — all are the **adapter's** responsibility. The library
  ships a deliberately plain default renderer.
- **A second portrayal pipeline.** Dynamic features have no Feature
  Catalogue, no Portrayal Catalogue, no S-98 Interoperability
  Catalogue, no viewing-group resolution, no scale-denominator
  evaluation. The Lua and XSLT engines are wired to S-100 catalogues
  and must not be polluted with dynamic-source rules.
- **A time-axis participant** (v1). Dynamic sources always render
  "now". When the user scrubs the S-104 time slider, AIS targets do
  not snap to historical positions. An opt-in
  `ITimeAwareDynamicSource` is sketched in §5 Q8 and deferred.
- **A persistence layer.** No recording, no replay, no
  serialisation. A recorded log file is the *adapter's* responsibility
  if the adapter wants one.
- **An own-ship application.** The own-ship overlay is the smallest
  credible consumer of this abstraction (§8), but its UI, settings,
  vector-length controls, and GPS source selection are out of scope
  here.
- **A raster / coverage push channel.** Sensor imagery, radar echo,
  future S-411 push variants — a future `IDynamicCoverageSource`
  mirrors this pattern but is not designed here.
- **An MCP surface.** Whether and how a dynamic source is exposed via
  MCP is a follow-up question.
- **A central renderer registry across sources.** The DI keyed-service
  mechanism described in §5 Q6 provides registration; a global
  `Kind`-keyed registry is unnecessary and avoided.

---

## 3. Design overview

The abstraction comprises three concerns, each placed in the
assembly that already owns the relevant dependency line:

1. **Source.** An `IDynamicFeatureSource` (in
   `EncDotNet.S100.Core`) advertises a stable instance `Id`, some
   metadata (display name, a string `RendererKey`), an enumerable
   snapshot of `DynamicFeature` instances, and a `Changed` event.
   Sources are graphics-agnostic and have no Mapsui dependency.

2. **Renderer.** An `IDynamicFeatureRenderer` (in
   `EncDotNet.S100.Renderers.Mapsui`) consumes a `DynamicFeature` and
   emits Mapsui `IFeature` + `IStyle`. A `DefaultDynamicFeatureRenderer`
   dispatches on `GeometryType` (point, curve, surface) and is the
   fallback when no specialised renderer is registered. Composition
   helpers (`CompositeDynamicFeatureRenderer`,
   `KindMatchingRenderer`) let adapters dispatch within a source on
   the opaque `Kind` string.

3. **Glue.** A viewer-side `DynamicSourceOverlayHost` (in
   `EncDotNet.S100.Viewer`) subscribes to a registered source, resolves
   the renderer at registration time via DI keyed services
   (`IServiceProvider.GetKeyedService<IDynamicFeatureRenderer>(source.Metadata.RendererKey)`),
   maintains a backing `MemoryLayer`, marshals updates to the UI
   thread, attaches the layer to the overlay tier via
   `IMapHost.AddOverlayLayer`, and surfaces the source as a
   `LayerStackEntry` in the `DynamicArrows` plane of the Layer Stack
   UI.

The actor flow per source:

```
                  +-------------------+
adapter writes -->| IDynamicFeature   |
(any thread)      |     Source        |
                  +---------+---------+
                            | CurrentFeatures, Changed
                            v
            +---------------+----------------+
            | DynamicSourceOverlayHost       |    (viewer)
            |  - resolves renderer via DI    |
            |  - marshals to UI thread       |
            |  - rebuilds MemoryLayer        |
            +---------------+----------------+
                            |
              +-------------+--------------+
              |                            |
              v                            v
   IDynamicFeatureRenderer       LayerStackEntry
   (default or registered)       (DynamicArrows plane)
              |
              v
        IFeature + IStyle
              |
              v
        MemoryLayer
              |
              v
   IMapHost.AddOverlayLayer
              |
              v
        MapControl
```

The correlation between **a source** (instance) and **a renderer**
(class-of-sources) is mediated by the string `RendererKey` on
`DynamicSourceMetadata`:

- `Id` is **instance-unique** (`"ownship"`, `"ais.port-of-seattle"`,
  `"ais.port-of-tacoma"`).
- `RendererKey` is **class-level** (`"ais.vessel"`, shared by both AIS
  sources above; `"ownship.point"` for the singleton own-ship source;
  `null` to opt into the default).

The viewer's `DynamicSourceOverlayHost` resolves `RendererKey` against
the DI container at register time. If the key is `null`, unregistered,
or registered against a type that doesn't implement
`IDynamicFeatureRenderer`, the host logs a warning and uses
`DefaultDynamicFeatureRenderer`. Every source draws something.

---

## 4. Surface

All types below are **stub-level** — XML-doc summaries and shapes,
not implementations. Method bodies, equality implementations, and
defensive validation are filled in by the implementation PR.

### 4.1 `EncDotNet.S100.Core` — graphics-agnostic types

```csharp
namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// A push-driven publisher of one or more dynamic features. Sources
/// are graphics-agnostic and may be implemented by any consumer of
/// EncDotNet.S100.Core.
/// </summary>
public interface IDynamicFeatureSource
{
    /// <summary>
    /// Instance-unique identifier — distinguishes this source from
    /// other sources of the same kind in the same host (e.g. two
    /// AIS feeds for different ports).
    /// </summary>
    string Id { get; }

    /// <summary>Display metadata and renderer-resolution hints.</summary>
    DynamicSourceMetadata Metadata { get; }

    /// <summary>Most recent snapshot of features known to the source.</summary>
    IReadOnlyList<DynamicFeature> CurrentFeatures { get; }

    /// <summary>
    /// Raised when <see cref="CurrentFeatures"/> changes. May be raised
    /// on any thread. The viewer-side host marshals to the UI thread
    /// before mutating Mapsui state.
    /// </summary>
    event EventHandler<DynamicFeaturesChanged>? Changed;
}

/// <summary>
/// Display metadata and renderer-resolution hints for a dynamic source.
/// </summary>
public sealed record DynamicSourceMetadata
{
    /// <summary>Human-readable label for the Layer Stack and the title bar.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Lookup key for the IDynamicFeatureRenderer registered to draw
    /// features from this source. When null or unresolved, the viewer
    /// falls back to DefaultDynamicFeatureRenderer.
    /// </summary>
    public string? RendererKey { get; init; }

    /// <summary>Optional longer description shown in tooltips / settings.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// One push-driven feature. Geometry vocabulary is intentionally
/// identical to <c>EncDotNet.S100.Pipelines.Vector</c>'s static
/// feature shape, so adapters that bridge to a snapshot of a static
/// dataset get a one-line projection. Coverage and None are excluded
/// from <see cref="GeometryType"/> by scope.
/// </summary>
public sealed record DynamicFeature
{
    /// <summary>
    /// Source-stable opaque identity. Source chooses the semantics
    /// (MMSI for AIS, "ownship" for an own-ship singleton, GUID for
    /// a route waypoint, "weather.contour.2025-11-08T12:00:00Z.980hPa"
    /// for an isobar). Stability across updates is a hard contract.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Opaque renderer-dispatch hint. Has no Feature Catalogue
    /// meaning. Conventional examples: "vessel.cargo", "vessel.tanker",
    /// "vessel.unknown", "ownship", "waypoint", "weather.isobar".
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// Geometry kind — referenced from
    /// <see cref="EncDotNet.S100.Pipelines.Vector.GeometryType"/>.
    /// Dynamic sources must use Point, Curve, or Surface.
    /// </summary>
    public required GeometryType GeometryType { get; init; }

    /// <summary>
    /// Geometry coordinates in WGS-84 lat/lon. Same convention as the
    /// static vector pipeline: latitude first. Cardinality depends on
    /// <see cref="GeometryType"/> (1 for Point, ≥2 for Curve, closed
    /// ring for Surface).
    /// </summary>
    public required IReadOnlyList<(double Latitude, double Longitude)> Coordinates { get; init; }

    /// <summary>
    /// Optional motion sidecar — only meaningful for moving point
    /// features (own-ship, AIS). Static features (waypoints, weather
    /// contours, sensor readings) leave this null.
    /// </summary>
    public DynamicMotion? Motion { get; init; }

    /// <summary>
    /// Caller-defined extra attributes — vessel name, MMSI, call sign,
    /// pressure level, sensor reading, leg label, etc. Renderers
    /// consume this opaquely.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Attributes { get; init; }
        = new Dictionary<string, object?>();

    /// <summary>UTC timestamp of the most recent update.</summary>
    public required DateTimeOffset LastUpdated { get; init; }
}

/// <summary>Motion data for a moving point feature.</summary>
public sealed record DynamicMotion
{
    public double? CourseOverGroundDeg { get; init; }
    public double? HeadingDeg { get; init; }
    public double? SpeedOverGroundKn { get; init; }
}

/// <summary>Hint about which features changed in a <see cref="IDynamicFeatureSource.Changed"/> event.</summary>
public sealed record DynamicFeaturesChanged
{
    public required DynamicSourceChangeKind Kind { get; init; }

    /// <summary>
    /// Feature ids touched by this change. May be empty for
    /// <see cref="DynamicSourceChangeKind.Reset"/>.
    /// </summary>
    public IReadOnlyList<string> ChangedIds { get; init; } = Array.Empty<string>();
}

public enum DynamicSourceChangeKind
{
    /// <summary>One or more features appeared.</summary>
    Added,

    /// <summary>One or more existing features were updated in place.</summary>
    Updated,

    /// <summary>One or more features were removed.</summary>
    Removed,

    /// <summary>Wholesale reset — overlay should re-read CurrentFeatures.</summary>
    Reset,
}

/// <summary>
/// Optional helper that an adapter with aging semantics (AIS sleep/lost,
/// stale sensor styling) can opt into. Sources without aging do not use
/// it. The library does not impose any timer defaults — adapters supply
/// their own thresholds.
/// </summary>
public sealed class DynamicFeatureTracker<TInbound>
{
    public DynamicFeatureTracker(Func<TInbound, DynamicFeature> project);

    /// <summary>Apply one inbound update.</summary>
    public DynamicFeaturesChanged Apply(TInbound update);

    /// <summary>Sweep expired entries given a "now" timestamp.</summary>
    public DynamicFeaturesChanged Sweep(DateTimeOffset now, TimeSpan stale, TimeSpan lost);

    public IReadOnlyList<DynamicFeature> Current { get; }
}
```

### 4.2 `EncDotNet.S100.Renderers.Mapsui` — Mapsui-bound types

```csharp
namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Renders a dynamic feature as one or more Mapsui IFeature + IStyle
/// pairs. Renderers are pure functions of a feature snapshot — they
/// do not subscribe to sources, do not retain state, and may be
/// called on any thread.
/// </summary>
public interface IDynamicFeatureRenderer
{
    /// <summary>
    /// True if this renderer can produce output for <paramref name="feature"/>.
    /// Used by <see cref="CompositeDynamicFeatureRenderer"/> for fallthrough.
    /// </summary>
    bool CanRender(DynamicFeature feature);

    /// <summary>
    /// Produce zero or more Mapsui features for <paramref name="feature"/>.
    /// Coordinates are projected to SphericalMercator (EPSG:3857) by the
    /// renderer.
    /// </summary>
    IEnumerable<IFeature> Render(DynamicFeature feature);
}

/// <summary>
/// Geometry-kind-dispatching fallback. Point → coloured disc + optional
/// heading line (if Motion.HeadingDeg is set). Curve → stroked line.
/// Surface → translucent fill + outline. Used when no specialised
/// renderer is registered for a source's RendererKey.
/// </summary>
public sealed class DefaultDynamicFeatureRenderer : IDynamicFeatureRenderer { }

/// <summary>
/// Composes a list of renderers; the first whose CanRender returns
/// true handles the feature. The default renderer typically sits last.
/// </summary>
public sealed class CompositeDynamicFeatureRenderer : IDynamicFeatureRenderer
{
    public CompositeDynamicFeatureRenderer(IEnumerable<IDynamicFeatureRenderer> renderers);
}

/// <summary>
/// Dispatches on <see cref="DynamicFeature.Kind"/> via exact or
/// prefix match (e.g. "vessel.cargo", "vessel.tanker", or just
/// "vessel.*"). The match policy is configured at construction.
/// </summary>
public sealed class KindMatchingRenderer : IDynamicFeatureRenderer
{
    public KindMatchingRenderer(IReadOnlyDictionary<string, IDynamicFeatureRenderer> byKind);
}

/// <summary>
/// DI helpers that correlate a source registration with a renderer
/// registration via a shared string key.
/// </summary>
public static class DynamicFeatureRendererServiceCollectionExtensions
{
    /// <summary>
    /// Register an IDynamicFeatureRenderer keyed by <paramref name="rendererKey"/>.
    /// Resolved by the viewer overlay host via GetKeyedService.
    /// </summary>
    public static IServiceCollection AddDynamicFeatureRenderer<TRenderer>(
        this IServiceCollection services,
        string rendererKey,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TRenderer : class, IDynamicFeatureRenderer;

    /// <summary>
    /// Register both a source and its renderer under the same key in
    /// one call. The convenience extension for adapter authors.
    /// </summary>
    public static IServiceCollection AddDynamicFeatureSource<TSource, TRenderer>(
        this IServiceCollection services,
        string rendererKey,
        ServiceLifetime sourceLifetime = ServiceLifetime.Singleton,
        ServiceLifetime rendererLifetime = ServiceLifetime.Singleton)
        where TSource : class, IDynamicFeatureSource
        where TRenderer : class, IDynamicFeatureRenderer;
}
```

### 4.3 `EncDotNet.S100.Viewer` — glue

```csharp
namespace EncDotNet.S100.Viewer.DynamicSources;

/// <summary>
/// Subscribes to dynamic sources, resolves their renderers from DI by
/// RendererKey, attaches a backing MemoryLayer to the overlay tier of
/// IMapHost, marshals all Mapsui mutations to the UI thread, and
/// publishes a LayerStackEntry per source.
/// </summary>
public sealed class DynamicSourceOverlayHost : IDisposable
{
    public DynamicSourceOverlayHost(IMapHost mapHost, IServiceProvider services);

    /// <summary>
    /// Register a source. Resolves IDynamicFeatureRenderer keyed by
    /// <c>source.Metadata.RendererKey</c>; falls back to the default
    /// renderer when the key is null or unregistered.
    /// </summary>
    public IDisposable Register(IDynamicFeatureSource source);
}

/// <summary>
/// Layer Stack entry adapter for a registered dynamic source. Implements
/// the same IsActive / visibility / display-plane contract that
/// LayerStackEntryViewModel consumes for dataset and overlay entries.
/// Sources surface in the DynamicArrows plane unless DynamicSourceMetadata
/// overrides via a future extension (see §9).
/// </summary>
public sealed class DynamicSourceRegistration { }
```

### 4.4 `IMapHost` overlay-tier methods

This design **reuses** the overlay-tier API proposed by the
in-flight validation-findings session
(`303c3372-5392-42f5-b7d5-4eca7e8bddb5`):

```csharp
// EncDotNet.S100.Viewer.Services.IMapHost (added by validation-overlay PR)
void AddOverlayLayer(ILayer layer);
void RemoveOverlayLayer(ILayer layer);
```

If that session adopts a different surface (a separate `IOverlayHost`,
a collection property, etc.), this design follows the chosen shape.
See §9.

---

## 5. Design decisions

Each question is presented as **Options / Recommendation / Rationale**.

### Q1 — Abstraction granularity

**Options.**

- (a) One source-of-many interface (`IDynamicFeatureSource` enumerates
  `DynamicFeature` instances).
- (b) One interface per feature (`IDynamicFeature` directly
  subscribed by the overlay).
- (c) Both — split into a `IDynamicTarget` for the singleton case
  and a `IDynamicFeatureSource` for the many case.

**Recommendation:** (a). Single-feature consumers (own-ship,
route-preview) expose a one-element collection; the cost is trivial
and the API surface stays small.

**Rationale.** AIS pushes one source toward many features per
publisher. Own-ship pushes the other way. (c) doubles the contract.
(b) makes the overlay subscribe N times for AIS, which is fine
mechanically but explodes the registration story (one DI entry per
target). The collection-shaped interface is the union that covers
both shapes without bifurcating consumers.

### Q2 — Feature shape and geometry vocabulary

**Options.**

- (a) `DynamicTarget` record with `GeoPosition Position` + optional
  motion fields. Point-only.
- (b) `DynamicFeature` record with a `DynamicGeometry` discriminated
  union (Point / Curve / Surface).
- (c) `DynamicFeature` record referencing
  `EncDotNet.S100.Pipelines.Vector.GeometryType` + a
  `IReadOnlyList<(double Latitude, double Longitude)>` — same
  convention as `Vector.Feature`.
- (d) Reuse `Vector.Feature` directly.

**Recommendation:** (c). See §6.2 for the full rationale.

**Rationale.** (a) wedges the contract to vessels and excludes
genericity (§1.3). (b) introduces a parallel discriminated union for
the same concept that already exists in the static vector pipeline,
forcing renderer authors to learn two vocabularies. (c) reuses what's
already there. (d) was tempting but breaks down on three semantic
mismatches between static and dynamic features:

1. **Identity.** `Vector.Feature.Id` is `long` (ISO 8211 record id /
   GML ordinal). Dynamic sources need a **stable opaque string** —
   MMSI, `"ownship"`, GUID, `"route.leg.3"`. Hashing strings to longs
   loses round-trippability and obscures intent.
2. **Type semantics.** `Vector.Feature.FeatureType` resolves against
   a Feature Catalogue. Dynamic features have no FC; `Kind` is a
   renderer-dispatch hint, not a catalogued type. Sharing the same
   field invites confusion and accidental FC lookups.
3. **Temporal & motion fields.** `Vector.Feature` has no `LastUpdated`
   (static datasets don't need one) and no motion sidecar (static
   features don't move). Burying either in `Attributes` loses IDE
   discoverability and prevents renderers from dispatching cleanly
   on "this point has heading".

The chosen shape (`DynamicFeature`) keeps the geometry vocabulary
identical to `Vector.Feature` (same `GeometryType` enum, same
`(Latitude, Longitude)` tuple) so adapters that ever want to bridge
get a one-line projection — but separates the fields whose semantics
genuinely differ.

### Q3 — Update model: push vs pull, event vs stream

**Options.**

- (a) Snapshot + change-event: `CurrentFeatures` property +
  `event EventHandler<DynamicFeaturesChanged> Changed`.
- (b) `IAsyncEnumerable<DynamicFeaturesChanged>` stream.
- (c) `IObservable<DynamicFeaturesChanged>`.
- (d) Snapshot pull only — overlay polls at a fixed rate.

**Recommendation:** (a).

**Rationale.** The overlay redraws on viewport change, on global
state change, and on dynamic-source change. Each redraw needs the
**current snapshot**, not the historical stream. (a) gives it that
directly via `CurrentFeatures`. The change-event carries a
`DynamicSourceChangeKind` (`Added` / `Updated` / `Removed` / `Reset`)
and the touched ids so the overlay can either re-read the snapshot or
apply the diff. (b) and (c) are streams; the overlay would still need
a parallel snapshot, doubling the contract. (d) leaks the cadence
choice into the source contract.

Allocation pattern at 1 Hz × hundreds of AIS targets: one
`DynamicFeaturesChanged` per debounced batch, ids list of changed
mmsi strings. The overlay re-projects only the changed features (see
§5 Q9).

### Q4 — Lifecycle / aging

**Options.**

- (a) Inside the source — each adapter implements its own aging
  state machine.
- (b) Inside the library — a shared `DynamicFeatureTracker<TInbound>`
  utility that adapters opt into.
- (c) Inside the renderer — compute display state from `LastUpdated`
  and a per-renderer policy.

**Recommendation:** (b), with (a) supported for sources that don't
need aging.

**Rationale.** AIS has spec-defined aging timers (sleeping at 6 min
for Class A vessels, lost at ~6 min thereafter, retired at 60 min;
the constants are AIS-specific and live in the adapter, not the
library). A weather-contour source has no aging. A route-preview
source has no aging. The library should not impose timer defaults.

What the library *can* offer is the data-structure plumbing (an id
→ feature dictionary, an apply-update method, a sweep-stale method,
a change-event emitter) so adapters that need aging don't reimplement
the same dictionary-and-event boilerplate. That's
`DynamicFeatureTracker<TInbound>`. Adapters without aging
(route-preview, weather contours, single-target own-ship) skip it
and implement `CurrentFeatures` / `Changed` directly — fine, the
contract is small.

(c) was rejected because the renderer is a pure function of a
feature snapshot — pushing lifecycle there would re-introduce state
and prevent the headless testing strategy (§7).

### Q5 — Coordinate frames & projection

**Options.**

- (a) Source publishes WGS-84 lat/lon for all geometry kinds; overlay
  projects to EPSG:3857 (SphericalMercator). Same as dataset renderers
  and `MeasureOverlayLayer`.
- (b) Source publishes already-projected coordinates.
- (c) Source declares its CRS in metadata; overlay reprojects.

**Recommendation:** (a).

**Rationale.** AIS canon is WGS-84 lat/lon. GPS canon is WGS-84
lat/lon. Weather feeds are WGS-84 lat/lon. Pushing projection into
the source forces every adapter to depend on ProjNet (or equivalent)
and to know the target map CRS. Consistency with dataset renderers
(which all project at render time) is the strong default. (b) and
(c) are explicit non-goals; adapters that consume non-WGS-84 data
project to WGS-84 themselves before publishing.

### Q6 — Rendering: matching renderer to source

**Options.**

- (a) `IDynamicFeatureSource` exposes a `Renderer` property.
- (b) Two-tier interface — `IMapsuiDynamicFeatureSource :
  IDynamicFeatureSource` in `Renderers.Mapsui` adds the renderer.
- (c) Neutral display-primitive vocabulary in `Core` — renderer
  returns an abstract type, Mapsui adapter converts.
- (d) `DynamicSourceMetadata.RendererKey` (string) resolved against
  the DI container via keyed services; correlated registration helper
  ships in `Renderers.Mapsui`.
- (e) Single central `Kind`-keyed renderer registry across all
  sources.

**Recommendation:** (d).

**Rationale.** The source must live in `Core` (headless / MCP /
non-Mapsui consumers need it). The renderer must live in
`Renderers.Mapsui` (it returns Mapsui `IFeature` + `IStyle`). Any
property on the source typed as `IDynamicFeatureRenderer` forces
`Core` to reference `Renderers.Mapsui`, defeating the
graphics-agnostic-source goal — (a) is rejected for that reason.

(b) works mechanically but every adapter ships two types (a source
and a Mapsui-tier wrapper) and cross-source renderer sharing is
awkward — each source instance binds its own renderer, so two AIS
feeds register two renderer instances.

(c) introduces a third graphics language alongside Mapsui's `IFeature`
and S-100's `DrawingInstruction`. Reusing `DrawingInstruction` itself
was considered: rejected because it carries S-100 Part 9 portrayal
semantics (`Plane`, `ViewingGroup`, `DrawingPriority`,
`ScaleMinimum/Maximum`, FC-resource-name references) that dynamic
features have no business inheriting.

(d) keeps `IDynamicFeatureSource` in `Core` and
`IDynamicFeatureRenderer` in `Renderers.Mapsui` with no cross-assembly
type dependency. The correlation is a string plus a DI registration.
`Id` (instance-unique) and `RendererKey` (class-level) are
correctly separated, so multiple AIS feeds naturally share one
renderer registration. The convenience helper
`AddDynamicFeatureSource<TSource, TRenderer>(rendererKey)` makes the
pair hard to break at registration time. Headless / MCP consumers
register a source without a renderer — fine.

(e) — a single central `Kind`-keyed registry — is unnecessary because
DI keyed services already provide registration, and within-source
dispatch (AIS cargo vs tanker vs unknown) is better handled by a
per-source `KindMatchingRenderer` composed inside the adapter's
single renderer registration.

**Fallback contract.** When the overlay cannot resolve a renderer
for `source.Metadata.RendererKey` (null, no registration, incompatible
type) it logs a warning and uses `DefaultDynamicFeatureRenderer`.
Every source draws something.

**Within-source dispatch.** Adapters with multiple feature kinds
compose `CompositeDynamicFeatureRenderer` (fallthrough on `CanRender`)
and `KindMatchingRenderer` (exact / prefix match on `Kind`) inside
their single registered renderer.

### Q7 — Layer Stack integration

**Options.**

- (a) Each `IDynamicFeatureSource` surfaces as a `LayerStackEntry` in
  the `DynamicArrows` plane (S-98 Main §9.2.1 / MSC.530(106) Rev.1
  Appendix 2 layer 8) via a thin `DynamicSourceRegistration` adapter.
- (b) Dynamic sources share one Layer Stack entry per plane (all AIS,
  all own-ship, all weather collapsed).
- (c) Dynamic sources surface as a top-level "Live data" group above
  the dataset entries.

**Recommendation:** (a).

**Rationale.** The Layer Stack groups by `S98DisplayPlane`; the
`DynamicArrows` plane already exists for "vector data such as
targets/AIS/own-ship vectors". A registered source naturally maps
to one entry there — visibility toggle, opacity, ordering — reusing
the `LayerStackEntryViewModel` contract. (b) loses per-source
visibility (a user can't hide AIS while keeping own-ship visible).
(c) introduces a new top-level grouping that doesn't match the S-98
plane model.

Sources advertise `DisplayName` (and an optional `Description`) via
`DynamicSourceMetadata`. A future `PreferredPlane` override on the
metadata record is sketched in §9 (a weather-contour source might
prefer a non-`DynamicArrows` plane). v1 always lands sources in
`DynamicArrows`.

### Q8 — Time axis interaction

**Options.**

- (a) Ignore the global time slider — always render "now".
- (b) Snap to the nearest cached update at the scrubbed time
  (requires history retention).
- (c) Hide entirely when not at "live" time.

**Recommendation:** (a) for v1.

**Rationale.** AIS feeds have no scrubbable history by default — a
1 Hz feed retains only "what arrived most recently" unless the
adapter records to disk. Own-ship has no historical track unless the
adapter records one. Snapping at scrub time (b) requires every
source to be a history store, which the contract should not impose.
Hiding (c) is surprising — the user expects "live" data to show
during S-104 scrubbing as a sanity reference (the time slider is for
the hydrographic dataset, not the AIS feed).

The upgrade path is an opt-in `ITimeAwareDynamicSource` that the
overlay queries with the scrubbed instant; sources that retain
history return a historical snapshot, sources that don't ignore the
call. Deferred — and the v1 limitation is documented in the doc and
the XML-doc on `DynamicSourceOverlayHost`.

### Q9 — Threading model

**Options.**

- (a) Sources are thread-affinity-free; the overlay marshals to the
  UI thread before mutating Mapsui state.
- (b) Sources must raise `Changed` on the UI thread.
- (c) The library exposes a synchronization context the source uses.

**Recommendation:** (a).

**Rationale.** AIS feeds arrive on background threads. GPS feeds
arrive on background threads. Forcing each adapter to marshal — (b)
— spreads `Dispatcher.UIThread` knowledge across every adapter and
ties the source to Avalonia, defeating headless testing. (c) leaks
a sync context into `Core`.

The contract:

- `IDynamicFeatureSource.Changed` may be raised on **any** thread.
- `IDynamicFeatureSource.CurrentFeatures` and `Metadata` must be safe
  to read from any thread (sources implement this via `volatile`,
  `ImmutableList<DynamicFeature>` swap, or a lock — the choice is the
  adapter's).
- `DynamicSourceOverlayHost` is the single marshalling boundary. It
  subscribes to `Changed`, captures the snapshot, and calls
  `Dispatcher.UIThread.Post` to rebuild the `MemoryLayer`. Renderers
  are pure and called from the UI thread during rebuild.

Concrete marshal sites:

- `DynamicSourceOverlayHost.OnChanged(object?, DynamicFeaturesChanged)`
  → `Dispatcher.UIThread.Post(() => Rebuild(...))`.
- `Rebuild` reads `CurrentFeatures`, projects through the renderer,
  and replaces the `MemoryLayer.Features` collection.

A debounce gate (configurable via `DynamicSourceOverlayHost` ctor
option, default 100 ms / 10 Hz) coalesces rebuilds when a source
publishes faster than the UI can repaint. Per-feature diffing is a
follow-up optimisation; v1 rebuilds the whole `MemoryLayer` on each
debounce flush.

### Q10 — Testing strategy

**Options.**

- (a) Headless: a `FakeDynamicFeatureSource` test helper drives
  synthetic updates against a fake `IMapHost`. Default renderer
  covered by unit tests across each geometry kind. No Avalonia.
- (b) Avalonia-headless test app with the real `MapControl`.
- (c) Integration only — exercise via an end-to-end recorded log.

**Recommendation:** (a).

**Rationale.** The overlay host is testable as a plain object given
a fake `IMapHost`. The renderer is a pure function. The source
contract is event-based and trivially drivable from a test helper.

Test surface for the implementation PR:

- `FakeMapHost` records `AddOverlayLayer` / `RemoveOverlayLayer`
  calls and exposes the captured `ILayer`.
- `FakeDynamicFeatureSource` exposes a `Push(IEnumerable<DynamicFeature>,
  DynamicSourceChangeKind)` method to drive synthetic updates.
- `DefaultDynamicFeatureRenderer` unit tests across Point (with and
  without motion), Curve (≥2 points), Surface (closed ring).
- `DynamicSourceOverlayHost` unit tests:
  - Register / unregister adds and removes an overlay layer.
  - `Changed` event triggers a `MemoryLayer` rebuild.
  - Unknown `RendererKey` falls back to the default renderer (and
    logs).
  - Threading contract — a `Changed` raised on a `ThreadPool` thread
    results in mutation on a test dispatcher.
- `KindMatchingRenderer` exact / prefix match.

Avalonia-headless is not required for any of the above.

### Q11 — Scope boundary

**In.**

- `IDynamicFeatureSource`, `DynamicFeature` (reuses
  `Vector.GeometryType` + `(Latitude, Longitude)`), `DynamicMotion`,
  `DynamicFeaturesChanged`, `DynamicSourceChangeKind`,
  `DynamicSourceMetadata` (incl. `RendererKey`),
  `DynamicFeatureTracker<T>` — all in `EncDotNet.S100.Core`.
- `IDynamicFeatureRenderer`, `DefaultDynamicFeatureRenderer`,
  `CompositeDynamicFeatureRenderer`, `KindMatchingRenderer`,
  `DynamicFeatureRendererServiceCollectionExtensions` (DI helper) —
  in `EncDotNet.S100.Renderers.Mapsui`.
- `DynamicSourceOverlayHost`, `DynamicSourceRegistration` (Layer
  Stack adapter) — in `EncDotNet.S100.Viewer`.
- Projection convention (WGS-84 in, EPSG:3857 in overlay).
- Threading contract (sources thread-affinity-free; overlay marshals).

**Out.**

- NMEA-0183, NMEA-2000, AIS VDM/VDO, GPS, MQTT, WebSocket, or any
  feed-protocol library.
- S-52 / IEC 62388 symbology.
- Own-ship UI.
- MCP exposure of a dynamic source.
- Time-axis participation beyond the stub `ITimeAwareDynamicSource`
  upgrade path.
- Persistence / replay.
- AIS lifecycle constants (sleeping / lost / retired timers).
- Central renderer registry across sources beyond DI keyed services.
- Raster / coverage push (`IDynamicCoverageSource` deferred).

### Q12 — First-consumer roadmap

**Recommendation.** Three independently-shippable PRs:

1. **Library abstraction** (this design + implementation): the types
   in §4, the DI helpers, the overlay host, default renderer, and
   the Layer Stack adapter. Unit-tested per §7. No real-world feed.
2. **Own-ship overlay** as the smallest credible consumer: one
   source, one feature, drive from a test stub or a recorded NMEA
   log. Validates the contract under "single moving point + heading
   line".
3. **AIS sample** under `samples/` (or as a viewer plugin): uses a
   third-party NMEA parser (`NmeaParser` on NuGet is the obvious
   candidate; alternatives at §10). Drives from either a recorded log
   file or a public feed (AISHub WebSocket). Validates the contract
   under "many features, aging lifecycle, custom symbology".

The non-vessel motivating cases (route preview, sensor overlay,
weather contour, fleet-management track history) are **not** on the
sequencing list because they are genericity sanity checks, not
committed deliverables. The doc and the surface are designed such
that each is a small follow-up PR.

---

## 6. Integration with existing systems

### 6.1 `IMapHost` and the overlay tier

The dynamic-source overlay attaches its backing `MemoryLayer` via
the validation-overlay session's proposed
`IMapHost.AddOverlayLayer(ILayer)` / `RemoveOverlayLayer(ILayer)`.
That session is in flight; this design assumes the API lands and
defers the bikeshed.

Precedent for `MemoryLayer`-as-overlay:
`src/EncDotNet.S100.Viewer/Tools/MeasureOverlayLayer.cs` and its
host `MeasureTool`. The dynamic-source overlay follows the same
shape — one `MemoryLayer` per registered source, rebuilt on
`Changed`, attached and detached via the overlay-tier API.

### 6.2 Geometry vocabulary reuse

`EncDotNet.S100.Pipelines.Vector.Feature` (defined in
`src/EncDotNet.S100.Core/Pipelines/Vector/IVectorSource.cs`) already
exposes:

```csharp
enum GeometryType { Point, Curve, Surface, Coverage, None }
sealed class Feature {
    long Id; string FeatureType; GeometryType GeometryType;
    IReadOnlyList<(double Latitude, double Longitude)> Coordinates;
    IReadOnlyDictionary<string, object?> Attributes;
}
```

This is exactly the geometry vocabulary a dynamic source needs. A
point is a point; a curve is a curve; lat/lon is lat/lon. Inventing
a parallel `DynamicGeometry` discriminated union would create two
vocabularies for the same concept and force renderer authors to
learn both.

`DynamicFeature` therefore **references** `GeometryType` (Point /
Curve / Surface; Coverage and None excluded by scope) and uses the
same `(Latitude, Longitude)` tuple convention.

`DynamicFeature` does **not** inherit from or wrap `Vector.Feature` —
the three semantic mismatches enumerated in §5 Q2 (identity, type
semantics, temporal/motion) make a shared record more confusing than
helpful. Adapters that want to bridge — e.g. capture a moment-in-time
snapshot of a dynamic source as a static dataset — get a one-line
projection because the geometry fields are spelled identically.

### 6.3 `LayerStackViewModel` and the `DynamicArrows` plane

`LayerStackViewModel` groups entries by `S98DisplayPlane`. The
`DynamicArrows` plane (S-98 Main §9.2.1 / MSC.530(106) Rev.1
Appendix 2 layer 8 — "vector data such as targets/AIS/own-ship
vectors") is the natural home.

The viewer-side `DynamicSourceRegistration` adapter implements the
`IsActive` / visibility / display-plane contract that
`LayerStackEntryViewModel` consumes today. No bespoke chrome. Each
registered source appears as one row in the Layer Stack panel
alongside dataset entries — visibility toggle, opacity, drag-to-
reorder.

### 6.4 `GlobalTimeService` non-participation

The S-104 / S-111 time-slider is driven by
`src/EncDotNet.S100.Viewer/Services/GlobalTimeService.cs`. v1
dynamic sources **do not subscribe** to that service. The S-104
slider scrubs hydrographic data; dynamic sources render "now"
regardless. The opt-in `ITimeAwareDynamicSource` upgrade path
(§5 Q8) is sketched but deferred.

### 6.5 Validation-overlay precedent

The in-flight validation-findings overlay session
(`303c3372-5392-42f5-b7d5-4eca7e8bddb5`) is the closest precedent
for an overlay-tier `MemoryLayer`. Both designs:

- Sit above datasets in the overlay tier.
- Use `MemoryLayer` as the backing surface.
- Need an explicit `IMapHost` API (the validation session is
  proposing it; this design reuses it).
- Surface in the Layer Stack UI.

The two designs deliberately do not share concrete glue (validation
findings have a fundamentally different update model — produced
synchronously by a validation run, not pushed by an external
feed) but share the same hosting contract.

---

## 7. Testing strategy

Headless, no Avalonia. See §5 Q10 for the full enumeration. Summary:

- **`FakeMapHost`** captures `AddOverlayLayer` / `RemoveOverlayLayer`.
- **`FakeDynamicFeatureSource`** drives synthetic updates from test
  code.
- **`DefaultDynamicFeatureRenderer`** unit-tested across each
  geometry kind (with and without `Motion` for Point).
- **`DynamicSourceOverlayHost`** unit-tested for: register/unregister
  flow, change-event-triggered rebuild, unknown-`RendererKey`
  fallback, threading contract via a test dispatcher.
- **`KindMatchingRenderer`** unit-tested for exact and prefix match.
- **`DynamicFeatureTracker<T>`** unit-tested for apply / sweep
  state-machine transitions given a synthetic clock.

All tests live in a new
`tests/EncDotNet.S100.DynamicSources.Tests/` xunit project. Adapter-
level tests (own-ship, AIS) live in their respective follow-up
project / sample.

---

## 8. Sequencing — abstraction → own-ship → AIS sample

### 8.1 PR-D1: library abstraction

- Drop the types from §4 into their respective assemblies.
- DI helpers in `Renderers.Mapsui`.
- `DynamicSourceOverlayHost` and `DynamicSourceRegistration` in
  `Viewer`.
- Default renderer with three geometry-kind cases.
- Unit tests per §7.
- Documentation: this note links from `src/EncDotNet.S100.Core/README.md`
  and `src/EncDotNet.S100.Renderers.Mapsui/README.md`.

No real-world feed shipped. No `samples/` directory created.

### 8.2 PR-D2: own-ship overlay

- A concrete `OwnShipSource : IDynamicFeatureSource` in a new
  `src/EncDotNet.S100.DynamicSources.OwnShip/` project (or under
  `Viewer` if the surface is small enough — to be decided in PR-D2).
- Drive from a test stub or recorded NMEA log; no GPS hardware
  required.
- Optional: custom renderer keyed `"ownship"` if the default disc-
  plus-heading isn't acceptable for an own-ship indicator.
- Viewer wiring: register at startup, show in Layer Stack.
- Validates the abstraction under "single moving point with motion".

### 8.3 PR-D3: AIS sample

- A new `samples/EncDotNet.S100.DynamicSources.AisSample/` console +
  viewer integration.
- Third-party NMEA parser dependency:
  - **`NmeaParser`** (NuGet, MIT) — actively maintained, broad
    sentence coverage.
  - Alternatives: `NmeaParserCore`, `AisDecoder`, hand-rolled VDM/VDO
    parser. Decision is the AIS-sample PR's; **not in scope here**.
- Feed source: recorded log file primary, optional AISHub WebSocket
  secondary.
- Adapter implements `IDynamicFeatureSource` over a
  `DynamicFeatureTracker<AisReport>`, applying AIS sleeping / lost
  timers in adapter code (not in library code).
- Custom renderer keyed `"ais.vessel"` with a
  `KindMatchingRenderer` over cargo / tanker / passenger / unknown.
- Validates the abstraction under "many features, aging, custom
  symbology".

Each PR ships independently. PR-D2 and PR-D3 are sketched here
solely so reviewers can sanity-check that the PR-D1 surface supports
them.

---

## 9. Open questions

1. **`IMapHost.AddOverlayLayer` shape.** If the in-flight
   validation-overlay session (`303c3372`) chooses a different
   surface (separate `IOverlayHost`, `IMapHost.OverlayLayers`
   collection, etc.), this design follows whichever shape lands.
   Resolution: track the sibling session's final API, update the
   §4.4 stub.
2. **`DynamicSourceMetadata.PreferredPlane`.** Should
   `DynamicSourceMetadata` allow a source to override the default
   `DynamicArrows` plane assignment (e.g. a weather-contour source
   preferring `Information`)? Recommended yes via an optional
   `S98DisplayPlane? PreferredPlane { get; init; }`, defaulting to
   `DynamicArrows`. Resolution: decide before PR-D1 lands.
3. **Update coalescing.** Should the source contract require
   coalescing (e.g. raise `Changed` no more often than 1 Hz), or
   should the overlay debounce? Recommended overlay-debounce with
   a configurable rate. Resolution: PR-D1 ships a default 100 ms
   debounce in `DynamicSourceOverlayHost` and re-evaluates after
   PR-D3 measurements.
4. **Stub interface file.** Worth shipping a 50-line
   `src/EncDotNet.S100.Core/DynamicSources/IDynamicFeatureSource.cs`
   alongside this doc? Lean **no** — the doc carries the full surface
   and a stub risks being treated as a contract that drifts from the
   design. Reconsider only if review feedback asks.
5. **Per-feature dirty-tracking.** v1 rebuilds the entire
   `MemoryLayer` on each debounce flush. AIS at hundreds of targets
   may motivate per-feature `IFeature` reuse / dirty marking. Out
   of scope here; revisit after PR-D3 measurement.

---

## 10. Alternatives considered

### 10.1 Extend `IVectorSource` / `VectorPipeline` to support push

**Rejected.** The vector pipeline is snapshot-shaped (`IVectorSource`
returns a `Feature` enumeration; `VectorPipeline.Process` produces
`DrawingInstruction` lists for one full evaluation). Adding push
semantics would either fork the pipeline or contaminate the
snapshot contract. The Lua / XSLT engines downstream of
`VectorPipeline` are wired to Feature Catalogues and Portrayal
Catalogues; dynamic features have neither. Two cleanly-separated
pipelines is the right factoring.

### 10.2 Reuse `Vector.Feature` directly as `DynamicFeature`

**Rejected.** Geometry **enum** and coordinate convention **are**
reused (§6.2). The record itself is not, because of three semantic
mismatches enumerated in §5 Q2: identity (`long` vs stable string),
type semantics (FC-bound `FeatureType` vs opaque renderer-dispatch
`Kind`), and the absence of `LastUpdated` and motion fields on the
static record.

### 10.3 Parallel `DynamicGeometry` discriminated union

**Rejected.** Duplicates the vector pipeline's `GeometryType` enum
for no semantic gain and forces renderer authors to learn two
vocabularies for the same concept.

### 10.4 Source exposes `IDynamicFeatureRenderer Renderer { get; }`

**Rejected.** Forces `EncDotNet.S100.Core` to reference
`EncDotNet.S100.Renderers.Mapsui`, defeating the graphics-agnostic-
source goal. Headless / MCP consumers would no longer be able to
implement `IDynamicFeatureSource` without pulling in Mapsui.

### 10.5 Two-tier `IMapsuiDynamicFeatureSource : IDynamicFeatureSource`

**Rejected.** Works mechanically — `Core` defines the base interface
and `Renderers.Mapsui` defines the derived interface adding
`Renderer`. But every adapter ships two types (the core source and
the Mapsui wrapper), and cross-source renderer sharing is awkward —
each source instance binds its own renderer, so two AIS feeds
register two renderer instances. The DI keyed-service approach (§5
Q6) achieves the same separation with a single interface and natural
cross-source sharing.

### 10.6 Neutral display-primitive vocabulary in `Core`

**Rejected.** A renderer that returns an abstract type (which a
Mapsui adapter then converts to `IFeature` + `IStyle`) introduces a
third graphics language alongside Mapsui's and S-100's
`DrawingInstruction`. Reusing `DrawingInstruction` itself was
considered: rejected because it carries S-100 Part 9 portrayal
semantics (viewing groups, drawing priorities, scale denominators,
FC-resource-name references) that dynamic features have no business
inheriting.

### 10.7 Move both source and renderer into `Renderers.Mapsui`

**Rejected.** Kills the graphics-agnostic-source goal. Headless and
MCP consumers cannot implement `IDynamicFeatureSource` without a
Mapsui reference.

### 10.8 S-100 portrayal pipeline native (Lua / XSLT)

**Rejected.** Dynamic features have no Feature Catalogue, no
Portrayal Catalogue, no viewing groups, no scale denominators, no
catalogued type system. Wedging them into the Lua engine
(`S101LuaPortrayal` / `S131LuaPortrayal`) or the XSLT pipelines
(S-124 / S-125 / S-127 / etc.) would pollute the catalogue-driven
engines with non-spec semantics and double the engines'
maintenance surface.

### 10.9 Central `Kind`-keyed renderer registry across sources

**Deferred.** DI keyed services already provide registration. Within-
source dispatch (AIS cargo vs tanker vs unknown) is better handled
by a per-source `KindMatchingRenderer` composed inside the adapter's
single renderer registration. A global `Kind`-keyed registry could
re-enter the design if cross-source `Kind` sharing becomes a real
pattern, but the current design does not need it.

### 10.10 Leave everything in the viewer; no library abstraction

**Rejected.** Other library consumers — `tools/RenderS102`, a future
MCP server, headless renderers, integration test harnesses — lose
the ability to surface live features. The abstraction is small
(≈10 types across three assemblies) and pays for itself the first
time a non-viewer consumer wants to participate.
