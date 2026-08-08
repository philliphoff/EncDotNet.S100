---
name: viewer-evaluation
description: |
  Procedure for running an independent, automated evaluation loop
  against the EncDotNet.S100 Avalonia viewer. The CLI launches and
  isolates the process; its embedded MCP server drives everything after
  that. Lets an agent launch the viewer headlessly, then over MCP load
  datasets, drive viewport / palette / display-category / time-step /
  own-ship, capture screenshots, await render-idle, and read render
  stats — then iterate. USE FOR: visually verifying a rendering or
  portrayal change; designing or reproducing integration/regression
  scenarios; measuring load/render performance; capturing reference
  images; deriving test fixtures from real datasets; any "does it
  actually look/behave right in the viewer?" question — INCLUDING from
  renderer (Renderers.Mapsui / Renderers.Skia), Core pipeline, or
  Datasets.SXXX work, not only when editing the Viewer project.
  DO NOT USE FOR: pure library logic unit tests (write xunit directly),
  viewer XAML/localization conventions (see viewer.instructions.md),
  or the MCP tool catalogue reference (see docs/mcp-server.md).
---

# Viewer evaluation loop (CLI + MCP)

The viewer hosts an MCP server and a rich CLI so an agent can drive it
**headlessly** to verify behaviour, capture images, and measure
performance — independent of the GUI and the user's settings. Reach for
this whenever a change needs *seeing* or *exercising end-to-end*, not
just unit-testing. The canonical tool reference is
[`docs/mcp-server.md`](../../../docs/mcp-server.md); this skill is the
operating procedure.

> The viewer is a GUI app — there is **no fully headless windowing
> mode**. It still needs a display server (a real desktop, or X11 /
> XWayland / Xvfb in CI). "Headless" here means *agent-driven without a
> human at the keyboard*.

## Pick the right tool first
- **Library logic** (parsers, geometry, coverage sampling, display-list
  building): write an **xunit test** in the matching `tests/` project.
  Do not spin up the viewer for something a unit test can pin.
- **Visual / portrayal / end-to-end behaviour** (does the chart layer,
  colour, declutter, project, pan, or re-render correctly?): use this
  loop. Capture the finding, then **back it with a unit test or a
  small synthetic fixture** wherever the root cause lives in a library.

## How to drive it: MCP is the single control surface

**Image capture and all live state changes go over MCP — not CLI
flags.** The CLI's job is to *launch and isolate* the process; once the
window is up, an MCP client loads data, reframes, toggles
palette/category/time-step/own-ship, captures images, and quits.
There is deliberately **no** one-shot "load → screenshot → exit" CLI
mode: it existed once (`--screenshot` / `--exit-after-screenshot` /
`--window-size` / …) but was **removed** because it was unreliable and
duplicated the MCP tools. Do not look for those flags — they are gone.

The core capture loop is always:

> `open_dataset` → `set_viewport` → `await_render_idle` →
> `render_to_image` (map surface) or `capture_app_screenshot` (whole
> window)

with `set_palette` / `set_display_category` / `set_time_step` /
`set_own_ship` interleaved as the scenario needs, and
`close_all_datasets` when done. For a before/after image diff across a
code change, run this loop twice (once per build) against the same
viewport — reframing over `set_viewport` rather than relaunching.

The CLI flags that remain are launch-time presets and isolation only:
`--ephemeral` / `--data-dir` (profile/cache isolation), `--mcp*` (enable
+ discover the server), `--bbox` / `--center`+`--zoom` / `--palette` /
`--display-category` / `--time-step` / `--own-ship-*` (preset the
*initial* view — the MCP tools change it thereafter), `--fc` / `--pc`
(catalogue overrides, no MCP counterpart), `--basemap`, and the
logging flags. Follow the procedure below.

### Hand-off to a human for manual GUI testing
When you launch the viewer for the **user** to click through (e.g. to
verify a dialog or interaction), it must outlive your shell session. A
plain `nohup … & disown` from a sync/attached command is killed when the
command's session is torn down — the GUI window dies seconds later.
**Always launch detached** so the process is fully independent:

- Use the `bash` tool with `mode: "async"` **and** `detach: true`. Do
  *not* rely on `& disown` in a sync command for hand-off runs.
- The process survives session shutdown; stop it explicitly with
  `kill -9 <pid>` (never name-based kills) when the user is done.
- Confirm it stayed up (`pgrep -f EncDotNet.S100.Viewer/bin`) before
  telling the user it's ready.

## Procedure: launch, then drive over MCP

### 1. Build, then launch the binary (not `dotnet run`)
Build `Release` first so the PID you drive/trace is the app itself, not
a `dotnet` host:

```bash
dotnet build -c Release src/EncDotNet.S100.Viewer
nohup src/EncDotNet.S100.Viewer/bin/Release/net10.0/<rid>/EncDotNet.S100.Viewer \
  --data-dir /tmp/eval/data --mcp --mcp-port-file /tmp/eval/mcp.url \
  >/tmp/eval/viewer.log 2>&1 & disown
```

> **Always launch the viewer fully detached.** From the agent's bash
> tool, use `mode: "async"` **with `detach: true`** (this `setsid`-wraps
> the process so it survives the bash session). A plain sync call — even
> with `nohup … & disown` — leaves the viewer parented to the bash
> session, so it gets **reaped the moment that call returns** and the
> window dies on its own with no error in the log. The `nohup …& disown`
> form above is only correct inside an already-detached async shell. When
> done, stop it explicitly with `kill -9 <pid>` (see below).

