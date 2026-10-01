# Time and the Timeline at collection scale — design handoff

Repo: `philliphoff/EncDotNet.S100`. Scope: `src/EncDotNet.S100.Viewer`. Answers `time-and-timeline-brief.md` (in this folder, 01.10.2026). Follows #685 slice 4 (#703); quick wins are #706.
Round: **1** · Issued: 2026-10-01.

Mock: `Timeline Revamp.dc.html`. Open in a browser. Scenario tabs at the top (2 Underway, 3 Rotterdam, 4 Mixed cadences, 5 Nationwide) recompute every frame. Tweaks: **scenario**, **mapViewFilter** (In map view), **primaryZone** (UTC / Data-area local), **mapBadges** (Off / Exceptions only — the opt-in setting). Frame ids are badges: `1a–1e` evolved strip, `2a–2e` lanes, `3a–3e` HUD; letters a–e = Live, Pinned in data, Pinned in a gap, Every forecast expired, Loading after a scrub. Now is fixed at Thu 01.10.2026 20:30Z. Station and NL dataset names are illustrative.

The mock is a high-fidelity HTML design reference, not code to port. Build in the existing Avalonia + ShadUI views using the app's theme resources and controls. All strings localisable. Mock-only scaffolding (scenario tabs, frame badges, striped map placeholders, the fake scale bar and tool column in section 3) is not part of the design.

## Changes in this round

Round 1, first issue. Everything below is new. Design review changes before issue: the picker popover over the map was dropped; the HUD moved to the bottom centre with the map-tool see-through surface; per-layer time moved off the map into the layer list (map badges are opt-in, exceptions only).

## Open items

## Decisions (read first)

1. **Recommended combination:** Time HUD (3) as the everyday surface; dock panel = lanes (2), collapsible to the strip (1). Only one is visible at a time. No picker popover over the map. Direction 1 alone is a valid smaller first ship.
2. **Live is a mode.** States: Live, Pinned, (later) Playing. Any user change of view time exits Live; loading or replacing a run does not.
3. **Per-product time policy** replaces unbounded Nearest (§4c). Outside tolerance a layer **hides**; its layer-list row says where the nearest data is.
4. **Size-honest gaps:** collapse when `len > max(6 h, 15 % of data time)`; width `clamp(3.5 + 1.25·ln(len/6 h), 3.5, 11) %` of the axis; labelled `⋯ 6 wk ⋯`; shorter gaps drawn to scale.
5. **Layer time** is one string shown in the layer list row and the dock lane label. **Not on the map** by default; the HUD chip counts layers with no data and opens the layer list. Opt-in: exception badges on the map for layers with no data only.
6. **Lanes show loaded + Library-known** data in the map-outline language: solid = loaded, dashed = online, solid outline no fill = on disk.
7. **In map view** filter: lanes and axis follow the viewport; on by default above 6 lanes.
8. Charts/notices on the clock: **opt-in, off**. Playback: **not in the first pass**. Dock does **not** auto-open; it opens from the HUD.

### Part A — Clock and modes

Files: `Services/GlobalTimeService.cs`, `ViewModels/TimelineViewModel.cs`, `Services/MapsuiDatasetLayerSession.cs`.

- [ ] **A1. Modes.** `TimeMode { Live, Pinned }` on `GlobalTimeService`. Live: view time = now, re-evaluated every minute. Exit on scrub, step, preset, next/previous data, chart click. Enter on the Live / Go live button or `N`.
- [ ] **A2. Now for every timeline** (drop the forecast-name test). The dashed NOW line is always drawn; the window always includes now.
- [ ] **A3. Unclamp view time** from loaded min/max so Live can sit in a gap or past all data.
- [ ] **A4. Live in a gap / past all data:** layers follow policy (hide); message per B3.

### Part B — Readout and messages (1a–1e)

