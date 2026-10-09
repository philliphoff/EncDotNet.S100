# soundcharts.app: capture shot list

Draft, 2026-10-03. This file lists the screenshots, loops and brand assets for the
SoundCharts landing page (this Astro site). Every image slot on the page is a
`<Shot id="…">` that uses the shot ID listed here. Drop a capture into
`src/assets/shots/<ID>.png` and the page picks it up in place of the stand-in or
placeholder. Run `npm run dev`, or open the built page with `?review`, to see the
IDs.

The site lives at **soundcharts.app**, so the page should look international.
The hero can stay in Puget Sound because that's where the name comes from. The
product grid and feature shots are spread across the UK, Europe, Asia-Pacific and
US waters. US cells are used where they are the best public-domain data, not
because they are home waters.

## Capture conventions

These apply to every shot unless a shot says otherwise.

| Setting | Value |
|---|---|
| Window | `--window-size 1600x1000` on a Retina display, so captures come out at 3200×2000 px. |
| Capture | Use `capture_app_screenshot` (whole window, chrome included) for app shots. Use `render_to_image` (map only) for product tiles. |
| Chrome theme | Dark for the hero and feature rows, with a Light twin (`<ID>.light.png`, `--theme light`) of each Day-palette window shot for visitors on a light system theme. |
| Palette | Day unless the shot says otherwise. Use `set_palette`. `render_to_image` ignores palette changes, so check palette shots with `capture_app_screenshot`. |
| Basemap | Bundled Natural Earth, not OSM. This removes the OSM attribution and gives a cleaner look. |
| Clean state | Run `dismiss_notification` for everything, then `await_render_idle` before each capture. Use no crash markers and a fresh `--data-dir`. |
| Time | Launch with `--mcp-test-hooks` and pin `set_test_clock`, so the Live and Expired states and timeline labels come out the same on every run. |
| Status bar | Make sure the cursor readout shows a sensible position. Move the pointer onto the map, or accept an empty readout. |
| Output | Save to `site/src/assets/shots/<ID>.png` (e.g. `H1.png`, `F1a.png`, `F7-night.png`). The build makes the AVIF/WebP sizes. Loops are MP4 (H.264) plus WebM, ≤ 3 MB, silent, with a poster frame. |

The capture script should be one .NET file-based app (`site/capture/capture.cs`,
using the `ModelContextProtocol` client) with one function per shot, so any shot can be taken again after a UI change. Loops are
frame sequences (`step_time` / `set_view_time` → `render_to_image`) joined with
`ffmpeg`, or a screen recording where real UI motion matters (F4, F5).

## Data licensing check (blocks publishing, not capture)

| Source | Status |
|---|---|
| NOAA ENC (S-57), NOAA S-102/S-104/S-111 on AWS | US public domain. Safe; credit NOAA. |
| USACE inland IENC | Public domain. Safe; credit USACE. |
| UKHO trial S-101/102/104/111 (`~/Downloads/Complete S10X datasets`) | *Data Exploration Licence* (v0.2, May 2020). **Not for the public site without written permission** (read 2026-10-03, see below). |
| IC-ENC S-101 producer cells | *Product Development and Testing Licence* v1.2, signed 2026-06-09, expires 2027-06-09. **Not for the public site without written permission** (see below). |
| IHO S-101 test dataset (`101AA00DS0010`, the current P01/F1a stand-in) | Producer code `AA` is the IHO test producer, not UKHO or IC-ENC. Probably fine, but **check the terms it was distributed under** before it appears on the site. |
| Repo fixtures (`tests/datasets/*`) | Ours or from spec samples. Safe. Some are synthetic and look it. |
| Natural Earth basemap | Public domain. |

Shots marked ⚖ depend on one of the uncleared rows. Each of them has a fallback.

**Licence reading (2026-10-03, not legal advice).** The UKHO and IC-ENC licences
use almost the same template. Neither covers screenshots on a public product
website:

- Display is allowed only "on your own equipment" to demonstrate an idea to
  potential customers or backers. Showing it to anyone else needs UKHO/IC-ENC's
  prior agreement.
- Publishing "small extracts" is allowed in articles and at events meant to
  share knowledge (papers, trade journals, conferences). A product landing page
  is not that.
- Both forbid making the data available to third parties, or creating products
  or services that used the data, beyond what is listed. Anything more needs their
  agreement first, and both licences invite you to ask.
- When the licence ends, you must destroy the data, *including where it is
  embedded in other material*. That would mean taking the images off the site.
  IC-ENC's licence expires 2027-06-09.