- `--data-dir <PATH>` → **fully isolated instance**: settings, crash
  markers, and all disk caches are re-rooted under the folder. Point it
  at an empty temp dir for a guaranteed-fresh viewer (no local profile
  or cache is read), and `rm -rf` the folder to dispose of it
  completely. Prefer this over `--ephemeral` for evaluation runs that
  must not be influenced by — or leave traces in — the developer's real
  data. It can also be set via the `S100_DATA_DIR` env var, and you can
  pre-seed the folder to launch with mocked-up settings/caches.
- `--ephemeral` → throwaway settings only (still reads the user's other
  on-disk state); use `--data-dir` when you need a clean cache too.
- `--mcp` enables the server; default `--mcp-port 0` picks a free port.
  Any MCP flag implies `--mcp`. A CLI-driven MCP run **never** persists
  the bound port.
- **Poll `/tmp/eval/mcp.url`** for the endpoint URI (also printed to
  stdout as `[MCP] listening on …`). It appears only once the server is
  listening.
- The app ignores SIGTERM (GUI process). **Stop it with `kill -9
  <pid>`** when done.

### 2. Connect an MCP Streamable-HTTP client
The server speaks **MCP over Streamable HTTP**. A throwaway Python
client (initialize handshake → `tools/call`, parsing both SSE framing
and `structuredContent`) is enough. Keep it in the session-state
`files/` dir — **do not commit it**. For manual poking,
`npx @modelcontextprotocol/inspector` (transport: Streamable HTTP) works.

Quirks that bite:
- A dataset id comes back as `{"value":"<id>"}`. Tools like
  `list_time_steps` / `describe_feature` want
  `{"datasetId":{"value":"<id>"}}`, but `close_dataset` wants the
  **bare string** id.
- `open_dataset` on an S-101 **exchange-set folder** may report world
  bounds and not portray; point it at the concrete `.000` cell file and
  supply your own viewport.

### 3. Drive the evaluation loop
Core read-only/mutating tools (full table in `docs/mcp-server.md`):

| Tool | Use |
|---|---|
| `open_dataset {path, spec?}` | Load a cell/tile/exchange-set. Returns bbox + `loadDurationMs`. |
| `list_datasets` | Enumerate loaded datasets + ids. |
| `set_viewport {south,west,north,east}` *or* `{centerLat,centerLon,zoom}` | Frame the data (forms are mutually exclusive). |
| `set_palette {palette}` | Day / Dusk / Night — full re-render. |
| `set_display_category {category}` | DisplayBase / Standard / OtherInformation / All. |
| `set_time_step {index|timestamp}` | S-104 / S-111 / S-411 time-aware datasets. |
| `set_own_ship {lat,lon,cog,sog,heading,hold}` | Position/steer simulated own-ship. |
| `await_render_idle {quietPeriodMs,timeoutMs}` | Block until the live map settles — the harness clock. |
| `render_to_image` | Capture the framebuffer (PNG `ImageContentBlock`) for visual eval / diffing. Map surface only. |
| `capture_app_screenshot` | Capture the **whole app window** (chart + docks/panels/timeline/status bar) as a PNG. Use to *see* non-render UX, e.g. verify `set_panel` opened a panel. |
| `get_render_stats` | Last on-screen paint: `frameDurationMs`, draw calls, per-style breakdown. |
| `close_dataset {id}` / `close_all_datasets` | Unload (retention loops without restarting). |
| `list_panels` | Read-only — enumerate activity panels (left/right/bottom dock tabs) + `available`/`selected`/`dockOpen`/`showing` state. |
| `set_panel {panel, visible?}` | Show/hide a panel by id (`Datasets`, `LayerStack`, `PickReport`, `Timeline`, …) to drive & verify non-render UX. |

**Canonical visual-eval loop:**
`open_dataset` → `set_viewport` → `await_render_idle` →
`render_to_image` → inspect the image → adjust code/params → repeat.
Always `await_render_idle` **before** `render_to_image` or
`get_render_stats`, or you race the paint pass.

**Performance loop:** `open_dataset` (read `loadDurationMs`) →
`set_viewport` → `await_render_idle` (cold) → `await_render_idle` (warm)
→ toggle `set_palette`/`set_time_step` N times, timing each round-trip;
read `get_render_stats` for per-style cost. Palette/display/time-step
wall time is dominated by fixed settle latency — to attribute CPU,
profile with `dotnet-trace collect -p <viewer-pid>` (default profile;
attach the `…/<rid>/EncDotNet.S100.Viewer` PID, not a `dotnet` host).
See the cartography skill for the rendering cost model.

## Turning a finding into a test
- Reproduce the issue in the loop, then isolate the **smallest dataset
  and viewport** that triggers it.
- If the cause is in a library (display-list build, geometry,
  simplification, coverage sampling): add an **xunit** test there with a
  synthetic fixture under `tests/datasets/…`. Gate fixtures that need
  real ENC/HDF5 data with `Skip.If(...)` (`Xunit.SkippableFact`) so CI
  passes without them.
- If it is genuinely render-output behaviour, keep a documented
  repro recipe (dataset path + flags + expected image) in the
  session-state notes — **not** a committed binary screenshot.

## Hygiene (do not commit)
- Real-world datasets for manual runs live under
  `~/Downloads/Complete S10X datasets` (S-101/102/104/111 trial cells).
  **Never commit** dataset files, captured images, traces, or the
  throwaway MCP harness.
- If you add a temporary env-gated `Stopwatch`/timing switch (e.g.
  `S100_*_TIMING=1`) to attribute a stage, **remove it before
  committing** — no diagnostic env switches in shipped code.
- Always `kill -9` the launched viewer when the loop ends; do not leave
  orphaned GUI processes or bound MCP ports behind.