- [ ] **B1. Mode pill.** Live: dark fill `#18181B`, white 10.5/700 +.06em `LIVE`, green dot `#4ADE80`. Pinned: outline `#D4D4D8`, `PINNED`, plus a filled **Go live** button.
- [ ] **B2. Readout.** Mono 13/600 primary time (`Fri 02.10 08:00Z`), then offset in accent (`in 11 h 30`, `5 h ago`, `now`), then muted secondary zone (`04:00 EDT local`). Data-area zone = zone of the dataset at the map centre; omit when equal to the primary.
- [ ] **B3. Status line** (11.5): left muted range — `cbofs 12:00Z · Baltimore 06:00Z · step follows cbofs (1 h)`; with the filter: `2 of 14 models in map view · …`. Right message, one at a time, priority:
  - Loading: `Drawing 23:00Z · 1 of 3 layers ready` · Cancel (muted)
  - All forecasts ended (Live): `Every forecast ended 10 h 30 ago` · **Check for new runs** (red `#B91C1C`; = Library Refresh for those sources); band at 45 %
  - Some layers empty: `No data at this time for 1 of 2 layers` (`No data now` when Live) · **Next data ›** or **‹ Previous data**, whichever is nearer (amber `#92400E`)
- [ ] **B4. Map stamp** (top-left, mono 11.5; shown while the dock is open and the HUD is hidden, and always burned into screenshots/exports): `LIVE · Thu 01.10 20:30Z`, `Fri 02.10 08:00Z · in 11 h 30`, while loading `Fri 02.10 08:00Z · drawing 23:00…` (shows the time actually drawn). Dot: green Live, blue Pinned, grey loading.

### Part C — Axis, gaps, stepping (1, 4a, 4b)

Files: `Services/TimelineAxisMap.cs`, `Views/CoverageBandControl.cs`, `Views/TimelineView.axaml`.

- [ ] **C1. Gap rule** (Decision 4). The window adds ±min(1.2 % of span, 36 h) of linear space around now and view time so both are always visible.
- [ ] **C2. Gap drawing.** Slider track broken by a white skewed block with 1.5 px `#A1A1AA` edges; band filled with a 135° hatch `#fff/#F4F4F5`; italic muted label `⋯ 6 wk ⋯` in the label row. Tooltip: duration and what is either side.
- [ ] **C3. Axis labels.** Priority gap label > day > 6-hour; minimum spacing ≈ 6 % (days) / 4.5 % (hours). Day format `Fri 02.10` when a day ≥ 9 % of width, else `02.10`.
- [ ] **C4. Sample ticks** of the driver layer when one step ≥ 0.7 % of width; otherwise a solid band (density). Snap to samples under the same condition (replaces the hard 50).
- [ ] **C5. Zoom/pan.** Wheel/pinch zoom around view time, drag to pan. Overview strip (6 px) above the axis: grey loaded union of *all* lanes, dark bracket = visible window. Preset menu right of Live: Now ± 6 h, Today, Next 48 h, This run, All loaded (`0`), In view.
- [ ] **C6. Step menu (4b).** By time: 10 min, 1 h (default), 6 h, 1 day. By data: Sample of ▸ (driver layer), Dataset/run boundary, Data skipping gaps. Default driver = the forecast with the coarsest cadence in view. ‹ › always enabled while a next value exists.
- [ ] **C7. Previous / next data** buttons flank ‹ ›; disabled (30 %) when none.
- [ ] **C8. Keyboard:** ←/→ step · Shift coarse (next unit up) · ⌥ previous/next data · Home/End range ends · `N` live · `+`/`−` zoom, `0` fit · `T` open/close dock · Space reserved.

### Part D — Time policy and layer time (4c, 4d)

Files: `TimePolicy`, layer list views.

- [ ] **D1. Policies:**
  - S-111: nearest sample; hide beyond one step outside the run window.
  - S-104 forecast: at or before; hide beyond one step.
  - S-104 observations: at or before; hold last ≤ 30 min, then hide.
  - S-411: at or before, valid until next; cap 14 d after the last.
  - Live: always now; when pinned draw at 50 %. Own ship never hides.