- If permission is given: show "Not to be used for Navigation" with the images,
  acknowledge the source, and don't use UKHO or IC-ENC branding.

So the default is NOAA and USACE data only. The Solent and IC-ENC shots come in
only with written permission from UKHO and IC-ENC.

**Decision (2026-10-03):** capture with NOAA charts only. Phillip is asking UKHO
and IC-ENC about screenshot use. Until they reply, every ⚖ shot uses its NOAA
fallback.

---

## H: Hero

### H1 · Multi-product hero, Day
- **Purpose:** first thing anyone sees. It should show "this is a real app, and
  those are layered marine data" in under two seconds.
- **Where:** Elliott Bay, Seattle, looking towards Bainbridge Island.
  Centre ≈ 47.61 N, 122.40 W, at about 1:40k, so ferries, piers, the
  traffic lanes and soundings are all readable.
- **Data:** NOAA S-57 `US5WA*` cells around Elliott Bay (in
  `~/SynologyDrive/Charts/NOAA ENCs`), plus NOAA S-102 `Washington` region
  tiles (AWS) underneath. Optionally add S-111 current arrows from
  `wcofs` (AWS, band 2) if they are dense enough at this scale (**verify**).
- **State:** Datasets panel open on the left with 3 visible layers and their
  layer times. Timeline visible at the bottom in Live mode. Other panels closed.
  Display category Standard.
- **Fallback (⚖ or weak S-111):** the Solent (UKHO trial S-101 + S-102 +
  S-104 + S-111). This is the only place where we have all four core
  S-100 products overlapping. Or San Francisco Bay (NOAA S-57 + S-102 +
  `sfbofs` S-111, all public domain).

### H2 · Same view, Night
- Same as H1, with the palette set to Night and the S-100 Night chrome. This is used by the
  page's Day/Dusk/Night switch and in the palette row (F7).

### H3 · Same view, Dusk
- Same as H1, with the palette set to Dusk.

### H4 · Hero loop (optional, replaces the H1 still on wide screens)
- 8 s, seamless. A slow pan and zoom into Elliott Bay (about 1:150k → 1:40k)
  while S-111 arrows animate through 6 time steps. Poster frame = H1.
- Build: script `set_viewport` keyframes and `step_time` and render the frames
  headless. If panel or chrome motion is wanted, use a screen recording.

---

## P: Product grid ("one map, many layers of the sea")

Square tiles, 1200×1200 rendered (`render_to_image` with `width=height=1200`),
map only, Day palette, no chrome. Each tile gets a plain-language caption under
its spec number. Order is listed below; the grid shows 12 on desktop and
8 on phones (`*` marks the 8).

| ID | Spec · caption | Data & place | Notes |
|---|---|---|---|
| P01* | S-101 · Electronic charts | UKHO trial S-101, Solent ⚖, or IC-ENC Denmark/Netherlands ⚖ | The next-generation ENC. Fallback: S-57→S-101 (P02) at a European location using SHOM `ENC_FR` cells ⚖. |
| P02* | S-57 · Today's charts, translated | NOAA `US5WA*`, Deception Pass or San Juan Islands | Shows that the large existing S-57 world works too. |
| P03* | S-102 · Bathymetry | NOAA S-102, Columbia River mouth or Puget Sound | Use the colour ramp at full strength. It is the most striking tile. |
| P04* | S-104 · Water levels | UKHO trial S-104, Solent ⚖ / NOAA Charleston pilot (`tests/datasets/S104`) | The gridded tile needs `set_dataset_state` to make it visible. |
| P05* | S-111 · Surface currents | NOAA `cbofs`, Chesapeake Bay mouth | Arrow field over the bay. |
| P06* | S-124 · Navigational warnings | `tests/datasets/S124/navwarn_mixed.gml` over an S-57 base | Fixtures are synthetic. Put them over a real chart so they read well. |
| P07* | S-125 · Aids to navigation | `tests/datasets/S125/aton_chesapeake.gml` over NOAA Chesapeake | |
| P08* | S-411 · Sea ice | `tests/datasets/S411/cis_seaice_synthetic.gml` | Egg codes look distinctive and unusual, which works well here. |
| P09 | S-421 · Route plans | `tests/datasets/S421/RTE-TEST-GFULL.s421.gml` | Or reuse a crop of F4. |
| P10 | S-129 · Under-keel clearance | `tests/datasets/S129/12900MCTDS200TS.gml` | |
| P11 | S-401 · Inland waterways | USACE IENC, Mississippi at St. Louis or Ohio River locks | Shows that this isn't only open water. Choose a stretch with bridges and locks. |
| P12 | S-131 · Harbour infrastructure | `tests/datasets/S131/harbour_typed.gml` | |

