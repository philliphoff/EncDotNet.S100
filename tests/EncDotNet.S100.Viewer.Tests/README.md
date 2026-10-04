# EncDotNet.S100.Viewer.Tests

Unit and headless UI tests for the SoundCharts viewer (`src/EncDotNet.S100.Viewer`).

Most tests are plain view-model and service tests. Anything that touches
Avalonia (`Dispatcher.UIThread`, controls, views) runs under Avalonia's headless
platform with `[AvaloniaFact]` / `[AvaloniaTheory]` from `Avalonia.Headless.XUnit`,
which runs the whole test on the UI thread.

## The headless app

`TestAppBuilder` starts `HeadlessViewerApp`, the viewer's real `App` with only its
XAML loaded: ShadUI, the S-100 Day/Dusk/Night chrome variants and every resource
in `App.axaml`. A view under test resolves the same templates and brushes as in
the app. The app's service container, MCP host and crash markers are not
started; tests compose the view models they need.

Rendering uses Skia (`UseHeadlessDrawing = false`), as the app does. That matters
for input as well as for frames: pointer hit testing only finds content that
was actually drawn, such as the text of a tab header whose template has no
background.

## Driving a view: `ViewHost`

`Headless/ViewHost.cs` shows a view in a headless window and drives it the way
a user would:

```csharp
[AvaloniaFact]
public void Clicking_an_inspector_tab_switches_the_inspector()
{
    var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
    datasets.SelectDataset(datasets.Add("/data/US5SEAFL.000", "S-57"));

    using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, 420, 900);
    host.Click(host.Find<TabItem>("Datasets.DatasetsTab"));
    host.Click(host.Find<TabItem>("Datasets.Inspector.LayersTab"));

    Assert.Equal(DatasetInspectorTab.Layers, datasets.InspectorTab);
}
```

- `Find<T>(id)` finds the single visible control with that automation id (or
  `Name`) anywhere in the window, across name scopes. `Find<T>(id, within)`
  scopes the lookup, for ids that repeat once per list row. `Find<T>(predicate)`
  is there for controls that have no id yet. A lookup that matches no control or
  several fails the test, rather than quietly picking one.
- `Click(control)` presses and releases the left button at the control's
  on-screen centre, so the click lands on whatever is on top there. If the
  control is covered or not hit-testable, the test fails just as the click
  would fail for a user.
- `Press(PhysicalKey.ArrowDown)` and `Type("text")` send keyboard input to the
  focused control.
- `Settle()` runs queued dispatcher work, lays out, and renders a frame. Each
  input helper calls it; call it yourself after changing a view model directly.
  The frame is needed because hit testing reads the compositor's scene, which
  the headless render timer only updates when it is forced to tick.

Assert on view-model state for behaviour, and on the visual tree for what the
user sees (`IsEffectivelyVisible`, `SelectedIndex`, text).

## Automation ids

Tests find controls by `AutomationProperties.AutomationId`, not by localized
text such as headers or tooltips. The same ids are the hook for the viewer's
UI-automation MCP tools (#776). Add the ids a test needs to the view as you
write the test.

Convention: dotted PascalCase, `<View>.<Element>`, with more segments where a
view has distinct parts.

| Kind | Example |
|---|---|
| A control in a view | `Datasets.List`, `Datasets.DatasetsTab` |
| A control in a part of a view | `Datasets.Inspector.LayersTab` |
| A control repeated once per item | `Datasets.Row.Remove` (scope with `Find<T>(id, row)`) |

Ids are stable identifiers, not user-facing text. Keep them in English and keep
them when the visible text changes. Set `AutomationProperties.Name` separately
for screen readers.
