# S-100 map host sample (`Map.AddS100`)

This sample is a small Avalonia and Mapsui desktop app that shows S-100 data
on its own map. It adds S-100 rendering to a stock Mapsui `MapControl` through
the public `AddS100(...)` extension and the `IS100MapSession` it returns. It
has its own window, controls and pointer handling, and doesn't reference
SoundCharts (`EncDotNet.S100.Viewer`).

Use it as a starting point for adding S-100 layers to your own Mapsui
application.

## Prerequisites

- The .NET 10 SDK.

The sample includes an S-101 cell, `sample-cell.000`. It's the IHO S-101 test
cell `101AA00DS0008.000`, linked from `tests/datasets/`.

## Run the sample

From the repository root:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.MapHost
```

In the window:

1. Choose **Load cell** to load and render the bundled S-101 cell. The map
   zooms to it.
2. Choose **Day**, **Dusk** or **Night** to change the palette, and clear
   **Visible** to hide the cell.
3. Drag to pan and use the mouse wheel to zoom.
4. Click the map to pick features. The topmost feature is outlined, and the
   status bar names it.
5. Choose **Zoom to cell** to frame the cell again, or **Unload** to remove it.

### Run the headless check

The `--smoke` option drives the same session without a window, so it runs in
CI:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.MapHost -- --smoke
```

It attaches a session to a bare `Map`, loads the cell, zooms to it, picks at
the cell's centre and removes the cell. It ends with:

```text
PASS: reusable S-100 session loaded, framed, picked, and tore down headlessly.
```

On failure it prints a `FAIL:` line to standard error and exits with code `1`.

## How it works

The integration is in the
[`MainWindow`](https://github.com/philliphoff/EncDotNet.S100/blob/main/samples/EncDotNet.S100.Samples.MapHost/MainWindow.axaml.cs)
constructor and event handlers. In outline:

```csharp
// Create a processor factory with the bundled catalogues for every product.
// It holds catalogue caches, so dispose it with the session.
using var factory = BundledDatasetProcessorFactory.Create();

// Attach a session to the control's map. AddS100 returns the session, which
// owns the S-100 layers, processors and rendering, and an adapter for pointer
// picks and snapshots. It also makes background renders repaint the control.
var map = new Map { CRS = "EPSG:3857" };
mapControl.Map = map;
var (session, adapter) = mapControl.AddS100(new S100MapsuiOptions
{
    CrsTransformFactory = new ProjNetCrsTransformFactory(),
    DatasetPipelineFactory = factory,
});

// Drive the session. Mapsui still handles navigation.
var id = await session.Datasets.LoadAsync("cell.000");
session.ZoomToDataset(id);
await session.SetPresentationAsync(MapPresentationState.Default.WithPalette(PaletteType.Night));
var picks = await adapter.PickAtScreenAsync(session.Query, x, y);

// Release every processor, layer, subscription and cache.
session.Dispose();
```

Each button and pointer handler in the code-behind calls one part of the API,
with comments that explain why the API works that way:

| Task | API |
|---|---|
| Attach a session to an existing map | `mapControl.AddS100(options)`, or `map.AddS100(options)` for a bare `Map` |
| Load or unload a dataset | `session.Datasets.LoadAsync(path)`, `session.RemoveDataset(id)` |
| Change the palette | `session.SetPresentationAsync(state.WithPalette(...))` |
| Show or hide a dataset | `session.SetVisible(id, visible)` |
| Zoom to a dataset | `session.ZoomToDataset(id)`, alongside normal `MapControl` gestures |
| Pick the features under the pointer | `adapter.PickAtScreenAsync(session.Query, x, y)` |
| Pick at a position, without a UI | `session.Query.PickAsync(new GeographicPickQuery { ... })` |
| Add a host overlay layer | `session.Layers.AddOverlayLayer(layer)` |
| Outline a picked feature | `S100PickHighlightLayer.Show(pick)` |
| Show a cell's extent when it's zoomed out of view | `S100DatasetExtentIndicatorLayer.Show(...)` |
| Clean up | `adapter.Dispose()`, `session.Dispose()` |

| File | What it shows |
|---|---|
| [`MainWindow.axaml.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/samples/EncDotNet.S100.Samples.MapHost/MainWindow.axaml.cs) | The integration: create, attach, drive and dispose. Start here. |
| [`MainWindow.axaml`](https://github.com/philliphoff/EncDotNet.S100/blob/main/samples/EncDotNet.S100.Samples.MapHost/MainWindow.axaml) | The toolbar and a stock Mapsui `MapControl`. `AddS100` doesn't need a special subclass. |
| [`SmokeTest.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/samples/EncDotNet.S100.Samples.MapHost/SmokeTest.cs) | The same session without a window, for `--smoke`. |

### Wiring notes

- **Projection.** The map's `CRS` is `EPSG:3857`. The renderer projects
  datasets to Web Mercator, and the pick adapter converts pointer pixels back to
  WGS-84.
- **CRS transforms.** The rendering assembly has no CRS implementation, so the
  host supplies one: `ProjNetCrsTransformFactory` from
  `EncDotNet.S100.Crs.ProjNet`.
- **Processor factory.** `Datasets.LoadAsync` needs an
  `IDatasetProcessorFactory`. `BundledDatasetProcessorFactory.Create()`, from
  the `EncDotNet.S100` package, returns one set up with the bundled catalogues
  for every product, so you don't set up catalogue managers, the Lua engine,
  the CRS factory or the product registry yourself. Dispose it with the
  session.
- **Overlays.** `S100DatasetExtentIndicatorLayer` and `S100PickHighlightLayer`
  are optional. Add the ones you want with `session.Layers.AddOverlayLayer(...)`,
  which places them above the dataset layers, and drive them with `Show` and
  `Clear`. Leaving them out doesn't change dataset rendering.
- **Repaints.** When a background render finishes, such as after a palette
  change, the session signals a redraw. `mapControl.AddS100` turns that signal
  into a `RefreshGraphics` call on the UI thread. With a bare `Map`, supply
  your own `S100MapsuiOptions.RedrawMarshal`; without one, the session redraws
  inline, which is fine headlessly.

### Use `S100MapControl` instead

This sample attaches the session by hand, with explicit fields and disposal,
to show each step. `S100MapControl` is a `MapControl` subclass that creates and
owns the session for you: setup is one `Configure(options)` call and cleanup is
one `Dispose()`. See
[One-call control](../../src/EncDotNet.S100.Renderers.Mapsui.Avalonia/README.md#one-call-control-s100mapcontrol)
in the `EncDotNet.S100.Renderers.Mapsui.Avalonia` README.

### Package references

The sample uses project references so that it builds in this repository. In
your own app, reference the published packages; the code in `MainWindow` stays
the same. It needs:

- `EncDotNet.S100.Renderers.Mapsui`
- `EncDotNet.S100.Renderers.Mapsui.Avalonia`
- `EncDotNet.S100.Crs.ProjNet`
- `EncDotNet.S100`

`AddS100` itself doesn't depend on any S-100 product. It takes an
`IDatasetProcessorFactory` from `EncDotNet.S100.Core`. `EncDotNet.S100` brings in
every product through `BundledDatasetProcessorFactory`. For a smaller app, build
your own `IDatasetProcessorFactory`, or a `DatasetPipelineFactory` with a
smaller `S100ProductRegistry`, and reference only the product packages you
need.