Not in the grid but listed in the standards table: S-122, S-127, S-128, S-201.

---

**P01 (2026-10-04):** real S-101 from the Canadian Hydrographic Service S-100 sample package (Nov 2025), Quebec City harbour cell 101CA00P468N0712W at 1:12 000. Unblocked by #754 (S-102 no longer suppresses S-101 depth areas outside its coverage). CHS licence: non-commercial use with the CHS notice, which is in the footer (`#chs-notice`) and credited under the tile; the data is read from a local copy (`--chs`) and never committed.

**P08 (2026-10-04):** US National Weather Service sea-ice analysis for Alaska (public domain), the latest chart from the BSH/BSIS S-411 Ice Portal (https://www.bsis-ice.de/IcePortal/). NWS publishes it in a continuous longitude frame (≈175°E–225°E), and `set_viewport` only accepts −180…180, so the recipe centres on the antimeridian at 1:10 000 000.

**The manifest:** `src/assets/shots/manifest.json` (written by capture.cs) is the exact record of each shot: datasets with source, licence, file and forecast run, the recorded viewer calls (viewport, palette, category, panels, clock), and capture times. `--manifest-only` refreshes it without replacing images; such entries have `imageMatchesRecord: false`. The descriptions below are the original plan and may not match the captures.

**Status (2026-10-03):** captured by recipe: P02, P03, P04 (S-104 over the ENC since #742), P05, P06, P08, P09, P10, P11. Still on README stand-ins:
- P01: native S-101 cells still render a blank map in the viewer after #739 (the pipeline produces instructions and the CLI renders fine). The recipe uses IHO DS0020 and skips itself while the map is empty.
- (P07 is now captured: S-125 derived from the NOAA Elliott Bay cells, portrayed with the S-101 AtoN rules since #750, over the chart at the Standard display category.)

P12 is now captured from S-131 derived from the NOAA Elliott Bay cells (berths, dolphins, anchorages, harbour facilities) by `GmlDeriver` in capture.cs.

## F: Feature rows

Use window captures here, because the chrome is part of what's being shown.
Crop to the relevant part of the window and keep the title bar, so it still reads
as a desktop app.

### F1 · Layered by the S-98 rules, not by load order (before/after slider)
- **Captured (2026-10-03) at the Chesapeake Bay entrance** (moved from Elliott Bay so all three stops are real): NOAA ENC US4VA1BE/BF → + S-102 102US004VA1BE/BF → + S-111 cbofs (latest run). The + Depths step is subtle (~11% of pixels change), because S-102 is portrayed in the same depth zones as the ENC.
- **Location:** same view as H1, at 1:25k.
- **Frames:** F1a S-57 chart only → F1b + S-102 depth shading → F1c + S-111
  currents. Use `set_dataset_state` between frames and keep the viewport fixed.
- **Use:** a three-stop slider on the page.

### F2 · Time: tides and currents on one timeline (loop)
- **Location:** Chesapeake Bay mouth (NOAA `cbofs` S-111 + S-102 twins).
  The ⚖ alternative is the Solent (S-104 + S-111), where the tide is famous.
- **State:** Timeline expanded, showing the zoomed run with axis labels, the Now
  marker and the step menu (#708 UI). Datasets panel open with layer times (#709).
- **Loop:** 6–8 s of stepping forward through about 12 steps. Then a
  **screen-recorded** 3 s of dragging the scrubber.
- **Blocked on:** Timeline slices #710 (lanes) and #711 if we want the finished
  timeline in the shot. Capture a provisional version now and redo it when
  #707 closes.

### F3 · Click anything: Object Information
- **Location:** Elliott Bay. Pick a lit buoy or a light near Alki Point
  (`pick_features`), then open the Object Information panel with decoded attributes.
- **Second variant (F3b):** pick an S-104 station so the embedded
  time-series chart appears (UKHO Solent station file ⚖ / NOAA station data if
  available).

### F4 · Plan a route
- **Location:** Seattle → Bremerton via Rich Passage, about 7 waypoints.
  Build it with `create_route` and `append_waypoint`. Mix great-circle and
  rhumb-line legs.
- **State:** Routes panel open with the leg table (distance and bearing per leg),
  and one waypoint selected on the map.
- **Optional loop:** screen recording of dragging a waypoint while the leg
  figures update live.

### F5 · Find data without hunting for files: the Library
- **State:** Library panel showing the NOAA S-102 AWS source faceted by
  region, and the map zoomed out to the US West Coast with coverage-area outlines
  (`AreaScaleThreshold` view). Include one area in the Update (amber) state.
  Second variant (F5b): S-111 forecast models with run and expiry state.
- **Data dir:** a seeded `collections.json` in a scratch `--data-dir`, as in
  the MCP validation recipe. Never use the real profile.
- **International angle:** also show the IC-ENC local collection with its
  20 country groups (group names only; nothing rendered) ⚖ check.

### F6 · Live overlays: own-ship and AIS (deferred)
- **Deferred (2026-10-03):** left off the page for now while own-ship and AIS are still experimental.
- **Location:** Elliott Bay ferry lanes, or the Strait of Singapore for a
  much busier and more international scene.
- **State:** `set_own_ship` on a ferry route, with AIS targets streaming from
  aisstream.io. Vessels panel open.
- **Caution:** live AIS is real vessel data. Either crop it so names and MMSIs
  can't be read, or use a recorded or synthetic target feed. Using a replay is better
  in any case, because the shot must be repeatable.

### F7 · Day / Dusk / Night (`F7-day`, `F7-dusk`, `F7-night`)
- Three crops of the **same** viewport side by side (H1 / H3 / H2, or a
  tighter 1:15k harbour crop). These need window captures (palette is not
  reflected in `render_to_image`).

### F8 · Validation (small card)
- **Captured (2026-10-03)** after #737/#741: NOAA ENCs and S-102 now validate clean. The `F8` recipe loads candidates over NOAA ENC US4VA1BF (the S-125 `aton_chesapeake.gml` fixture, synthetic, and NOAA's S-111 CBOFS tile, which has a few genuine 360° directions). It selects the one with the most located findings (S-125: 2 findings, both drawn as overlay markers), closes the rest and opens its Validation tab.

### F9 · ECDIS display controls (small card)
- ECDIS Display Controls panel open (display category, safety contour, four
  shades) next to a chart that shows the effect, e.g. a shallow harbour entrance.

---

## D: Developer section

| ID | Asset | Notes |
|---|---|---|
| D1 | Terminal recording: `s100 render` producing a PNG, then the PNG fading in | asciinema-style SVG or a 6 s MP4. Command: `s100 render 102US004MI1CI262227.h5 out.png --palette night -w 2048 -h 1536`. |
| D2 | MCP clip: an agent driving the viewer | **Done (2026-10-03).** Recipe `D2`: real MCP calls (create_route, append_waypoint ×6, sample_coverage_along on S-102, which finds the 0.5 m West Point shoal, move_waypoint ×2, re-check gives 11.1 m least depth), window frames composed with a transcript panel (SkiaSharp) and encoded by ffmpeg to `public/clips/D2.{mp4,webm}` (~20 s, ~640 KB each). It is a scripted demo of real tool calls, not a recording of a live agent session. Needs `ffmpeg` on PATH. |
| D3 | Code snippets | Styled text, not images: the `S100Dataset.Open` facade snippet from the README and a CLI one-liner. |

---

## B: Brand and supporting assets

| ID | Asset | Notes |
|---|---|---|
| B1 | SoundCharts mark + wordmark (SVG, light/dark) | The site uses the viewer's app icon (`src/EncDotNet.S100.Viewer/Branding/icon.svg`). The DocFX docs use it too (`_appLogoPath` in `docfx.json`). |
| B2 | App icon (macOS .icns, Windows .ico, Linux PNGs) | Already exists in `Branding/`. |
| B3 | Favicon set + `apple-touch-icon` | Done: taken from `Branding/` at build time. |
| B4 | Open Graph card, 1200×630 | Night-palette crop of H2 + wordmark + "S-100 marine charts on your desktop". |
| B5 | Platform glyphs for the download buttons (macOS / Windows / Linux) | Optional. The button already picks the visitor's platform. |
| B6 | Data attribution strip | Text only: NOAA, USACE, UKHO (if ⚖ is cleared), Natural Earth. |
| B7 | "Not for navigation" disclaimer | The README doesn't currently say this. The page footer should, and the README probably should too. |

---

## Order of work

1. Licences read (2026-10-03): UKHO and IC-ENC data stay off the site unless written
   permission arrives. Check the IHO `101AA` test dataset's terms for P01.
2. Write the capture script and take H1–H3, P02/P03/P05/P11 and F1/F3/F4/F7. All of
   these use public-domain data and shipped UI.
3. Take F5 now. Take F2 after #710/#711.
4. F6 needs an AIS replay source; D2 needs a screen recording.
5. Brand assets (B1–B4) can be done alongside all of the above.
