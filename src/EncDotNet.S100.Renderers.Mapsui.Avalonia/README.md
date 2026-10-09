# EncDotNet.S100.Renderers.Mapsui.Avalonia

`EncDotNet.S100.Renderers.Mapsui.Avalonia` connects an S-100 map session to a
live Avalonia `Mapsui.UI.Avalonia.MapControl`. Reference it when your
application shows S-100 data in an Avalonia window. It adds what needs
Avalonia: a ready-made map control, redraws on the UI thread, pointer picking,
coordinate conversion, and PNG capture.

The base package,
[`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/README.md),
stays independent of any UI framework. It owns the session, layer creation,
`MapsuiLayerBands` and `MapsuiMapNavigator`. This package doesn't own datasets,
processors, S-98 composition, presentation state, automatic framing or other
host UX policy.

For a complete Avalonia host, see the
[MapHost sample](../../samples/EncDotNet.S100.Samples.MapHost/README.md).

## Install

```bash
dotnet add package EncDotNet.S100.Renderers.Mapsui.Avalonia
dotnet add package EncDotNet.S100.Crs.ProjNet
dotnet add package EncDotNet.S100
```

`EncDotNet.S100.Crs.ProjNet` supplies the CRS transforms the session needs.
`EncDotNet.S100` supplies `BundledDatasetProcessorFactory`, which lets the
session load datasets from a path. If you build dataset processors yourself, you
don't need it.

## One-call control (`S100MapControl`)

`S100MapControl` is a `MapControl` that attaches a session to itself and owns
it. Add it to your XAML, then call `Configure` once. `Configure` creates an
`EPSG:3857` map, attaches the session, and exposes it as `Session` with the
adapter as `Adapter`. Disposing the control disposes both.

```xml
<s100:S100MapControl x:Name="MapView"
     xmlns:s100="clr-namespace:EncDotNet.S100.Renderers.Mapsui.Avalonia;assembly=EncDotNet.S100.Renderers.Mapsui.Avalonia" />
```

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Renderers.Mapsui;

public MainWindow()
{
    InitializeComponent();

    MapView.Configure(new S100MapsuiOptions
    {
        CrsTransformFactory = new ProjNetCrsTransformFactory(),
        DatasetPipelineFactory = BundledDatasetProcessorFactory.Create(),
    });

    MapView.PointerReleased += async (_, e) =>
    {
        try
        {
            var p = e.GetPosition(MapView);
            var picks = await MapView.PickAsync(p.X, p.Y);
            // picks[0] is the topmost feature under the pointer.
        }
        catch (Exception ex)
        {
            // Log or show the error. An exception that escapes an async
            // event handler is unhandled.
        }
    };
}

protected override void OnClosed(EventArgs e)
{
    MapView.Dispose(); // Disposes the session and the adapter.
    base.OnClosed(e);
}
```

Call `MapView.Session.Datasets.LoadAsync(path)` to load a dataset; see
[Add S-100 data to a map](../EncDotNet.S100.Renderers.Mapsui/README.md#add-s-100-data-to-a-map)
and [Present datasets](../EncDotNet.S100.Renderers.Mapsui/README.md#present-datasets)
in the base package.

`Configure` must run on Avalonia's UI thread, and the control's map must use
`EPSG:3857`. Disposing the control doesn't dispose collaborators you supplied
on the options, such as a shared processor owner.

`S100MapControl` derives from `CaptureSynchronizedMapControl`, so its image
captures don't race the live paint; see [Capture images](#capture-images).

## Attach to your own map control

Use `mapControl.AddS100(options)` instead of `S100MapControl` when you need to
inject shared collaborators, keep your own paint diagnostics, or otherwise wire
the control yourself. It works on any Mapsui `MapControl` that already has a
map. It returns the session and an attached `AvaloniaMapsuiMapAdapter`, and you
dispose both. If you don't set `S100MapsuiOptions.RedrawMarshal`, it supplies
one that redraws on the UI thread.

```csharp
var (session, adapter) = mapControl.AddS100(new S100MapsuiOptions
{
    CrsTransformFactory = new ProjNetCrsTransformFactory(),
    DatasetPipelineFactory = processorFactory,
});
```

To attach only the adapter, without a session, call
`AvaloniaMapsuiMapAdapter.Attach(mapControl)`:

```csharp
var map = new Mapsui.Map();
var mapControl = new CaptureSynchronizedMapControl { Map = map };

using var adapter = AvaloniaMapsuiMapAdapter.Attach(mapControl);
adapter.RequestRedraw();
```

`Attach` and `AddS100` must run on Avalonia's UI thread. The adapter borrows the
control and the map: disposing it detaches the adapter but disposes neither.

## Pick features under the pointer

`PickAtScreenAsync` is the pointer-based counterpart of the base package's
[`session.Query.PickAsync`](../EncDotNet.S100.Renderers.Mapsui/README.md#pick-features-at-a-location).
It reads the live viewport on the UI thread to convert the control pixel to
WGS-84 and to capture the current resolution, so cells drawn out of scale at
this zoom are left out. It then runs the pick off the UI thread.

```csharp
// For example, in a PointerPressed handler, with the pointer position
// relative to the map control:
var picks = await adapter.PickAtScreenAsync(session.Query, point.X, point.Y);
// picks[0] is the topmost feature or coverage sample under the pointer.
```

`radiusMeters` (default 50 m) sets the tolerance for points and curves, and
`maxResults` limits the number of picks returned. The method returns an empty
list without querying when the pixel isn't finite, the viewport isn't laid out,
or the map CRS isn't supported. `S100MapControl.PickAsync` calls this method on
the control's own session.

Pointer gestures, result panels and selection are up to your application.

## Convert coordinates

- `TryScreenToWgs84(x, y)` converts a live control pixel to a `GeoPosition`.
- `TryImagePixelToWgs84(x, y, width, height)` converts a pixel in an image from
  `RenderCurrentViewToPngAsync`, using the same extent, fit and rotation as the
  capture.

Both support Mapsui's default `EPSG:3857` map CRS and return `null` for a map
in any other CRS.

## Capture images

`adapter.RenderCurrentViewToPngAsync(width, height, pixelDensity)` renders the
current view to PNG bytes without moving the live map:

```csharp
byte[]? png = await adapter.RenderCurrentViewToPngAsync(1280, 800, 1.0);
```

Live painting and capture share Skia GPU resources. Attach a
`CaptureSynchronizedMapControl`, or use `S100MapControl`, when you capture while
the map might be repainting; the capture then waits for the live paint instead
of racing it. Over a plain `MapControl`, the capture is best effort: on an idle
map it renders the current view, but during active repainting the image can
occasionally be torn or incomplete. If you never capture, a plain control is
enough.

`AvaloniaControlCapture.CapturePngAsync(control)` captures a whole control or
window at 96 dpi; an overload takes a scale, such as `2` for a HiDPI display.
The capture goes through the live layers inside
`S100VectorTileRenderer.BeginOffscreenRender()`, so it reuses the tiles the live
view has cached and doesn't schedule tiles at the capture's scale.

> [!IMPORTANT]
> `AvaloniaControlCapture` throws `InvalidOperationException` if the captured
> control contains a Mapsui map control that doesn't derive from
> `CaptureSynchronizedMapControl`.
