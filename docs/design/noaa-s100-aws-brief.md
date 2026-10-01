# Design brief: NOAA S-102 / S-104 / S-111 from AWS in the Library

Issue: #685 (part 2). Audience: UX designer. Status: brief. The data has been surveyed; no UI exists yet.

## 1. Goal

Let users find and use NOAA's S-100 products, published on AWS Open Data, from the Library:

- **S-102** bathymetric surface
- **S-104** water level
- **S-111** surface currents

The data is far too large to fetch in full and refreshes continually. Users need to:

- see what exists and where, without downloading it
- pull down just the part they care about
- keep that part current with little effort

These products differ from the ENC sources the Library already handles, because two of the three are **time-varying forecasts**, not charts with editions. The main design problem is fitting forecasts into a panel built around "online / local / outdated datasets".

## 2. Where this lives today

Read these to see the current UI; the design has to fit inside it:

- `Library` panel (left dock), as refined in the library-panel handoff (#687). It has:
  - source nodes with a kind tag (`DIR`/`ZIP`/`WEB`/`LIST`/`FEED`/`S-128`) and a status line
  - an `All / Local / Online / Updates` state filter
  - a bulk bar ("6 to download · 16,4 MB  [Download 6] [On pan]")
  - a map coverage overlay outlined by availability, and tap-to-filter on the map
- Availability states per dataset: **Online** (indexed, not downloaded), **Local**, **Loaded**, **On pan** (deferred load as you pan), **Update** (a newer edition is online).
- **Online catalogue directory** ("+ Add ▾ › Browse online catalogues…"): the curated `known-sources.json`, with quality chips and a stale flag for catalogues older than 365 days. Choosing NOAA ENC or USACE opens an **Add to Library** dialog with facet pickers: states, Coast Guard districts and regions for ENC; rivers for USACE. The result is a filtered collection.
- Downloads go to the user's data folder, run 3 at a time, and are cancellable. A per-item "Update" uses the edition comparison.

## 3. What the data looks like

Measured on 2026-09-30. Every catalogue below already parses with our reader.

### S-102 bathymetry (`noaa-s102-pds/ed3.0.0/`)

| | |
|---|---|
| Size | **17.3 GB**, 5,209 tiles, ~3 MB each (max 22 MB) |
| Organisation | Region → Area folders: 14 regions, 113 areas (e.g. *Northeast → Long Island Sound*: 164 tiles, 0.55 GB). Biggest area is *Alaska → Southeast*: 328 tiles, 2.0 GB. |
| Resolution / purpose | **Port** 4 m (3,770 tiles, 11.8 GB), **Transit** 16 m (1,418 tiles, 5.5 GB), a few 2 m tiles |
| Catalogue | One catalogue lists all of it. Fetching it costs **638 KB** gzipped, and it is regenerated daily. It includes a footprint polygon per tile, but no file sizes; sizes come from one cheap bucket listing per area. |
| Change pattern | Editions, like ENCs. Nearly every tile was reissued between June and September 2026, so most downloaded tiles go out of date within a quarter. Old editions are deleted from the bucket. |
| Navigation use | Every tile is flagged *not for navigation* |

### S-111 surface currents (`noaa-s111-pds/ed1.0.1/model_forecast_guidance/`)

| | |
|---|---|
| Organisation | **14 forecast models**, one per water body, e.g. Chesapeake Bay (cbofs), Lake Michigan & Huron (lmhofs), Gulf of Maine, Tampa Bay, San Francisco Bay, US East (rtofs_east). They overlap in places. |
| Cadence | A new **run** every 6 h for most models (some once a day). Each run is a forecast covering roughly the next 2 days. |
| Retention | About 31 days of runs, ~6.8 GB per model |
| Per run, three shapes | **Tiles**: 5–157 per model, 0.2–0.7 MB each, on the *same tile grid as S-102*. **Whole-area file**: one file covering the model's whole domain, 1–128 MB. **Zip** of all the tiles. |
| Catalogue | One per model, overwritten with the **latest run only** (≤ 900 KB). |
| Scale | The latest run of every model, as tiles, is **≈ 494 MB** in total. lmhofs alone is 186 MB, nyofs 2 MB. |

### S-104 water level (`noaa-s104-pds/ed2.0.0/`)

A pilot: 4 tiles for Charleston from a single forecast run in December 2025, untouched since. In practice it is stale, but it will grow.

### Shared traits

- All three are US-only, free, anonymous HTTPS, and need no account.
- The bucket paths contain the product-spec edition (`ed3.0.0`). When NOAA moves to a new edition, the remotely refreshable known-sources list (#685 part 1) will point at the new path.
- S-102 and S-111 tiles share a grid. "Bathymetry plus currents for these tiles" is an exact match, not an approximation.

## 4. Proposed model (engineering's starting point; please challenge it)

1. **Three entries in the online catalogue directory**, one per product, not one "NOAA AWS" entry. Their behaviour differs too much.
2. **S-102 = browse everything, download on demand.**
   - Indexing the whole product costs one 638 KB request, so coverage is shown everywhere at no cost.
   - Adding it opens the familiar facet dialog: **Region → Area** and **Port / Transit**. The dialog shows estimated sizes.
   - The user downloads individual tiles or the filtered set.
   - "On pan" could optionally download as well as load, within a user-set disk budget.
   - Batch **"Update downloaded (N)"** matters more than per-item badges, because nearly everything goes out of date each quarter.
3. **S-111 = the model is the unit; only the latest run matters.**
   - The user picks models. A dataset is *model + tile*.
   - A newer run supersedes the old one automatically, and only the latest run per model is kept on disk.
   - New state: **forecast expired** (the run's time window has passed), which differs from "a newer edition exists".
   - Shape choice: per tile (the default; selectable, aligned with S-102) or the single whole-area file.
   - The catalogue is rechecked about hourly, not on the ENC 15-minute/conditional cadence.
   - Browsing older runs ("show me last Tuesday") is *later*.
4. **S-104** is listed with a "pilot / stale" quality chip, using the same mechanics as S-111.

## 5. Questions for design

1. **Collection or live layer?** Should S-111 (and S-104) be a normal Library collection with download states? Or a "live" source the user turns on per model, which keeps itself current, with downloading as an implementation detail? What does each look like in the tree, the state filter and the bulk bar?
2. **Time.** How do we show a run's issue time, the forecast window, and the move to expired?
   - In the tree row, the detail pane and on the map.
   - Is there a time control (the forecast hour shown), or is that renderer territory and out of scope here?
3. **Expired vs Update.** Is "forecast expired" a sixth availability state with its own swatch and outline? Or a variant of Update? How does it interact with the `Updates` filter segment?
4. **Choosing S-102 areas.** Is the Region → Area facet tree enough with 113 areas? Or should the dialog let users select on the map: draw a box, or tap areas or tiles, with a live size total? The existing dialogs are list-only.
5. **Size awareness.** Where do size estimates and running totals appear (dialog, bulk bar, node status line)? Do we need a disk-budget setting, and where?
6. **Download as you pan.** Do users want "On pan" to fetch tiles automatically within a budget? Or must every download be explicit? If automatic, how do we show that bytes are being spent?
7. **Mass updates.** S-102 will flag most local tiles as out of date every few months. How do we avoid a wall of Update badges? A node-level summary plus one action, auto-update, or something else?
8. **Cross-product pairing.** Should the UI expose the shared tile grid, e.g. "Also get currents for these 24 tiles" when S-102 tiles are selected, or a combined "Chesapeake Bay: bathymetry + currents" preset? Or leave the products independent?
9. **Overlapping S-111 models.** Some water bodies are covered by a regional model and by rtofs_east/west. Do we explain or rank these, or just list the models?
10. **S-104 now.** Show the stale pilot (chip and all), or hide it until it has real coverage?
11. **Not for navigation.** All S-102 data is flagged *not for navigation*. Where, if anywhere, should that be surfaced?

## 6. Constraints

- Desktop Avalonia app, ShadUI theme. Strings are localisable. Visual language follows the library-panel refinement (#687): kind tags, status-line dots, sentence-case availability, the bulk bar.
- Must work offline from cached indexes. Cached index ages are already surfaced per source; reuse that.
- No accounts or keys. Never fetch everything implicitly. Every multi-GB path needs an explicit user action or a user-set budget.
- Out of scope: rendering of S-102/S-111 (styling, current arrows, time stepping) beyond saying where a time control would sit, if one is needed.

## 7. Requested deliverables

- A mock (same format as the library-panel handoff: annotated `.dc.html` + `HANDOFF.md`) covering:
  - the three directory entries
  - the S-102 add dialog, with whatever area-selection approach is recommended
  - S-102 and S-111 nodes in the Library tree, in their states (online, partially local, updates pending, expired forecast, offline/cached)
  - the bulk bar and detail pane for an S-111 tile
- Answers or recommendations for the questions in §5, so engineering can split the work into slices.
