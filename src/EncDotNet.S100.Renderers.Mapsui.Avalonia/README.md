# EncDotNet.S100.Renderers.Mapsui.Avalonia

Optional Avalonia adapter for applications that host S-100 Mapsui layers in a
live `Mapsui.UI.Avalonia.MapControl`.

The base [`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/)
package remains UI-framework neutral. It owns layer creation,
`MapsuiLayerBands`, and `MapsuiMapNavigator`. This package adds only mechanics
that require Avalonia:

- explicit attachment to any live `Mapsui.UI.Avalonia.MapControl`;
- UI-thread redraw invalidation;
- live-control and captured-image coordinate conversion;
- pointer (screen-pixel) picking against a session's `IS100MapQuery`;
- current-view PNG rendering without mutating the live navigator;
- Avalonia `Control` PNG capture; and
- capture/live-paint synchronization for shared Skia GPU resources.

It does not own datasets, processors, S-98 composition, presentation state,
automatic framing, or host UX policy.

## Usage

```csharp
var map = new Mapsui.Map();
var mapControl = new CaptureSynchronizedMapControl { Map = map };

using var adapter = AvaloniaMapsuiMapAdapter.Attach(mapControl);
adapter.RequestRedraw();

var position = adapter.TryScreenToWgs84(x, y);
var png = await adapter.RenderCurrentViewToPngAsync(1280, 800, 1.0);
```

`Attach` (and `mapControl.AddS100(...)`) accept any Mapsui
`MapControl` and must run on Avalonia's UI thread. The returned adapter borrows
the control and map; disposing it detaches the adapter but does not dispose
either object.

`CaptureSynchronizedMapControl` is an optional `MapControl` subclass: attach it
(rather than a plain control) only when the host renders the current view to an
image — `RenderCurrentViewToPngAsync` — under a live paint and wants that capture
serialized against painting over the shared Skia GPU resources. Over a plain
control the capture still produces a correct image but runs best-effort
(unsynchronized). Hosts that never capture — most embeddings — need no subclass.

Coordinate conversion supports Mapsui's default `EPSG:3857` map CRS and returns
`null` for maps whose CRS requires a different projection.

### Pointer picking

`PickAtScreenAsync` is the UI-side counterpart to the base package's
[`session.Query.PickAsync`](../EncDotNet.S100.Renderers.Mapsui/README.md#picking).
It reads the live viewport on the UI thread to convert a control pixel to
WGS-84 and to capture the current resolution — so the session drops cells that
are scaled out at this zoom — then runs the pick off the UI thread:

```csharp
using var s100 = map.AddS100(options);
using var adapter = AvaloniaMapsuiMapAdapter.Attach(mapControl);

// e.g. from a PointerPressed handler, with the pointer position in the control
var picks = await adapter.PickAtScreenAsync(s100.Query, point.X, point.Y);
// picks[0] is the topmost feature/coverage sample under the pointer
```

Pointer gestures, hit panels, and selection remain the host's responsibility.
It returns an empty list (without querying) for a non-finite pixel, an
unlaid-out viewport, or an unsupported map CRS. `radiusMeters` (default 50 m)
sets the point/curve tolerance and `maxResults` caps the topmost picks.

Whole-control or whole-window capture is available through
`AvaloniaControlCapture.CapturePngAsync`. When the target contains a
`CaptureSynchronizedMapControl`, the same synchronization protects the
transitive map paint.
