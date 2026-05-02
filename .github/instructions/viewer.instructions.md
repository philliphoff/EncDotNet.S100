---
applyTo: "src/EncDotNet.S100.Viewer/**"
---

# Viewer editing rules

When modifying viewer code:

## Localization

- **All UI-visible strings must be localizable.** Never hardcode a
  user-facing string in XAML, view code-behind, or a view-model.
- Add the string as a `<data name="…"><value>…</value></data>` entry
  in `src/EncDotNet.S100.Viewer/Resources/Strings.resx` and add a
  matching `public static string Foo => Get(nameof(Foo));` property
  in `src/EncDotNet.S100.Viewer/Resources/Strings.cs`. The `Strings`
  class is hand-written; keep `.resx` and `.cs` in sync manually.
- In XAML, reference the string via
  `{x:Static loc:Strings.Key}` (declare
  `xmlns:loc="using:EncDotNet.S100.Viewer.Resources"` once per file).
- In code, reference via `Strings.Key`. For format strings, use
  `string.Format(Strings.Status_Foo, arg1, …)` — never an
  interpolated `$"..."` literal for status text, file-picker titles,
  native-menu items, or other shown text.
- Group resx keys with stable prefixes (e.g. `Tooltip_*`, `Button_*`,
  `Menu_*`, `Status_*`, `Settings_*`, `Catalog_*`, `Pick_*`,
  `DistanceUnit_*`, `Palette_*`, `FilePicker_*`, `Window_*`,
  `Pane_*`, `Catalogue_*`).
- View-model display strings (e.g. enum→name conversions) should
  resolve through `Strings` too, either directly or via an
  `IValueConverter` that maps enum values to localized labels (see
  `PaletteTypeNameConverter`, `DistanceUnitNameConverter`).

## Tooltips

- **Every `Button` and `ToggleButton` in the viewer must have a
  meaningful `ToolTip.Tip`** sourced from `Strings.Tooltip_*` (or an
  equivalent resx key). Icon-only buttons especially need tooltips.

## GridSplitters

- All `GridSplitter` instances use the shared style class
  `PaneSplitter` (transparent background, `BorderThickness=0`,
  accent brush on `:pointerover` and `:pressed`) and a thickness of
  4 (Width for vertical splitters, Height for horizontal).
- The style is defined in `MainWindow.axaml`'s `<Window.Styles>`. A
  duplicate copy lives in `Views/CatalogPanelView.axaml`'s
  `<UserControl.Styles>` because Avalonia styles defined on a Window
  do not propagate into UserControl logical sub-trees.
- When adding a new splitter, set `Classes="PaneSplitter"` and
  `Width="4"` or `Height="4"` — do not introduce a per-splitter
  style block.

## Pick mode button

- The toggled pick-mode button uses `.pickActive` class plus a child
  selector `Button#PickModeButton.pickActive ic|FluentIcon` to push
  `Foreground=White` down to the icon (FluentIcon does not inherit
  Foreground automatically).
