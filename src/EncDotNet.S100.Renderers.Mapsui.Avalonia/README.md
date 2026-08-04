# EncDotNet.S100.Renderers.Mapsui.Avalonia

Optional Avalonia adapter for applications that host S-100 Mapsui layers in a
live `Mapsui.UI.Avalonia.MapControl`.

The base [`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/)
package remains UI-framework neutral. It owns layer creation,
`MapsuiLayerBands`, and `MapsuiMapNavigator`. This package adds only mechanics
that require Avalonia:

- explicit attachment to a live `CaptureSynchronizedMapControl`;
- UI-thread redraw invalidation;
- live-control and captured-image coordinate conversion;
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

`Attach` must run on Avalonia's UI thread. The returned adapter borrows the
control and map; disposing it detaches the adapter but does not dispose either
object. Use `CaptureSynchronizedMapControl` (or a subclass) rather than a plain
Mapsui `MapControl` so offscreen captures are serialized against live GPU
painting.

Coordinate conversion supports Mapsui's default `EPSG:3857` map CRS and returns
`null` for maps whose CRS requires a different projection.

Whole-control or whole-window capture is available through
`AvaloniaControlCapture.CapturePngAsync`. When the target contains a
`CaptureSynchronizedMapControl`, the same synchronization protects the
transitive map paint.