- [ ] **D2. Layer time string:** `HH:MMZ · T+N h` for forecasts (T+ from run time), optional offset `(−24 min)`; `no data · last 18:00Z, 6 h earlier` / `next 20.08 00:00Z, 6 wk later` (date added when > 20 h away); `drawing…`; `live · not at view time`.
- [ ] **D3. Layer list row (4d).** Time-aware rows get a second line: mono 11 layer time + offset (`#3F3F46`; no data `#B45309`; drawing/live muted). No data adds a **Hidden** amber tag. Clicking the time jumps to the nearest data. Dock lane labels use the same string.
- [ ] **D4. HUD chip → layer list.** "No data at this time for 1 of 2 layers" opens and scrolls the layer list to the first hidden layer; its Next/Previous data action stays.
- [ ] **D5. Opt-in map badges** (setting "Show missing layers on the map", off): only layers with no data, bottom left, column ≤ 230 px, see-through surface, offset ellipsized with full text in the tooltip.

### Part E — Lanes (2a–2e)

- [ ] **E1. Layout.** 248 px label column + track. Summary row (strip slider + band) and label row on top; then groups: `▾ S-111 Surface currents  2`, lanes 34 px: swatch, code (mono 12/600), sub (11 muted), tag, layer time (mono 10.5) on the second line.
- [ ] **E2. Bands:** loaded 12 px product colour; online 18 px dashed outline, fill colour at 6 %; expired loaded grey `#A1A1AA` with the Library **Expired** tag; newer run online → **New run** amber tag. Live sources: a 0.7 % mark at now.
- [ ] **E3. Lines:** dashed NOW, 2 px accent view-time line at 55 %, through all lanes. Gap hatch through all lanes.
- [ ] **E4. In map view** filter (checkbox in the preset row): only lanes intersecting the viewport, which also define the axis; others fold into `▸ 12 more outside the map view` with a grey union band. Hover a lane → highlight its footprint on the map.
- [ ] **E5. Band actions:** popover with run, window, size, Get / Load, Reveal in Library. Download progress fills the band.
- [ ] **E6.** `Collapse to strip` in the dock header switches to direction 1; remembered.

### Part F — Time HUD (3a–3e)

- [ ] **F1. Placement.** Bottom centre of the map, 14 px above the edge. Must not overlap the scale bar (top centre), the tool column (right) or attribution. Layer time is not drawn on the map (see D3–D5).
- [ ] **F2. Surface.** Same as the map tool buttons: white at 72 %, 8 px backdrop blur, radius 10, no border, no shadow. Text full-opacity ink. The map stamp (directions 1/2) and opt-in badges use the same surface.
- [ ] **F3. Contents** (40 px high): mode pill (radius 6) · mono time 13/600 · offset (accent) · `drawing…` while loading · ‹ › · **Live** (when pinned) · dock toggle (panel icon, tooltip "Open timeline", shortcut T).
- [ ] **F4. Message chip** 8 px above the bar, same surface tinted (amber `rgba(255,251,235,.82)`, red `rgba(254,242,242,.82)`), same text/actions as B3.
- [ ] **F5. Dock relationship.** Dock open → HUD hidden (the dock header has the same controls); dock closed → HUD back. The dock remembers lanes vs strip. Under 900 px wide the dock is unavailable and the HUD is the only control; under ~640 px drop the offset, then the PINNED label.
- [ ] **F6. No picker.** Clicking the time only focuses the bar for keyboard stepping. Presets, zoom, step menu and next/previous data are dock-only.

### Part G — Library link (slice 5)

- [ ] **G1.** Online / on-disk windows come from cached indexes (works offline).
- [ ] **G2.** Library facet **Valid at view time** beside All / Local / Online / Updates.
- [ ] **G3.** Library row: Show on timeline, Go to start of run. Timeline band: Reveal in Library. Expired / New run use the same tags in both.

