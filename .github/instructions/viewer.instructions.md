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

## E2E evaluation & performance testing via the MCP server

The viewer hosts an MCP server that lets an agent drive it headlessly —
for **visual/behavioural verification of a change** *and* for
**automated load/render performance testing**. Reach for it whenever a
change needs *seeing* or *exercising end-to-end*, when profiling load /
render hot paths, or when validating that a rendering optimization
actually moves a real-world scenario.

> The general, reusable procedure (both visual-eval and performance
> loops, launch/teardown, MCP client quirks, turning a finding into a
> test) is the **`viewer-evaluation`** skill — load it for the full
> recipe; it applies even from renderer / Core / `Datasets.SXXX` work,
> not only when editing the Viewer project. The full MCP tool catalogue
> lives in `docs/mcp-server.md`. The performance-testing specifics
> below remain here as the viewer-local quick reference.

### 1. Launch an ephemeral instance with a dynamic MCP port

Run the built binary (not `dotnet run`, so the PID you trace is the app
itself) and let it pick a free port, writing the bound URL to a file:

```
src/EncDotNet.S100.Viewer/bin/Release/net10.0/<rid>/EncDotNet.S100.Viewer \
  --ephemeral --mcp --mcp-port-file /tmp/perfrun/mcp.url
```

- `--ephemeral` keeps no persisted state between runs (clean baseline).
- `--mcp` enables the server; `--mcp-port 0` (the default) picks an
  ephemeral port. `--mcp-port-file` writes the endpoint URL once the
  server is listening — poll that file for readiness.
- Launch detached (`nohup … & disown`) so it survives the shell; the
  app is a GUI process and ignores SIGTERM, so stop it with
  `kill -9 <pid>`.
- Real-world datasets for manual runs live under
  `~/Downloads/Complete S10X datasets` (S-101/S-102/S-104/S-111 trial
  cells). Never commit these.

### 2. Connect an MCP Streamable-HTTP client

The server speaks MCP over Streamable HTTP. A minimal Python client
(initialize handshake → `tools/call`, parsing both SSE framing and
`structuredContent`) is sufficient. Quirks that bite:

- A dataset id comes back as `{"value": "<id>"}`. `list_time_steps`,
  `describe_feature`, etc. want `{"datasetId": {"value": "<id>"}}`,
  but `close_dataset` wants the bare string id.
- `open_dataset` on an S-101 exchange-set folder may report world
  bounds and not portray; point it at the concrete `.000` cell file
  and supply your own viewport.

### 3. Drive a scenario

Key automation tools (all under the viewer's MCP server):

| Tool | Use |
|---|---|
| `open_dataset` `{path, spec?}` | Load a cell/tile. Returns `loadDurationMs` (load hot-path wall time). |
| `close_dataset` `{id}` | Unload (bare string id). |
| `list_datasets` | Enumerate loaded datasets + ids. |
| `set_viewport` `{south,west,north,east}` | Frame the data. |
| `set_palette` `{palette}` | Day/Dusk/Night — triggers a full re-render of every dataset. |
| `set_display_category` / `set_time_step` | Drive ECDIS display mode / coverage time-step. |
| `await_render_idle` `{quietPeriodMs,timeoutMs}` | Block until rendering settles (the harness clock). |
| `get_render_stats` | Returns `frameDurationMs` (last Mapsui paint). |
| `render_to_image` | Capture the framebuffer for visual diffing. |

Typical loop: `open_dataset` → `set_viewport` → `await_render_idle`
(cold) → `await_render_idle` again (warm) → toggle `set_palette` N
times, timing each `set_palette` + `await_render_idle` round-trip.

### 4. Read server-side timing and profile

- `open_dataset.loadDurationMs` and `get_render_stats.frameDurationMs`
  are the cheap structured signals; capture cold vs warm.
- Palette/display/time-step **wall time is dominated by fixed
  settle/framework latency**, largely independent of per-dataset
  render CPU. To attribute CPU, profile rather than trusting wall time.
- Profile with `dotnet-trace collect -p <viewer-pid>` (omit
  `--profile`; the default works — `cpu-sampling` is rejected by
  `collect`). The PID to attach is the
  `…/<rid>/EncDotNet.S100.Viewer` process, not a `dotnet` host.
  Export with `dotnet-trace convert --format speedscope`; the export
  is *evented* (open/close), so compute inclusive time by walking
  stacks.
- For one-off per-stage attribution inside a renderer, add a
  temporary env-gated `Stopwatch` block (e.g. `S100_*_TIMING=1`) and
  **remove it before committing** — do not leave diagnostic env
  switches in shipped code.

### 5. Reusable harness

A throwaway Python harness (`mcp_client.py` + scenario drivers) is kept
in the session-state `files/` dir, not the repo. Reuse it as a starting
point; do not commit dataset paths or generated traces.
