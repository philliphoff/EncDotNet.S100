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
// 1. Compose a session over a bare map. AddS100 returns the disposable
//    IS100MapSession that owns all S-100 layers, processors, and rendering.
var map = new Map { CRS = "EPSG:3857" };
var session = map.AddS100(
    new ProjNetCrsTransformFactory(),                       // host supplies the CRS
    new S100MapsuiOptions { DatasetPipelineFactory = factory }); // enables LoadAsync

// 2. Bind the map to your Mapsui control and attach the framework adapter,
//    which converts pointer pixels to geographic picks and drives redraws.
mapControl.Map = map;
var adapter = AvaloniaMapsuiMapAdapter.Attach(mapControl);

// 3. Drive it. Navigation stays with Mapsui; the session adds S-100 operations.
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
| attach a session to an existing `Map`  | `map.AddS100(crsFactory, options)`                    |
| load / unload datasets                 | `session.Datasets.LoadAsync` / `session.RemoveDataset`|
| change palette (Day / Dusk / Night)    | `session.SetPresentationAsync(state.WithPalette(...))`|
| toggle a dataset                       | `session.SetVisible(id, …)`                           |
| zoom via normal Mapsui navigation      | `MapControl` gestures + `session.ZoomToDataset(id)`   |
| geographic pick + pointer adaptation   | `adapter.PickAtScreenAsync(session.Query, x, y)`      |
| highlight a pick                       | `S100PickHighlightLayer.Show(pick)`                   |
| show the cell extent when zoomed out   | `S100DatasetExtentIndicatorLayer.Show(...)`           |
| dispose everything                     | `session.Dispose()` / `adapter.Dispose()`             |

## Reading the code

| File                                                   | What it shows                                                                 |
| ------------------------------------------------------ | ----------------------------------------------------------------------------- |
| [`MainWindow.axaml.cs`](MainWindow.axaml.cs)           | The integration itself: compose, attach, drive, dispose. Start here.          |
| [`MainWindow.axaml`](MainWindow.axaml)                 | The toolbar and the reusable `CaptureSynchronizedMapControl`.                  |
| [`SampleS100Host.cs`](SampleS100Host.cs)               | Bootstrapping the `DatasetPipelineFactory` from the bundled catalogues.        |
| [`SmokeTest.cs`](SmokeTest.cs)                          | The same session driven headlessly (the `--smoke` path).                       |

## Wiring notes

- `Map.CRS` is set to `EPSG:3857`; the reusable renderer projects datasets to
  Web Mercator and the pick adapter converts pointer pixels back to WGS-84.
- The reusable assembly ships no CRS implementation, so the host supplies
  `ProjNetCrsTransformFactory` (`EncDotNet.S100.Crs.ProjNet`).
- `Datasets.LoadAsync` needs a `DatasetPipelineFactory`. `SampleS100Host` builds
  one by hand from the bundled catalogues — the same bootstrap the Viewer and
  `s100` CLI perform.
- The overlays (`S100DatasetExtentIndicatorLayer`, `S100PickHighlightLayer`) are
  entirely optional — add the ones you want to `Map.Layers` and drive them with
  `Show`/`Clear`. Omitting them changes nothing about dataset rendering.
- Async re-renders (e.g. a palette change) repaint the control only if the host
  sets the `S100Vector{Snapshot,Scene,Tile}Renderer.RequestRedraw` hooks — this
  sample points them at the adapter's redraw and clears them on close. This is a
  known rough edge tracked in issue #512 (a per-session redraw seam should
  replace the process-global statics).

> **Consuming this out of repo?** A real application would reference the
> published `EncDotNet.S100.*` NuGet packages instead of the project references
> this in-repo sample uses, but the code in `MainWindow` is identical.

## A note on package coupling (feeds issue #512 step 9)

To build that `DatasetPipelineFactory`, this sample must reference
`Datasets.Pipelines` (which transitively pulls in **every** S-1xx product) plus
`Portrayals`, `Specifications`, `Features`, and `Scripting.MoonSharp`. That
weight is exactly the coupling step 9 aims to reduce (per-product registration,
a leaner contracts package, and ideally a public one-call bundled-factory
convenience). This sample is the concrete, non-Viewer host that surfaces it.

The bundled `sample-cell.000` is the IHO S-101 test cell
`101AA00DS0008.000`, linked from `tests/datasets/` so there is a single source
of truth.
