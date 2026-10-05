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

As in the app, rendering uses Skia (`UseHeadlessDrawing = false`) with the
embedded Inter font. That matters for input as well as for frames: pointer hit
testing only finds content that was actually drawn, such as the text of a tab
header whose template has no background. The font makes text look the same on
every OS.

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

- `ViewHost.Show(view, width, height, theme)` dresses the window as the main
  window is at run time: the chrome theme is set on the application, the
  background is the theme's window colour, and the accent brushes come from
  `AccentColors.Apply`, the same code the main window uses.
- `Find<T>(id)` finds the single visible control with that automation id (or
  `Name`) anywhere in the window, across name scopes. `Find<T>(id, within)`
  scopes the lookup, for ids that repeat once per list row. `FindAll<T>(id)`
  returns every visible match, and `IsShown(id)` says whether one is visible.
  `Find<T>(predicate)` is there for controls that have no id yet. A `Find` that
  matches no control or several fails the test, rather than quietly picking one.
- `Click(control)` presses and releases the left button at the control's
  on-screen centre, so the click lands on whatever is on top there. If the
  control is covered or not hit-testable, the test fails just as the click
  would fail for a user. `DoubleClick` clicks twice at the same point.
  Clicks go to the control's own top level, so they also work on menu items,
  which open in a separate popup.
- `RightClick(control)` returns the context menu that opened; find its items
  with `Find<MenuItem>(id, menu)` and click them.
- `Press(PhysicalKey.ArrowDown)` and `Type("text")` send keyboard input to the
  focused control. Select-all is `Press(PhysicalKey.A, RawInputModifiers.Control)`
  on the headless platform.
- `Settle()` runs queued dispatcher work, lays out, and renders a frame. Each
  input helper calls it; call it yourself after changing a view model directly.
  The frame is needed because hit testing reads the compositor's scene, which
  the headless render timer only updates when it is forced to tick.

Assert on view-model state for behaviour, and on the visual tree for what the
user sees (`IsEffectivelyVisible`, `SelectedIndex`, text).

## Frame snapshots

`ViewHost.CaptureFrame()` renders the window to a PNG. `ViewFrameTests` checks
such frames with Verify against snapshots in `Snapshots/<TestClass>/`, using the
perceptual comparer from `EncDotNet.S100.VisualRegression` (as the chart renders
do), so small anti-aliasing differences between platforms pass:

```csharp
return Verify(host.CaptureFrame(), "png").UseParameters(theme);
```

On a mismatch the test writes `*.received.png` and `*.diff.png` next to the
`*.verified.png`; CI uploads them as an artifact. If the change is intended,
replace the verified file with the received one. Don't commit received files.

Frames catch changes a user would notice at a glance: a theme or resource that
no longer applies, a missing selection tint, a panel laid out differently. A
one- or two-pixel detail is within the tolerance, so check small details with an
assertion instead. Keep frames free of machine state: for example, show times
in UTC, not in the machine's zone.

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
