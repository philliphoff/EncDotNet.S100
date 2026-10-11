# Add S-100 data to a Mapsui app

This guide adds S-100 charts to an Avalonia app that uses
[Mapsui](https://mapsui.com/): a map control that loads a dataset, frames it,
and reports the features under a click. The same session API works with other
Mapsui hosts; see [Use another Mapsui host](#use-another-mapsui-host).

## Prerequisites

- An Avalonia 12 app targeting .NET 8 or later.
- A dataset file, such as an S-101 cell (`.000`) or an S-102 file (`.h5`). See
  [Get sample data](getting-started.md#get-sample-data).

## Install the packages

```bash
dotnet add package EncDotNet.S100.Renderers.Mapsui.Avalonia
dotnet add package EncDotNet.S100.Crs.ProjNet
dotnet add package EncDotNet.S100
```

| Package | Provides |
|---|---|
| `EncDotNet.S100.Renderers.Mapsui.Avalonia` | `S100MapControl`, and the Mapsui session package it builds on |
| `EncDotNet.S100.Crs.ProjNet` | The coordinate transforms the session needs |
| `EncDotNet.S100` | `BundledDatasetProcessorFactory`, which reads and portrays datasets with the bundled catalogues |

## Add the map control

Add `S100MapControl` to your window:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:s100="clr-namespace:EncDotNet.S100.Renderers.Mapsui.Avalonia;assembly=EncDotNet.S100.Renderers.Mapsui.Avalonia"
        x:Class="MyApp.MainWindow">
  <s100:S100MapControl x:Name="MapView" />
</Window>
```

## Load a dataset and pick features

In the window's code-behind, configure the control once, load a dataset after
the window opens, and pick on click:

```csharp
using Avalonia.Controls;
using Avalonia.Input;
using EncDotNet.S100;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Renderers.Mapsui;

namespace MyApp;

public partial class MainWindow : Window
{
    private readonly BundledDatasetProcessorFactory _processorFactory =
        BundledDatasetProcessorFactory.Create();

    public MainWindow()
    {
        InitializeComponent();

        MapView.Configure(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = _processorFactory,
        });

        Opened += async (_, _) =>
        {
            var id = await MapView.Session.Datasets.LoadAsync("path/to/cell.000");
            MapView.Session.ZoomToDataset(id);
        };

        MapView.PointerReleased += OnMapPointerReleased;
    }

    private async void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        try
        {
            var point = e.GetPosition(MapView);
            var picks = await MapView.PickAsync(point.X, point.Y);
            if (picks.Count > 0)
                Title = picks[0].Info.FeatureTypeName ?? picks[0].Info.FeatureType;
        }
        catch (Exception ex)
        {
            // An exception that escapes an async event handler is unhandled.
            Title = ex.Message;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        MapView.Dispose();          // Disposes the session and every dataset in it.
        _processorFactory.Dispose();
        base.OnClosed(e);
    }
}
```

Run the app. The map shows the dataset, framed to its extent. Drag to pan,
scroll to zoom, and click a feature to see its type in the title bar.

- `Configure` creates an `EPSG:3857` map, attaches an S-100 session to it, and
  exposes the session as `MapView.Session`. Call it once, on the UI thread.
- `LoadAsync` detects the product from the file, portrays it and adds it to the
  map. An S-101 base cell also picks up its update files (`.001`, `.002`, …).
  The first render of an S-101 cell runs its Lua portrayal, so it takes a
  moment.
- `PickAsync` returns the features under the pointer, topmost first.

## Change the display

The session applies display settings to every dataset it holds. Keep the
current settings in a field; `MapPresentationState` is immutable, so each change
makes a copy:

```csharp
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;

private MapPresentationState _presentation = MapPresentationState.Default;

private async Task UseNightPaletteAsync()
{
    _presentation = _presentation.WithPalette(PaletteType.Night);
    await MapView.Session.SetPresentationAsync(_presentation);
}
```

`WithSymbolScale` and `WithTextScale` work the same way.

## Show a time step

For time-varying products such as S-104 water levels and S-111 currents, set
the map time. The session re-renders the time-aware datasets for it:

```csharp
await MapView.Session.SetTimeAsync(new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));
```

## Show, hide and reorder datasets

Use the `MapDatasetId` that `LoadAsync` returned:

```csharp
MapView.Session.SetVisible(id, isVisible: false);
MapView.Session.SetOpacity(id, 0.5);
MapView.Session.SetOrder(new[] { bathymetryId, chartId }); // bottom to top
```

Products are layered according to S-98 interoperability, so an order you set
applies within those rules rather than replacing them.

## Use another Mapsui host

The session itself doesn't depend on Avalonia. Reference
`EncDotNet.S100.Renderers.Mapsui` instead of the Avalonia package, and attach a
session to any `Mapsui.Map` with `AddS100`:

```csharp
using EncDotNet.S100;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;

using var processorFactory = BundledDatasetProcessorFactory.Create();
var map = new Map { CRS = "EPSG:3857" };

await using var session = map.AddS100(new S100MapsuiOptions
{
    CrsTransformFactory = new ProjNetCrsTransformFactory(),
    DatasetPipelineFactory = processorFactory,
    // Post redraw requests to your UI thread, if your control needs that:
    // RedrawMarshal = action => myDispatcher.Post(action),
});

var id = await session.Datasets.LoadAsync("path/to/cell.000");
session.ZoomToDataset(id);
```

The map must use `EPSG:3857`. Call session methods on the thread that owns the
map; rendering runs in the background and asks for a redraw when it finishes.

## Next steps

- [Renderers.Mapsui README](../src/EncDotNet.S100.Renderers.Mapsui/README.md):
  all session options, picking by coordinate, overlays such as the pick
  highlight, dependency injection, and live features such as own ship and AIS.
- [Renderers.Mapsui.Avalonia README](../src/EncDotNet.S100.Renderers.Mapsui.Avalonia/README.md):
  attaching to your own `MapControl`, coordinate conversion and image capture.
- [MapHost sample](../samples/EncDotNet.S100.Samples.MapHost/README.md): a
  complete Avalonia window with palette buttons, visibility and picking.
- [Loading datasets](loading-datasets.md): other ways to open data.
