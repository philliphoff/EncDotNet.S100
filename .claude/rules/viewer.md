---
paths:
  - "src/EncDotNet.S100.Viewer/**"
  - "tests/EncDotNet.S100.Viewer.Tests/**"
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

## Accessibility and automation

Accessibility and automation are part of building any new or changed UI, not
a later pass. Screen readers, the headless view tests and the `ui_*` MCP tools
all reach controls through Avalonia's automation peers, so the same attributes
serve all three. See `tests/EncDotNet.S100.Viewer.Tests/README.md` for the full
conventions.

- **Give every interactive control an automation id**:
  `AutomationProperties.AutomationId="<View>.<Element>"` in dotted PascalCase,
  e.g. `Datasets.Toolbar.Add`, `Settings.FourShades`,
  `Library.NodeMenu.Rename`. Ids are abstract, English and **never
  localized**; keep them when the visible text changes. A control repeated
  per row shares one id (`Datasets.Row.Remove`); tests and tools scope it by
  the row.
- **Give every control an accessible name**, from a localized `Strings.*`
  value. A button whose content is a string is named by it. A button whose
  content is a control (an icon, or a panel holding an icon and text) is
  not: Avalonia names it after the content's type (e.g.
  `FluentIcons.Avalonia.FluentIcon`), so set `AutomationProperties.Name`.
  Inside a data template, bind it to the row's own text (e.g.
  `{Binding Label}`).
- **Name list rows, tree items and tabs.** A `ListBoxItem`, `TreeViewItem`
  or `Expander` whose content is a view model, or a `TabItem` whose content
  is a panel, is named after that object's type. Set
  `AutomationProperties.Name` on the item container style from the row's
  display name (`<Style Selector="ListBoxItem" x:DataType="vm:Row">` with a
  `{Binding Name}` setter), and on a `TabItem` to a sentence-case
  `Label_Tab_*` string.
- **Keep names short** ("Move up", "Zoom in"), in sentence case: a name in
  capitals ("DATASETS") may be spelled out letter by letter, and a
  placeholder ("Filter by name, title, product…") is not a name. A new
  activity tab passes `name:` (a `Label_Activity_*` string) to
  `AddActivityTab`, since some pane titles are in capitals. Put a longer description in
  the tooltip and in `AutomationProperties.HelpText`, and a keyboard
  shortcut in `AutomationProperties.AcceleratorKey`, not in the name. An
  icon button whose tooltip is a sentence gets its own short `Label_*`
  string for the name and the tooltip's string as its `HelpText`.
- **Name inputs after their label.** A text box, combo box, slider, number
  box or toggle switch next to a label `TextBlock` gets
  `AutomationProperties.Name` set to the label's string; on its own a screen
  reader announces only "toggle switch, on".
- **Hide duplicates.** Mark a control that only repeats its parent's
  action (e.g. a tree item's expand chevron)
  `AutomationProperties.AccessibilityView="Raw"` instead of naming it.
- **Don't rely on raw pointer events for behaviour.** Behaviour behind
  `PointerPressed`/`Tapped` alone cannot be reached by the keyboard, screen
  readers or the `ui_*` tools. Back it with a control that has a command
  (a `Button`, `ToggleButton`, list selection…); a `Button` with an empty
  template over the visual keeps the look (the Library's clickable tags).
  When a pointer shortcut stays (double-click to load, click a value to
  copy), give it a keyboard and automation path too and say which in a
  code comment.
- **Run the guard.** `UiAutomation/AccessibilityGuardTests` in
  `tests/EncDotNet.S100.Viewer.Tests` fails on any button or input without
  an accessible name, any name longer than a short label or in capitals,
  any activity tab without its own `name:`, and any
  interactive control without an automation id, in every view. There is no allow-list: new UI must pass it as written.
  `UiAutomation/LiveAccessibleNameTests` shows panels with populated rows
  and fails on any row, item or button whose live name is a type name or
  missing; add a case when you add a list or tree to a view.
- **Test new UI through its ids.** Add an `[AvaloniaFact]` view test that
  drives the new UI with `ViewHost` and finds controls by automation id,
  not by localized text.

## GridSplitters

- All `GridSplitter` instances use the shared style class
  `PaneSplitter` (transparent background, `BorderThickness=0`,
  accent brush on the `.hovered` and `:pressed` states) and a
  thickness of 4 (Width for vertical splitters, Height for
  horizontal). The 500ms hover delay is implemented by the
  `Behaviors.HoverDelay` attached property — set
  `bh:HoverDelay.DelayMilliseconds="500"` on each splitter. The
  behavior wires `PointerEntered`/`Exited` and toggles the
  `hovered` style class via a `DispatcherTimer`, so a brief
  pass-through never flashes the accent and pointer-leave clears
  it instantly. Avalonia's `BrushTransition` with `Delay` does not
  reliably gate quick mouse pass-throughs, hence the timer-based
  behavior.
- Side panels adjacent to a splitter must NOT draw their own
  `BorderBrush`/`BorderThickness` on the splitter-facing edge — the
  splitter is intentionally invisible at rest and a static panel
  border would defeat that.
- The style is defined in `MainWindow.axaml`'s `<Window.Styles>`. A
  duplicate copy lives in `Views/CatalogPanelView.axaml`'s
  `<UserControl.Styles>` because Avalonia styles defined on a Window
  do not propagate into UserControl logical sub-trees.
- When adding a new splitter, set `Classes="PaneSplitter"`,
  `Width="4"` or `Height="4"`, and
  `bh:HoverDelay.DelayMilliseconds="500"` — do not introduce a
  per-splitter style block.

## Application panels

- The generic `Views.ApplicationPanel` control provides the standard
  panel chrome: a title bar with an **upper-cased** `Title` and an
  optional close button, above a content area that fills the remaining
  space. It derives from `ContentControl`, so its `Content` is the panel
  body; the title bar comes from the control template in
  `Views/ApplicationPanel.axaml` (registered app-wide via a
  `StyleInclude` in `App.axaml`).
- Use it for any docked/standalone panel rather than hand-rolling a
  header `Border` + `TextBlock` + close `Button`. The left/right/bottom
  docks in `MainWindow.axaml` are built from it.
- Set `Title` (natural-cased is fine — the control upper-cases it via
  `ToUpperConverter`). For a dismissable panel set `ShowCloseButton`,
  `CloseCommand`, `CloseCommandParameter`, and a localized
  `CloseButtonToolTip` (`Tooltip_*`). Panels that swap content another
  way (e.g. the left activity pane) leave `ShowCloseButton` at its
  `false` default.

## Pick mode button
- The toggled pick-mode button uses `.pickActive` class plus a child
  selector `Button#PickModeButton.pickActive ic|FluentIcon` to push
  `Foreground=White` down to the icon (FluentIcon does not inherit
  Foreground automatically).

## Verifying changes end to end

To see or measure a viewer change (visual check, load/render timing,
profiling, reference images), load the `viewer-evaluation` skill: it
launches the built viewer headlessly via the `s100` CLI and drives it over
the viewer's MCP server. The MCP tool catalogue is in `docs/mcp-server.md`.
