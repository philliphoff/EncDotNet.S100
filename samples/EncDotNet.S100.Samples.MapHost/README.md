# S-100 Map Host sample (`Map.AddS100`)

A minimal **Avalonia + Mapsui desktop app** that embeds the reusable S-100
rendering extension in a host that is *not* the bundled Viewer. It is the
architectural fitness test for [issue #512](https://github.com/philliphoff/EncDotNet.S100/issues/512):
it proves another UI application can adopt S-100 as a managed layer subsystem
through the public `Map.AddS100(...)` → `IS100MapSession` API, with its own
window, control, and interaction — **without referencing
`EncDotNet.S100.Viewer`**.

It is also the *first application-level caller of `Map.AddS100`*: the in-repo
Viewer composes the lower-level `MapsuiMapSession` by hand, so until this sample
the public extension had only test coverage.

## The integration in three steps

The whole embedding is the three steps in [`MainWindow`](MainWindow.axaml.cs)'s
constructor. In essence:

```csharp
// 0. One call gives you an IDatasetProcessorFactory seeded with the official
//    bundled catalogues for every product (from the EncDotNet.S100 package).
//    It owns long-lived catalogue caches, so dispose it with the session.
using var factory = BundledDatasetProcessorFactory.Create();

// 1. Attach a session to the control's map AND the Avalonia adapter in one call.
//    mapControl.AddS100 returns the disposable IS100MapSession (owns all S-100
//    layers, processors, and rendering) plus the adapter (pointer-pixel picks,
//    snapshots), and wires a UI-thread redraw marshal so the background renderers
//    repaint the live control automatically — no process-global hooks.
var map = new Map { CRS = "EPSG:3857" };
mapControl.Map = map;
var (session, adapter) = mapControl.AddS100(new S100MapsuiOptions
{
    CrsTransformFactory = new ProjNetCrsTransformFactory(),  // host supplies the CRS
    DatasetPipelineFactory = factory,                        // enables LoadAsync
});

// 2. Drive it. Navigation stays with Mapsui; the session adds S-100 operations.
var id = await session.Datasets.LoadAsync("cell.000");
session.ZoomToDataset(id);
await session.SetPresentationAsync(MapPresentationState.Default.WithPalette(PaletteType.Night));
var picks = await adapter.PickAtScreenAsync(session.Query, x, y);

session.Dispose();   // releases every processor, layer, subscription, and cache
```

Each button and pointer handler in the code-behind is a worked example of one of
these calls, with comments explaining *why* the API is shaped that way.

## Run it

Desktop GUI:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.MapHost
```

Click **Load cell** to render the bundled S-101 cell, use the palette buttons
and the **Visible** toggle, drag/wheel to pan and zoom, and **click the map** to
pick features (the topmost hit is outlined by the reusable pick-highlight
overlay).

Headless self-check (no window; suitable for CI):

```bash
dotnet run --project samples/EncDotNet.S100.Samples.MapHost -- --smoke
```

The `--smoke` path drives the same reusable session — attach, `LoadAsync`,
`ZoomToDataset`, `Query.PickAsync`, dispose — and exits non-zero on failure.

## What it demonstrates

Every toolbar control and pointer gesture maps onto the reusable API surface:

| Fitness capability (issue #512)        | API used                                              |
| -------------------------------------- | ----------------------------------------------------- |
| attach a session to an existing `Map`  | `map.AddS100(options)`                                |
| load / unload datasets                 | `session.Datasets.LoadAsync` / `session.RemoveDataset`|
| change palette (Day / Dusk / Night)    | `session.SetPresentationAsync(state.WithPalette(...))`|
| toggle a dataset                       | `session.SetVisible(id, …)`                           |
| zoom via normal Mapsui navigation      | `MapControl` gestures + `session.ZoomToDataset(id)`   |
| geographic pick + pointer adaptation   | `adapter.PickAtScreenAsync(session.Query, x, y)`      |
| attach a host overlay layer            | `session.Layers.AddOverlayLayer(layer)`               |
| highlight a pick                       | `S100PickHighlightLayer.Show(pick)`                   |
| show the cell extent when zoomed out   | `S100DatasetExtentIndicatorLayer.Show(...)`           |
| dispose everything                     | `session.Dispose()` / `adapter.Dispose()`             |

## Reading the code

| File                                                   | What it shows                                                                 |
| ------------------------------------------------------ | ----------------------------------------------------------------------------- |
| [`MainWindow.axaml.cs`](MainWindow.axaml.cs)           | The integration itself: compose, attach, drive, dispose. Start here.          |
| [`MainWindow.axaml`](MainWindow.axaml)                 | The toolbar and a stock Mapsui `MapControl` (`AddS100` needs no special subclass). |
| [`SmokeTest.cs`](SmokeTest.cs)                          | The same session driven headlessly (the `--smoke` path).                       |

## Wiring notes

- `Map.CRS` is set to `EPSG:3857`; the reusable renderer projects datasets to
  Web Mercator and the pick adapter converts pointer pixels back to WGS-84.
- The reusable assembly ships no CRS implementation, so the host supplies
  `ProjNetCrsTransformFactory` (`EncDotNet.S100.Crs.ProjNet`).
- `Datasets.LoadAsync` needs an `IDatasetProcessorFactory`.
  `BundledDatasetProcessorFactory.Create()` (from the `EncDotNet.S100`
  convenience package) returns one seeded with the bundled catalogues for every
  product in a single call — no hand-wiring of the portrayal/feature catalogue
  managers, Lua engine, CRS factory, or product registry. Dispose it with the
  session.
- The overlays (`S100DatasetExtentIndicatorLayer`, `S100PickHighlightLayer`) are
  entirely optional — attach the ones you want through
  `session.Layers.AddOverlayLayer(...)` (the session's host-facing overlay band,
  above the dataset layers) and drive them with `Show`/`Clear`. Omitting them
  changes nothing about dataset rendering.
- Async re-renders (e.g. a palette change) repaint the control automatically: the
  background cached / scene / tile renderers signal completion through a
  per-session redraw sink the session stamps onto each dataset layer, and
  `mapControl.AddS100` wires that sink to a UI-thread `RefreshGraphics`. A host on
  a bare `Map` can supply its own `S100MapsuiOptions.RedrawMarshal`; omitting it
  redraws inline (fine for headless). This replaced the former process-global
  `RequestRedraw` statics (issue #512).

> **Consuming this out of repo?** A real application would reference the
> published `EncDotNet.S100.*` NuGet packages instead of the project references
> this in-repo sample uses, but the code in `MainWindow` is identical.

## A note on package coupling (issue #512 step 9)

`Map.AddS100` no longer references any S-100 product: it takes an
`IDatasetProcessorFactory` (in `EncDotNet.S100.Core`), so the reusable Mapsui
extension is product-free. This sample therefore references only
`Renderers.Mapsui`, `Renderers.Mapsui.Avalonia`, `Crs.ProjNet`, and the
`EncDotNet.S100` convenience package — the earlier hand-bootstrap that pulled in
`Datasets.Pipelines`, `Portrayals`, `Specifications`, `Features`, and
`Scripting.MoonSharp` collapsed to a single `BundledDatasetProcessorFactory.Create()`.

The convenience factory still pulls the products in transitively — that is the
batteries-included trade-off a host opts into by using it. A host that wants a
smaller footprint can instead build its own `IDatasetProcessorFactory` (or a
`DatasetPipelineFactory` with a subset `S100ProductRegistry`) and reference only
the products it needs.

The bundled `sample-cell.000` is the IHO S-101 test cell
`101AA00DS0008.000`, linked from `tests/datasets/` so there is a single source
of truth.
