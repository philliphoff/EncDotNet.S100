# AIS zoom-gated subscription

> Status: implemented<br>
> Specs touched: [PR-D3 AIS overlay (#142)](https://github.com/philliphoff/EncDotNet.S100/pull/142), [PR-D4 dynamic-source pick (#147)](https://github.com/philliphoff/EncDotNet.S100/pull/147)<br>
> Companion docs: [AIS dynamic feature source](ais-source.md), [Dynamic feature sources](dynamic-feature-source.md)

## Problem

PR-D3 wired the AIS overlay end-to-end. With the overlay enabled and an
aisstream.io API key in scope, the source subscribes **immediately on
viewer startup** with whatever bbox the persisted `InitialArea`
contains — which, for a fresh user, is `null` (i.e. the world). That
behaviour has three problems:

1. **Visual.** A globe-wide AIS feed paints thousands of vessel
   symbols on the cold-start map, almost none of which correspond to
   anywhere the user is looking.
2. **Quota.** aisstream.io is a free service; pulling the world feed
   when you only care about, say, the English Channel wastes their
   bandwidth and ours.
3. **Performance.** First-frame render time scales with the live
   vessel count.

The existing `AisDynamicFeatureSource.UpdateArea(BoundingBox?)` and
`AisStreamIoSubscription.TryUpdateArea` have been plumbed since PR-D3
but **are not called from the viewer today**. We want the AIS
subscription to (a) not start until the visible map is meaningfully
smaller than "the world", and (b) thereafter track the live viewport
so the wire bbox always matches what the user can see.

## Decisions

### Q1. Activation criterion — zoom level or viewport area?

**Decision: viewport bounding-box span in degrees.**

aisstream.io's spatial filter *is* a lat/lon bounding box, so a
"trip when both lat-span and lon-span are ≤ T" rule maps 1:1 to "the
filter we'd send wouldn't be insane." We use both spans (logical AND)
rather than area because area collapses an extremely-wide-and-thin
viewport into the same number as a square one, and the wide-and-thin
case still produces a globe-spanning longitude filter.

Default threshold: **50°**. That excludes the cold-start global view
(typically 360° × 170° on a fresh viewer) and admits any reasonable
regional view (e.g. North Sea ~10° × 6°, Mediterranean ~40° × 14°).

### Q2. Should the gate also drive the area filter?

**Decision: yes — the gate trip seeds the subscription with the live
viewport bbox, and subsequent viewport changes call `UpdateArea`
(debounced 250 ms).**

Subscribing to "the world" the moment the user zooms past the
threshold would just kick the original problem one click down the
road. Now that we have a viewport listener for the gate, it's
essentially free to keep feeding it into the existing
`AisDynamicFeatureSource.UpdateArea` plumbing.

The 250 ms debounce mirrors `DynamicSourceOverlayHost`'s
`_coalesceWindow`. It keeps a single pan or zoom from triggering
hundreds of resubscribes against aisstream.io.

### Q3. Where the setting lives

**Decision: `AisOverlaySettings.ActivationViewportSpanDegrees`,
nullable `double`, default `50.0`.**

A single number is easy to explain in a tooltip and easy to test.
Degrees is the right unit because that's what the wire format
already takes. A `null` value means "no gate" (subscribe immediately
— the legacy PR-D3 behaviour) for backward compatibility.

Validation: any value `<= 0` is normalised to `null` by the settings
view-model so users can't accidentally configure a gate that never
opens.

### Q4. UI

**Decision: numeric input + tooltip + helper text in the AIS section
of the Settings dialog. Defer the layer-stack "waiting" indicator.**

New `Resources/Strings.resx` keys:

| Key | Purpose |
|---|---|
| `Settings_AisActivationSpan` | Field label |
| `Settings_AisActivationSpanTooltip` | Hover help |
| `Settings_AisActivationSpanHint` | Sub-label clarifying default + null semantics |

A tiny "waiting to activate — zoom in" indicator on the layer-stack
row would be a nice UX bonus, but the row currently doesn't surface
`DynamicSourceMetadata.Description` at all; threading that through is
a separate change. Defer.

### Q5. Lifecycle — start/stop or always-allocated?

**Decision: deferred decorator wrapping `IDynamicFeatureSource`.**

```
DeferredAisFeatureSource (viewer-side)
    │  while gate closed:
    │     CurrentFeatures = []
    │     Changed not raised
    │     no subscription, no driver, no socket
    │
    │  on first viewport with both spans <= T:
    │     factory(seedBbox) -> AisDynamicFeatureSource
    │     hook inner.Changed -> forward
    │
    │  on every subsequent viewport (debounced):
    │     inner.UpdateArea(bbox)
    │
    └─ Dispose: dispose inner if activated; cancel debounce timer
```

This is the least-invasive option. The real `AisDynamicFeatureSource`
and the aisstream.io driver stay completely unaware of the gate; the
decorator is a thin viewer-side wrapper that lives next to
`AisOverlayServiceCollectionExtensions`.

### Q6. What do we do once the gate has tripped?

**Decision: one-shot activation. Once the source is constructed, it
stays constructed for the lifetime of the viewer process — even if
the user zooms back out.**

Tearing down on zoom-out has three drawbacks: (a) more chatty against
aisstream.io's connection limits, (b) confuses users who wonder why
their vessels disappeared, and (c) the corner cases (does the
threshold use hysteresis? what if the user is mid-pan?) are non-trivial
for a marginal benefit. Document the one-shot behaviour explicitly so
no one is surprised.

Each viewer launch starts gated again — there is no persisted "this
session has activated" flag.

## Architecture

### New types (viewer-side)

| Type | Purpose |
|---|---|
| `MapViewportSnapshot` (record) | Immutable lat/lon bbox of the current viewport. |
| `IMapViewportNotifier` (interface) | Publishes `event ViewportChanged` and exposes `Current`. |
| `MapViewportNotifier` (class) | Singleton DI registration. Hooks `Mapsui.Navigator.ViewportChanged`, projects EPSG:3857 → EPSG:4326 via `SphericalMercator.ToLonLat`, and re-fires. |
| `DeferredAisFeatureSource` (class) | The decorator described above. Implements `IDynamicFeatureSource` + `IAsyncDisposable`. |

### Modified types

| Type | Change |
|---|---|
| `AisOverlaySettings` | New `double? ActivationViewportSpanDegrees`, default 50.0. |
| `AisOverlayServiceCollectionExtensions.BuildSource` | When the threshold is non-null, returns `new DeferredAisFeatureSource(threshold, () => realSource, viewportNotifier)`. |
| `App.axaml.cs` | Registers `MapViewportNotifier` as a singleton. |
| `MainWindow.axaml.cs` | Once the `Mapsui.Navigator` exists, calls `notifier.Bind(navigator)` so the notifier starts publishing. |
| `SettingsViewModel` | New `AisActivationViewportSpanDegrees` property + persistence. |
| `SettingsView.axaml` | New labelled numeric input + tooltip + hint. |
| `Resources/Strings.resx` + `.cs` | Three new keys. |

### Threading

- `Mapsui.Navigator.ViewportChanged` fires on the UI thread.
- `MapViewportNotifier.ViewportChanged` therefore also fires on the UI
  thread (it's a thin re-publisher).
- `DeferredAisFeatureSource.OnViewportChanged` runs on the UI thread.
  Activation does in-process work only (constructor +
  `messageSource.Subscribe`); the driver's WebSocket connect happens
  lazily inside the driver itself.
- The inner `AisDynamicFeatureSource.Changed` events arrive on the
  websocket reader thread — we forward them verbatim. The downstream
  `DynamicSourceOverlayHost` already marshals to UI.
- The debounce timer uses `CancellationTokenSource` + `Task.Delay`;
  the trailing call lands on a worker thread but only invokes
  `inner.UpdateArea`, which the driver makes thread-safe via its own
  internal lock.

Activation uses a cheap mutex (`object _activationLock`) plus a
`volatile` field for the inner reference. `volatile`-read on the hot
path keeps `CurrentFeatures` allocation-free.

### Test surface

| Project | What it covers |
|---|---|
| `AisOverlaySettingsTests` | Round-trip the new property; default = 50.0; null deserialises cleanly. |
| `DeferredAisFeatureSourceTests` | Gate closed → empty features, no `Changed`. Above threshold → still gated. ≤ threshold → factory called once with seed bbox. Subsequent viewports → debounced `UpdateArea`. Activation is one-shot (zoom-out doesn't deactivate). `DisposeAsync` disposes inner. Uses fake notifier + spy factory + fake `IDynamicFeatureSource`. |
| `MapViewportNotifierTests` | EPSG:3857 → EPSG:4326 projection round-trips reasonably; `Current` is `null` until bound. |
| `SettingsViewModelTests` | Set/get round-trip; ≤ 0 → null; getter falls back to default when settings absent. |
| `AisOverlayFactoryTests` | Already exercises Disabled / real source paths; new test covers the deferred-wrapper path. |

## Out of scope

- Tearing down on zoom-out (Q6 — one-shot).
- Layer-stack panel "waiting to activate" indicator (Q4 — defer).
- Generalising the gate to other dynamic sources (the decorator
  stays AIS-named; if a second source ever needs the same trick we
  can lift the abstraction then).
- Persisting "AIS already activated this session" across viewer
  restarts.