## Tests

- `TimelineAxisMap`: Rotterdam fixture (4 clusters, gaps 6–12 wk) → 4 collapsed gaps, widths strictly increasing with duration, all within 3.5–11 %; a 3 h gap in a 80 h range is not collapsed.
- `TimePolicy`: S-111 view 1.5 steps past run end → hidden; S-104 obs 24 min after last → held with offset; 40 min → hidden; Charleston Dec 2025 at today → hidden with `last 14.12.2025 …`.
- Modes: Live advances with a fake clock; any step/scrub/preset sets Pinned; replacing a run keeps the mode.
- Stepping: ‹ › enabled with 10 000 samples; driver defaults to the coarsest forecast in view; next/previous data skip gaps.
- Layer time string formatting (D2) for ok / offset / no data (both directions) / drawing / live.

## Acceptance

- Scenario 2: loading cbofs starts Live; an hour later the map shows the new now with no user action.
- Scenario 3: the four clusters and the 5-week gap to now are visible at once; ⌥→ jumps cluster to cluster.
- Scenario 4: pinned 3.5 h ahead, cbofs draws and Baltimore is hidden; the HUD chip reads “No data at this time for 1 of 2 layers”.
- Scenario 5: with In map view, only cbofs/dbofs are listed and set the axis; panning to the Gulf of Maine re-filters without moving view time.
- Scenario 7: S-104 Charleston (Dec 2025) never draws at today’s time.
- Nothing larger than the 40 px HUD bar (plus its chip) sits over the map unless the user opts in.

## Tokens used in the mock

ShadUI light: background `#FFFFFF`, foreground `#09090B`, muted `#F4F4F5`, muted-fg `#71717A`, border `#E4E4E7`, primary `#18181B`, accent `#2563EB` (text on glass `#1D4ED8`), success `#16A34A`, live dot `#4ADE80`, warning text `#B45309` / `#92400E`, warning fill `#FFFBEB`, warning border `#FCD34D`, destructive text `#B91C1C`. Product swatches: S-111 `#2563EB`, S-104 `#0E7490`, live `#52525B`, expired `#A1A1AA`. Map surface: white 72 % + 8 px blur, radius 10 (bar) / 6 (chips, pills). Radius 4/6/8 elsewhere. Type: Inter 10.5–13.5; JetBrains Mono for times and dataset codes. Use the app's theme resources, not these literals.

## Files in this folder

- `HANDOFF.md` — this spec.
- `Timeline Revamp.dc.html` (+ `support.js`) — the mock; open in a browser. Sections: 0 vocabulary, 1 strip, 2 lanes, 3 HUD, 4 details (gaps, step menu, keyboard, policy, layer list), 5 answers to the brief's §8 and slices.
- `time-and-timeline-brief.md` — the brief this answers.
- `screenshots/` — static captures at 1×, named `<scenario>-<frame id>-<direction>-<state>.png`: all of `1a–1e`, `3a–3e` and `2a/2b/2d` for scenario 2 (underway); Rotterdam `1c`, `2b`, `3c`; mixed `2b`, `3b`; nationwide `1b`, `2a`; and `details-4a…4d`. The mock is the source of truth where they differ.

## Slices

- **0** (#706): ‹ › above 50 samples; tolerance policy (D1); Now follows the clock.
- **1**: Modes, readout, Now everywhere, map stamp (A, B).
- **2**: Axis, gaps, zoom, step menu, next/previous data, keyboard (C).
- **3**: Layer time in the layer list, HUD chip opens it, loading state (D2–D4, B3 loading). D5 optional.
- **4**: Lanes for loaded data + In map view (E1–E4, E6).
- **5**: Library-known bands and actions (E5, G).
- **6**: Time HUD and dock toggle (F).
- **Later**: playback/loop, run comparison and older runs, charts and notices on the clock, chart-as-scrubber, voyage time.
