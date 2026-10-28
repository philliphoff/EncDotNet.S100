# SoundCharts

SoundCharts is the desktop viewer for IHO S-100 data. It loads any mix of the
supported products onto one interactive map, aligned in time, and lets you
browse collections of charts, identify features, check validation findings and
plan routes. It runs on macOS (Apple silicon), Windows and Linux, needs no
native HDF5 libraries and uses no commercial S-52 assets. The project is
`EncDotNet.S100.Viewer`; it's built on Avalonia 12 and Mapsui 5 and uses the
EncDotNet.S100 libraries for all product-specific work. The website is
[soundcharts.app](https://soundcharts.app).

This page is the SoundCharts user guide. Notes for people working on the viewer
itself are in [For contributors](#for-contributors) at the end.

## Install

Each [GitHub release](https://github.com/philliphoff/EncDotNet.S100/releases)
has self-contained builds, so you don't need to install .NET:

| Platform | Asset |
|---|---|
| macOS (Apple silicon) | A signed and notarized `.dmg`, and the `.app` as a `.tar.gz` |
| Windows (x64, Arm64) | `.zip` |
| Linux (x64, Arm64) | `.tar.gz` |

For the install steps on each platform, see
[Getting started](../../docs/getting-started.md#desktop-app).

### Nightly builds

Nightly builds of SoundCharts are published from `main` to the
[`nightly` prerelease](https://github.com/philliphoff/EncDotNet.S100/releases/tag/nightly)
when `main` has changed since the last one. Use them to try a fix or feature
before it's released. They're unsupported: they pass CI but get no other
testing, and they can have bugs or unfinished features.

The assets are signed like a release's and keep the same names, so their links
always get the newest nightly: `SoundCharts-nightly.dmg` and
`SoundCharts-nightly-osx-arm64.tar.gz` for macOS,
`SoundCharts-nightly-win-x64.zip` and `SoundCharts-nightly-win-arm64.zip` for
Windows, and `SoundCharts-nightly-linux-x64.tar.gz` and
`SoundCharts-nightly-linux-arm64.tar.gz` for Linux.

- **About** shows a **Nightly build** badge next to the version, such as
  `0.25.2-nightly.20261022`: the next patch release plus the build date.
- A nightly still tells you when a newer stable release ships. It never offers
  another nightly.
- Nightly and stable builds share the same settings, Library and other app
  data, so each reads what the other saved. On macOS they're also the same
  app, so a nightly replaces the installed SoundCharts.
- To go back to a stable build, install the
  [latest release](https://github.com/philliphoff/EncDotNet.S100/releases/latest)
  over the nightly.

### Linux runtime prerequisites

The Linux archives bundle the .NET runtime and SkiaSharp's native
`libSkiaSharp.so`. The bundled Skia is the self-contained build, so Skia itself
doesn't need the system fontconfig or FreeType. The archives don't bundle the
system libraries that the .NET runtime and the Avalonia X11 windowing stack
load from the operating system. Desktop distributions usually have them;
minimal, server and container images usually don't. On Debian or Ubuntu:

```bash
sudo apt-get update
sudo apt-get install -y \
  libicu74 \
  fontconfig fonts-dejavu-core \
  libx11-6 libice6 libsm6 libxext6 libxrender1 libxi6 libxcursor1 libxrandr2 \
  libgl1 libegl1
```

On Debian 12 or older Ubuntu releases, install the matching ICU package
instead of `libicu74`, such as `libicu72` or `libicu70`.

| Group | Packages | Why |
|---|---|---|
| Globalization | `libicu` | .NET needs ICU at startup. Without it, the app stops with `Couldn't find a valid ICU package`. |
| Fonts | `fontconfig` and a font package, such as `fonts-dejavu-core` | Text and labels. Skia loads without fontconfig, but with no fonts installed, labels are blank. |
| Windowing (X11) | `libx11-6 libice6 libsm6 libxext6 libxrender1 libxi6 libxcursor1 libxrandr2` | Avalonia creates its window through X11. |
| GPU / GL | `libgl1 libegl1` | OpenGL and EGL acceleration. SoundCharts falls back to software rendering if they don't work, but the loader still resolves them. |

You also need a display server: X11, or Wayland through XWayland.
SoundCharts is a GUI application and has no headless mode.

### Run from source

From the repository root, with the .NET SDK installed:

```bash
dotnet run --project src/EncDotNet.S100.Viewer
```

## Supported products

| Standard | Subject | Encoding | Portrayal | Validation rules |
|---|---|---|---|---|
| **S-101** | Electronic Navigational Charts | ISO 8211 | Lua (S-100 Part 9A) | Yes |
| **S-102** | Bathymetric Surfaces | HDF5 | Coverage | Yes |
| **S-104** | Water Level Information | HDF5 | SoundCharts' own: S-104 defines none ([details](../EncDotNet.S100.Datasets.S104/README.md#portrayal)) | Yes |
| **S-111** | Surface Currents | HDF5 | Arrow symbols | Yes |
| **S-122** | Marine Protected Areas | GML | XSLT | Yes |
| **S-124** | Navigational Warnings | GML | XSLT | Yes |
| **S-125** | Marine Aids to Navigation | GML | XSLT | Yes |
| **S-127** | Marine Resources and Services | GML | XSLT | Yes |
| **S-128** | Catalogue of Nautical Products | GML | XSLT | Yes |
| **S-129** | Under Keel Clearance Management | GML | XSLT | Yes |
| **S-131** | Marine Harbour Infrastructure | GML | Lua | Yes |
| **S-201** | Aids to Navigation Information (IALA) | GML | XSLT | Yes |
| **S-411** | Sea Ice Information | GML | XSLT | Yes |
| **S-421** | Route Plans | GML | XSLT | Yes |
| **S-401** (IEHG) | Inland ENC | ISO 8211 | Lua (Part 9A) | No |
| **S-57** (legacy) | Electronic Navigational Charts (Edition 3.1) | ISO 8211 | Through S-101 | Through S-101 |

You can load any combination of these at once. Time-varying products are drawn
at the same view time.

## Open data

Use the **File** menu, or drag files and folders onto the window:

- **File** > **Open Dataset...** opens one dataset file.
- **File** > **Open Exchange Set...** opens an exchange-set folder.
- **File** > **Open Exchange Set (ZIP)...** opens a zipped S-100 exchange set.

SoundCharts accepts the following.

| What | How to open it | What loads |
|---|---|---|
| S-100 exchange set | A folder that contains `CATALOG.XML`, or a `.zip` of one | Every dataset the catalogue lists. Some products, such as the JCOMM/IHO S-411 sample sets, spell the file `catalogue.xml`; that works too. |
| S-57 (or S-63) exchange set | A folder that contains `CATALOG.031`, or the `CATALOG.031` file itself | Every base cell the catalogue lists, with each cell's sequential updates (`.001`, `.002` and so on) from the set applied. |
| Folder of ENC cells | A folder of base cells (`.000`) with no `CATALOG.031` or `CATALOG.XML` | Every base cell, detected as S-57 or S-101 one by one, with the update files next to each cell applied. |
| Single dataset | A `.h5` file (S-102, S-104, S-111), a `.gml` file (any GML product) or a `.000` cell (S-101 or S-57) | The dataset. A `.000` cell also picks up the `.001`, `.002` and later updates next to it. |

Zipped S-57 exchange sets aren't supported. This matches the `s100 validate`
command, which also reads S-57 exchange sets from a folder.

An S-57 or S-100 exchange set shows a signature and integrity badge on its row
in the **Datasets** panel.

If you drop a folder that isn't an exchange set or a folder of cells, such as a
folder that holds many exchange sets, the **Add to Library** dialog opens
instead. See [Browse collections in the Library](#browse-collections-in-the-library).

When you open a folder of cells, SoundCharts frames the map on the folder
before the cells finish loading. It reads each S-101 cell's extent once and
caches it, so later opens don't read the cells again. S-57 cells have no quick
way to read their extent, so the map frames them when they load.

### S-57 cells

SoundCharts detects S-57 base cells from their ISO 8211 header and translates
them to the S-101 model, so they're drawn by the S-101 portrayal. This is a
best-effort translation, not an S-52 implementation. For details, see
[S-57 to S-101 translation](../../docs/s57-to-s101.md).

### Large exchange sets

When an S-57 exchange set has more than 50 cells, SoundCharts doesn't read
every cell when you open it:

- Every cell is listed in the **Datasets** panel straight away, dimmed, and
  its footprint is outlined on the map.
- A cell is read and drawn when it comes into view at a scale that suits its
  usage band.
- Cells that leave the view are unloaded, least recently used first, once a
  memory budget is reached.

This keeps the window responsive and bounds memory use however large the set
is.

The list of cells in a set's `CATALOG.031` is cached between sessions. When
you open the same set again, SoundCharts uses the cached list and doesn't parse
the catalogue. If the catalogue file changes (its modified time or size), the
cache entry is replaced. Cell contents are always read from disk, so the cache
never shows out-of-date chart data.

### Reopen recent files

**File** > **Open Recent** lists the last 10 datasets and exchange sets you
opened, including folders and ZIPs. A dropped `CATALOG.031` is remembered as
its folder. An exchange set reopens with the same progress display as a new
open, and entries that no longer exist on disk are skipped. **Clear Recently
Opened** empties the list.

## Browse collections in the Library

The **Library** panel keeps *collections* of exchange sets and datasets, such
as all the ENCs for one region or a folder of trial data. Adding a source to a
collection indexes what it contains without loading anything: product, name
and title, edition and update, issue date, usage band and coverage. The index
is cached, so the Library is ready at the next start and refreshes in the
background. A source that hasn't changed costs only a quick check.

Local sources are referenced where they are. SoundCharts never copies them,
and removing a collection never deletes data.

For the design, see
[Dataset collections](../../docs/design/dataset-collections.md).

### Add a source

Choose **Add** in the **Library** panel, choose **File** > **Add to Library**,
or drop a folder, `.zip`, `CATALOG` file or collection manifest onto the
panel. The menu groups sources by where the data is.

**On this computer:**

- **Folder…** scans a folder recursively for S-100 (`CATALOG.XML`) and S-57
  (`CATALOG.031`) exchange sets, zipped exchange sets and loose datasets.
- **Exchange set or ZIP…** adds one exchange set.
- **S-128 catalogue…** adds an S-128 Catalogue of Nautical Products.
- **Collection manifest…** adds named groups of local folders from a
  `.s100collection.json` file. See
  [Local collection manifests](../../docs/local-collection-manifest.md).

**Online:** **Browse online catalogues…** opens the **Add online catalogue**
wizard. See [Add an online catalogue](#add-an-online-catalogue).

**Another computer:** **Connect to a shared feed…** adds datasets served by
`s100` on another computer. See [Shared feeds](#shared-feeds).

### Add an online catalogue

The **Add online catalogue** wizard has three steps. **Back** and the step
header go back without losing your choices, and each catalogue is read only
once.

1. **Catalogue**: choose from a curated directory, grouped by region. Search
   by name, provider or country. The directory includes NOAA ENC; USACE Inland
   ENC rivers and buoy overlay; NOAA S-102 bathymetry and S-104 and S-111
   forecasts; S-124 navigational warnings from Canada over SECOM; and
   community inland ENC lists for Europe and Brazil. The chosen catalogue shows
   chips for what it provides (**Coverage outlines**, **Bounding boxes** or
   **No coverage until downloaded**; editions; sizes), its URL and a link to
   its terms.
2. **What to include**: choose **Everything** or **Only what I select**. You
   can select NOAA ENC cells by state, Coast Guard district or region; USACE
   cells by river; and community list entries by download, with a filter box.
   Cell counts and download sizes show where the catalogue gives them. Your
   selections are kept if you switch to **Everything** or to another catalogue
   and back. The catalogue's date is shown, and flagged when it's over a year
   old.
3. **Add to**: add to a **New collection** or an **Existing collection**. A
   new collection's name follows your selection until you type your own. The
   step reviews what's added, what the map shows and whether new editions are
   detected.

To add a catalogue that isn't in the directory, choose **Add a catalogue by
URL**, paste the URL and choose **Check & add**. SoundCharts reads the start of
the document and recognizes NOAA ENC, USACE Inland ENC and chartcatalogs lists
by their root element, and [S-100 feeds](../../docs/s100-feed-format.md) by
their `format` property. The catalogue is listed under **Custom**, saved in
`catalogues.json` next to `collections.json`, and you can remove it again.

#### SECOM services

SoundCharts reads S-100 data from SECOM (IEC 63173-2) services:

- **Show SECOM services from the MCP service registry** lists the S-100 data
  services in the MCP service registry by product. Provisional registrations
  carry a **Pilot** chip.
- You can also paste a service endpoint, such as
  `https://s124.ccg-gcc.gc.ca/api/secom`. A URL that answers the SECOM
  `Capability` request is recognized.

When you choose a service, SoundCharts checks whether it can read it, for
example **Readable without a certificate**, **Readable with your
certificate**, **Needs a certificate**, **Needs a signed request**, **Server
certificate not trusted** or **Not reachable**, with the reason. A service
that needs a certificate or a signed request uses your MCP identity; see
[Keys and certificates](#keys-and-certificates).

**What to include** lists the service's products with object counts. A service
with more than 5,000 objects is cut off with a warning. Objects have no
outline until they're downloaded. Each downloaded object's signature is
checked, and the result shows in its details.

- **Keep a copy of every object, synced on each refresh** downloads every
  object and deletes copies the service no longer lists or has cancelled. It's
  on by default when the selection is under 100 MB. The source's status line
  shows "Synced N of M" and the time, or why it couldn't sync. A copy that's
  open in the **Datasets** panel is kept until you close it.
- **Only objects in the current map view** narrows the source to the map view
  at the time you tick it, and the counts update. Use it for large services.

#### NOAA forecasts

The NOAA S-104 and S-111 catalogues list forecast models. In the wizard you
choose the **Models** and how to **Download as**: **Tiles**, which use the
S-102 grid and can load as you pan, or **One file per model**, which covers the
model's whole water body. Forecast sources show their runs; **Check for new
runs** (or **Refresh**) looks for a newer run, and **Update** downloads it and
replaces the run you have. Runs past their forecast window are tagged
**Expired**.

### Browse the tree

The tree lists collections and their sources:

- Each node shows a short tag for its kind and its dataset count. The tags are
  `DIR` (folder), `ZIP`, `JSON` (collection manifest), `WEB` (online
  catalogue), `AWS` (a catalogue on AWS Open Data), `LIST` (community list),
  `FEED` (shared feed), `SECOM` and `S-128`.
- A second status line appears only when there's something to say: indexing,
  a download in progress, problems, a shared feed's reachability, or the
  **Session** collection's pin hint.
- **Rename…**, **Refresh**, **Copy URL** and **Remove from library** are in
  the node's context menu and in the **More actions** (···) menu.
- **Show on map** (in a source's or collection's context menu) keeps its local
  datasets loading as you pan. It follows the source: datasets the source gains
  open after each refresh, and ones it loses close. It's remembered across
  restarts, and synced SECOM services have it on by default. All of a source's
  datasets share one row in the **Datasets** panel, however many folders they
  come from.
- **Keep downloaded** (in an online source's context menu, or **Keep
  everything downloaded and current, on each refresh** when you add it)
  downloads what the source lists after every refresh: missing items, newer
  editions and updates, and a forecast's latest run. At most 100 MB downloads
  at a time; beyond that, the status line says how much is needed. Datasets
  you downloaded yourself are never deleted. Only SECOM services also remove
  objects they no longer list.

S-128 datasets you load appear in a temporary **Session (loaded S-128)**
collection. **Keep in library** makes one permanent.

### Find datasets in the list

The list below the tree shows the selected node's datasets:

- **Filter box**: filter by name, title or product. The count shows inside the
  box. To include cancelled datasets, choose **Include cancelled** from its
  filter menu.
- **State segments**: narrow the list to **Local**, **Online** or **Updates**,
  each with its count.
- **Availability**: each row starts with a short line drawn the same way as
  the dataset's outline on the map, so the list is also the map's legend.

  | Line | State | Meaning |
  |---|---|---|
  | Solid green | **Local** | On disk |
  | Dashed blue | **Online** | Can be downloaded |
  | Dotted grey | **Listed** | Known only from a catalogue |
  | Dashed red | **Missing** | The file has moved |

- **Tags** after the name: a newer edition online ("Ed 46 available"),
  **Loaded**, **On pan**, **Queued**, **Failed · retry** (select it to retry),
  and **Package** or **Unpacked** for community list entries.
- **Details pane**: groups the metadata under **Product**, **Coverage** and
  **Source**. Its **Download**, **Zoom to** and **Load** (or **Load after
  download**) buttons are in the order you'd use them. Select a shortened
  download link to copy the full link.

### See coverage on the map

While the **Library** panel is showing, the listed datasets' coverage is
outlined on the map without loading them. The outlines follow the list,
including its filters. The **Show dataset coverage on the map** toggle turns
them off.

- Outlines use the line styles in the table above. The selected dataset is
  drawn in the accent color with a light fill.
- ENC cells show in a two-band window: the usage band that suits the current
  scale, plus the next finer one. You see what zooming in will reveal, without
  coarse cells piling up.
- Point-sized items, such as most S-124 warnings, show as small rings that
  you can select.
- Coverage near the antimeridian, such as the Aleutians and the western
  Pacific, is drawn on the correct side of ±180°.

To find every Library dataset that covers a spot, select the map outside
**Pick Mode**. A banner says how many there are, for example "3 datasets cover
this point · 1 / 3". The most detailed one is selected, and **Next ›** (or
selecting the spot again) steps through the others. **Zoom to** in the details
pane frames a dataset.

### Load datasets from the Library

- Double-click a dataset, or choose **Load** in the details pane, to load it
  now.
- **On pan** in the bar under the list registers every listed local dataset to
  load as it comes into view, for example a whole collection, or everything
  under a spot you selected.

Loading from the Library uses the same on-demand loading as
[large exchange sets](#large-exchange-sets). S-57 cells load according to
their usage band, and S-100 datasets according to their coarsest display
scale. Datasets from one exchange set share one header in the **Datasets**
panel, and reuse it if the set is already open. Online, missing and
catalogue-only items are skipped.

### Download datasets

You can download from NOAA, USACE, community lists, shared feeds and SECOM
services:

- **One dataset**: choose **Download** in the details pane. **Load after
  download** also loads it.
- **Many at once**: the bar under the list says what it acts on, for example
  "6 to download · 16.4 MB" and "Filtered set · 2 already local".
  **Download 6** downloads them, at most three at a time.
- **Progress** shows in the bar, in each downloading row (with **Cancel**), on
  the tree node and in a notification. **Cancel** in the bar stops the batch.
- A failed or interrupted download never replaces a good copy.
- A downloaded dataset is **Local** and loads like any other.
- When the catalogue lists a newer edition or update than you downloaded, the
  row is tagged with it, for example "Ed 46 available", and appears under
  **Updates**. Downloading again replaces your copy.

Downloads go to one managed folder per provider: `downloads/noaa-enc/`,
`downloads/usace-ienc/`, `downloads/community/<list>/<entry>/`,
`downloads/feeds/` and `downloads/secom/`. These aren't caches, so **Clear
caches** keeps them.

#### USACE Inland ENC

The rivers catalogue lists USACE's river cells grouped by river, with cell
counts and sizes, and adds the rivers you choose as a collection. USACE
publishes no coverage polygons, so cells show as bounding boxes. Each cell is
described by its reach and river miles, for example "Pittsburgh, PA →
Allegheny Lock No. 8 (Allegheny, mi 1–46)".

#### Community inland ENC lists

These are CC0 lists maintained by the
[chartcatalogs](https://github.com/chartcatalogs/catalogs) project, one per
authority (Austria, Brazil, EuRIS, France, the Netherlands and others).

- Each entry is a download, which can hold one cell or a whole exchange set.
  The lists give no coverage, editions or sizes, so an entry has no outline
  until it's downloaded.
- Before you download it, an entry is listed by its description and tagged
  **Package**.
- After you download it, the source re-indexes and the entry becomes a group
  tagged **Unpacked**. Its datasets are listed under it with their outlines,
  editions and updates, and a notification says how many it held.
- A cell is tagged **Update available** when the list publishes a newer
  download than the one you have.

#### Shared feeds

A shared feed serves datasets from another computer:

1. On the computer with the data, run:

   ```bash
   s100 feed serve <folder> --host 0.0.0.0
   ```

   `--host 0.0.0.0` lets other computers reach it.

2. In SoundCharts, choose **Add** > **Connect to a shared feed…** and paste the
   URL that the command prints.

The feed is named after the serving computer, and its tree node says whether
that computer is reachable. The access token in the URL is never shown in
full; **Copy URL** copies it. You can choose which products to include (S-57,
S-101, S-102 and so on), with counts and sizes. Datasets show their real
coverage before you download them. Each download goes to its own folder under
`downloads/feeds/` and loads straight away. SoundCharts checks the feed again
at most once a minute, so datasets added on the serving computer appear after
a refresh.

### Where the Library stores data

Collections are stored in `collections.json`, next to `settings.json` (or under
`--data-dir`). Indexes and downloaded catalogues are caches that SoundCharts
can rebuild.

## Manage loaded datasets

The **Datasets** panel has two tabs above an inspector:

- **EXCHANGE SETS** nests each dataset under the exchange set it came from.
  Each exchange-set row shows a member count, the set's signature badge, a
  show/hide toggle for every dataset in the set, and a close button that
  unloads the whole set. Loose files don't appear on this tab.
- **DATASETS** lists every loaded dataset in render order. Each row has a
  visibility toggle and an opacity control. Change the order by dragging, with
  the up and down buttons, or from the context menu (**Bring to front**,
  **Send to back**, **Move up**, **Move down**). The context menu also has
  **Isolate (hide others)**. Toolbar buttons show every dataset, hide every
  dataset, and reset every opacity to 100%.

The inspector shows the selected item. For a dataset, it has **DATASET**,
**LAYERS** and **VALIDATION** tabs. For an exchange set, it shows the set's
producer, issue date, dataset count, signature and source path. You can drag
the divider between the tabs and the inspector, and its position is saved.
The panel opens on **EXCHANGE SETS** unless only loose datasets are loaded.

Double-click a dataset row to load it, if needed, and zoom the map to its
extent. This is the quickest way to find one member of a widely spread
exchange set, including one that's [out of scale](#out-of-scale-outlines).

Some S-111 and S-104 exchange sets contain several variants of the same
product for the same cell. For example, the Rotterdam (NL) S-111 set publishes
the same grid as separate neap and spring, and depth-band, products under one
dataset name. Their arrows would draw on top of each other and look like
several time steps at once. SoundCharts shows the first variant and loads the
others hidden; their rows are dimmed. Turn one on from the list to compare
variants.

## Navigate the map

The map fills the center of the window. Pan and zoom with the mouse wheel,
trackpad or touch. The map toolbar has **Zoom In**, **Zoom Out** and **Zoom to
Extent** buttons, and you can drag the compass rose to rotate the map. A scale
bar at the bottom uses your **Distance Units** setting.

The map is drawn in Web Mercator (EPSG:3857). Coverage grids in UTM
coordinate reference systems, which are common for S-102, are reprojected as
they're drawn.

### Basemap

Choose the basemap in **Settings** > **Chart display** > **Basemap**:

- **Offline (Natural Earth)**, the default, draws the bundled public-domain
  Natural Earth 1:10m land with no network access. It uses a level of detail
  that matches the zoom, for the tiles in view only.
- **Online (OpenStreetMap)** draws OpenStreetMap tiles and caches them on disk.
- **None** draws only the chart's water background.

Use **None** or **Offline** to work offline.

The offline basemap repeats one world to the east and west, and chart data
does the same: each dataset is drawn in its own longitude range and one world
either side, wherever those copies are in view. So a dataset stored in a
0…360° range (such as the NIC Arctic S-411) or one that runs continuously
across ±180° (such as the US NWS S-411 sea-ice product, about 175°E to 225°E)
shows on both sides of 0° and ±180°, over matching land. Picks, the pick
highlight, extent outlines, the overscale pattern and validation findings
follow the copies. **Zoom to Extent** still frames the dataset's own range.

Limits:

- Zoomed out past about two worlds, the basemap can extend past the data's
  last copy.
- The **Single surface** scene mode (**Settings** > **Advanced** > **Scene
  mode**) draws only the dataset's own range.
- OpenStreetMap tiles aren't repeated. A dataset that extends east of +180°
  has no online tiles under that part; use the offline basemap for such
  datasets.

### Out-of-scale outlines

An S-101 dataset stops drawing when you zoom out past its smallest display
scale. If an exchange set covers far-apart areas, framing all of it can zoom
out so far that every member disappears. So while a loaded, visible dataset is
out of scale, SoundCharts draws a thin dotted accent-colored border around its
extent. Zoom in on the border, or double-click the dataset's row in the
**Datasets** panel, to see it. Zooming back in hides the border.

Turn the borders off with **Settings** > **Chart display** > **Out-of-scale
dataset outlines** (on by default). They don't appear when **Ignore scale
minimum** is on, because datasets then never drop out.

### Overscale indication

When you zoom in past a chart's finest compilation scale, the status bar shows
**OVERSCALE** with the overscale factor. Select it to see each overscaled cell
in view. With **Settings** > **Chart display** > **Overscale indication** on,
the overscaled area also has the S-52 vertical-line pattern as a caution.

## Control the layer stack

The **Layer stack** panel lists every visible layer in its S-98 display
plane, grouped within each plane by S-98 priority. The planes include **Base
chart (under)**, **Bathymetry**, **On-demand surface**, **Base chart (over)**,
**Other chart overlays**, **Cautions and warnings**, **Dynamic arrows**,
**Mariner overlay** and **ECDIS alerts**. **Show empty planes** shows planes
with no layers. The basemap is always at the bottom, and map tools (the
measure tool and validation findings) are always at the top.

Rows include:

- A row for each loaded dataset, named after the dataset.
- Sub-layer rows when a product draws more than one layer, such as S-111
  currents, which draws an arrow layer.
- A row for each live overlay, such as **Own ship** (under **Dynamic
  arrows**) or **NDBC buoy observations** (under **Other chart overlays**).
- A colour legend under each weather fill (wave height, wind speed): a swatch
  and a label for each band, such as **2–2.5 m** or **Force 3, 7–10 kn**, in
  the palette the map is drawn in. Legends appear only in this panel, not on
  the map.
- Optional weather layers, listed but off until you turn them on: **Isobars**
  (under **Other chart overlays**, only for a file with mean sea level
  pressure), the **Wind speed** fill and the **Wave direction** arrows. Their
  rows have a **Show on map** checkbox. It's the same switch as the layer's
  eye in the **Datasets** panel, and it stays as you set it while the dataset
  is loaded, through time steps, palette changes and redraws.

Each dataset row has an **Active** toggle. When it's off, the dataset is taken
out of the cross-product stack: it no longer takes part in S-98 rules or
picking. Visibility, in the **Datasets** panel, only hides it.

The S-98 interoperability rules decide the order across products, not the
order you loaded them in. For example, when an S-101 ENC and an S-102
bathymetric surface cover the same area, the bathymetry goes on a plane drawn
beneath the chart. See
[S-98 interoperability](../../docs/design/s98-interoperability.md).

## Change the display

Use the pill buttons on the map toolbar (**Display: Standard ▾** or the
current category, **Text ▾** and **Palette ▾**) or the **Display controls**
panel:

- **Display category**: **Display Base**, **Standard**, **Other Information**
  or **All**. Changing it redraws every vector dataset.
- **Display planes**: **Under Radar** and **Over Radar** toggles (S-100 Part 9
  §11.6).
- **Overlays** › **Navigational warnings** (Display controls panel): on by
  default and kept after a restart. Off, the map draws no S-124 warning at
  all, whatever its status, and no warning pick highlight (S-98 Edition 2.0.0
  §6.1.1, §12.12.2). Each warning dataset's own visibility in **Datasets**
  keeps its state. See [Which warnings the map draws](#which-warnings-the-map-draws).
- **Text**: toggles for the three S-101 text viewing groups, **Important
  Text**, **Other Text** and **All other chart text**.
- **Palette**: **Day**, **Dusk** or **Night**, the three S-100 Part 9 mariner
  palettes. Coverage products (S-102, S-104, S-111) switch palette too. You can
  also set it with **Settings** > **Appearance** > **Color Profile**.
- **Ice display mode** (S-411): shown when an S-411 dataset is loaded. Choose
  **Concentration**, **Stage of development** or **Navigational ice** (S-100
  Part 9 §11.7). This is separate from the display category.
  **Navigational ice** is provisional: it's derived from total concentration,
  not a POLARIS or RIO navigational-risk calculation, and its tooltip says so.
- **Show warnings not yet in force** (S-124): shown when an S-124 dataset is
  loaded; off by default and kept after a restart. When it is on, the map
  also draws a navigational warning from its publication time until it comes
  into force. It has no effect while the **Navigational warnings** switch is
  off. See [Which warnings the map draws](#which-warnings-the-map-draws).

The **Display controls** panel also lists each loaded vector product's viewing
groups, so you can hide or show particular symbol families:

- Labels come from the product's portrayal catalogue. Where those names aren't
  usable, such as the bare numeric IDs in S-127 and S-421, SoundCharts uses
  its own labels.
- For S-101, the viewing groups are grouped into sections such as depths, aids
  to navigation, alert highlights and mariner selectors. Groups without a
  section are under **Other**.
- Three S-101 mariner selectors are hidden by default, even in **All**, to
  keep the chart readable and panning smooth: the shallow water pattern
  (90000), survey accuracy and quality (90010), and the low-accuracy marker
  (90011). Turn them on in this panel.
- **Reset overrides** clears hidden viewing groups for one product, and
  **Reset all overrides** clears them for every product.

All of these choices are saved between sessions.

### Mariner settings

**Settings** > **Chart display** > **Mariner Settings** sets the S-101 and
S-102 portrayal parameters (S-100 Part 9 §4.2). Changes redraw every loaded
dataset.

- **Depth Unit**
- **Safety Contour**, **Safety Depth**, **Shallow Contour** and **Deep
  Contour**
- **Four-shade depth scheme**
- **Highlight shallow-water dangers**
- **Plain boundaries**
- **Simplified symbols**
- **Full light lines**
- **Radar overlay mode**
- **Ignore scale minimum**
- **National Language**: the 3-letter ISO 639-2/B code for chart text, or
  **(Catalogue default)**.

## Identify features

Turn on **Pick Mode** (the cross-hair button on the map toolbar, **View** >
**Appearance** > **Pick Mode**, or the **I** key) and select a feature. The
**Object information** panel opens on the right with:

- **LOCATION**: the latitude and longitude of the point, in degrees and
  decimal minutes. The copy button copies it as signed decimal degrees.
- A hit list of every feature at the point. Select a row to show its details.
- The selected feature's class, identifier, source dataset and type name.
- Its attributes, decoded. Feature catalogue codes such as `CATPLE` are shown
  by name ("Category of pile"), and enumerated values by their labels. You can
  collapse complex attributes.
- Links that open the feature, or each attribute, in the
  [S-100 Feature Catalogue eXaminer](https://s100examiner.com/) in your
  browser. They appear only for products the eXaminer hosts. Turn them off, or
  point them at a mirror, in **Settings** > **Integrations** > **S-100 Feature
  Catalogue eXaminer**. The **Feature catalogues** panel has the same link for
  each catalogue.
- **REFERENCES**: every `xlink:href` the feature carries. Select one to show
  the referenced feature. This is useful for S-125 aid-to-navigation status
  and S-421 route topology.
- A time-series chart for a fixed-station observation (S-104 and S-111
  stations, data coding format 8).
- A **Depth** chart where depth data is available: the base depth from S-102
  bathymetry or S-101 depth areas, dredged areas or soundings, adjusted by
  S-104 water levels over time when they cover the point. With no water-level
  data, the depth is static.
- A **Warning card** for an S-124 navigational warning part. Its header has
  the series and number (such as "NAVAREA X 106/26"), the warning type, and
  whether the warning is **In force**, **Not yet in force** or **Ended** at
  the view time (now, when the Timeline is Live). When another warning ended
  it, a line says why: "Cancelled by NAVAREA X 104/26", or "Cancelled —
  not in in-force bulletin NAVAREA X 110/26" when the latest in-force
  bulletin for its series, published after it, doesn't list it (S-124
  §9.2.1, S-98 §12.12). Below that are its
  validity, in your Local/UTC setting, its area, and the issue date-time
  group as the message writes it. The full message text follows in a
  monospaced block, so telex columns line up. You can select the text, and
  **Copy** copies it. A part with text in several languages has a tab per
  language, with your UI language first; your choice holds for the session.
  Messages over 22 lines stop at 20 until you select **Show all**. A warning
  number in the text, such as "106/26" in a weekly "warnings in force"
  bulletin, is a link when that warning is in the **Navigational warnings**
  list: select it to open that warning, as selecting its row does. A series
  name just before the number ("AUSCOAST 314/26"), or the nearest one above
  it, says which series it belongs to; otherwise the card's own series, or
  the only listed warning with that number. Numbers not in the list, and the
  card's own number, stay plain text. **Zoom to** centres the map on a part
  with a location at a scale that fits it. The attribute rows fold into
  **Attributes** beneath the card, closed until you open them.
- **TEXT**: long text lifted out of the attribute table into cards with the
  same body as the Warning card, so it isn't cut off at the value column. An
  S-101 referenced text file (`TXTDSC` / `NTXTDS`) shows its first line and
  file name as the header. Any other text value with a line break or over 120
  characters, such as S-122 or S-127 regulation text, shows the attribute's
  name, and its row leaves the table. The text is set proportional, as prose,
  unless it is laid out in columns with runs of spaces. Texts over 22 lines
  stop at 20 until you select **Show all**; **Copy** copies the whole text,
  and **Examiner** opens the attribute in the eXaminer.
- **NOT FOR NAVIGATION** in the footer of each Warning card and text card
  whose dataset's exchange-set catalogue marks it `notForNavigation`. Hover
  over it for "The data provider marks this data as not for navigation". A
  file opened without a catalogue shows no label.
- When several warnings are at the point, each gets a Warning card in place
  of the hit list: points first, then lines, then areas, each by number. A
  closed card still shows the number, type and status. One card is open at a
  time, and its warning is highlighted on the map; select another card's
  header to open it instead. Pointing at a card previews its warning on the
  map with the same highlight. Other features at the point follow as rows
  below the cards.
- An **EGG CODE** diagram for an S-411 sea-ice or lake-ice area. The WMO /
  SIGRID-3 egg shows the total concentration at the top of the oval, with the
  partial concentration, stage of development and form of ice rows beneath it.
  A single ice type leaves out the partial row, and open water has no oval.
  Thinner fourth and fifth ice classes are to the right of their row, outside
  the oval, and snow depth is a caption beneath it. Hover over a value to see
  its feature catalogue meaning (such as "Grey Ice") and its role in the egg.

To pick once without Pick Mode, Cmd-click (macOS) or Ctrl-click (Windows and
Linux), or press and hold for about half a second.

Coverage products (S-102, S-104, S-111) can be picked too. A pick that misses
every vector feature reports the grid value at that point: depth and
uncertainty, water level and trend, or current speed and direction.

S-104 Edition 2.0.0 defines no portrayal, so the S-104 station gauges and
surface isolines are SoundCharts' own symbology, not IHO's. The panel says
"SoundCharts portrayal: S-104 Ed 2.0.0 defines no symbology" under an S-104
station or grid cell, and so does the dataset's **Dataset** tab in the
Datasets panel. [S-104 portrayal](../EncDotNet.S100.Datasets.S104/README.md#portrayal) explains what the gauges and isolines
show.

The pick is highlighted on the map so you can find it after you pan or zoom
away. A ring with a center dot marks the point. When the feature has geometry,
its outline is drawn too: an area outline with a light fill, a line, or a ring
around a point. The highlight uses your accent color, follows the light or dark
theme, and clears when you close the pick. An MCP agent that calls
`pick_features` with `select: true` shows the same panel and highlight, so an
agent can show you exactly what it picked. See
[MCP server](../../docs/mcp-server.md).

### Search for features

The **Search** panel finds features across every loaded dataset by ID, feature
type or dataset. Select a result to show the feature in the **Object
information** panel. This works even when datasets reuse `gml:id` values for
different features.

### Find navigational warnings

The **Navigational warnings** panel, beside **Library** in the activity bar,
lists every S-124 warning part in the loaded datasets, including parts with no
location. Nothing is drawn for those, so this list is the only way to reach
them. Parts are grouped by series and source, with the newest number first;
choose **Published** in the sort menu to put the most recently published
first, or **Classification** to list on-chart warnings first, then off-chart,
then information ones. Rows show the number, the warning type, and the area
and validity. A text-only part shows a document glyph and "No location".

- The summary line counts the warnings in force and the text-only ones. Select
  the text-only count to list only those; select it again to show all.
- Search by number, type or area.
- **View time** (the default) reads each warning's status at the Timeline's
  view time, so the list matches the map; **Now** reads it at the current
  time. Warnings not in force then fall into a dimmed **Not in force** group
  at the bottom, closed until you open it, with "Ended" or "Starts" and the
  date, or why another warning ended it ("Not in bulletin NAVAREA X
  110/26", "Cancelled by NAVAREA X 104/26"). Cancellations and in-force bulletins count here, in
  the summary and in the rail badge, as they do on the Warning card and in
  route checks.
- When a series' latest in-force bulletin lists warnings that aren't loaded,
  its group says so: "Listed in NAVAREA X 110/26 but not received: 103/26,
  107/26" (S-124 §9.2.2).

Select a row to open its Warning card in **Object information**. When the part
has a location and is in force, the map centres on it and highlights it. A
part that isn't in force opens its card but leaves the map where it is, and a
text-only part shows a note over the map saying nothing is drawn for it. With
no S-124 datasets loaded, the panel offers **Open Library**.

#### Classify warnings on chart, off chart or information

S-98 Edition 2.0.0 §12.12.2 asks that you can classify each warning's
relevance to your intended route:

- **On chart** (the default): relevant to the route, drawn while it is in
  force.
- **Off chart**: not relevant to the route, for example a danger in water too
  shallow for the ship to reach. Not drawn.
- **Information**: relevant to the route, for information. Not drawn.

Each row ends with a glyph for its classification: a faint map for on chart,
and an amber crossed eye (off chart) or "i" (information) otherwise. Select
the glyph to change it. The Warning card has the same choice under
**Classification**. The classification covers every part of the warning and
is kept after a restart and a refresh. It is keyed by series and number with
the year, so a cancellation or a replacement warning, which has its own
number, starts on chart. Off-chart and information warnings stay in the list
and are never removed. Route checks still report them, with the
classification at the end of the finding's details. For now the
classification is the same for every route, not per route.

#### Which warnings the map draws

The map draws a warning only while it is in force at the view time, as S-98
Edition 2.0.0 §12.12 asks of an ECDIS, so the map and the list agree:

- Above every other rule, the **Navigational warnings** switch in **Display
  controls** › **Overlays** turns all warning drawing off (S-98 §6.1.1,
  §12.12.2): no parts, affected areas, texts or pick highlight, not even for
  the open Warning card. The panel then says "Not drawn: Navigational
  warnings are off in Display controls", with **Turn on** to draw them again.
  The list, the Warning cards, opening a warning from the list, links and
  route checks keep working; picking on the map finds no warning.

- An ended or cancelled warning is never drawn. Cancelled includes a passed
  cancellation date, a cancellation warning, and a warning missing from the
  latest in-force bulletin for its series.
- A warning classified **Off chart** or **Information** isn't drawn, nor are
  its affected areas and texts, until you classify it **On chart** again.
- A warning not yet in force is drawn only when **Show warnings not yet in
  force** is on (in the map's **Display Settings**), from its publication
  time until it starts.
- Each part of a warning is judged on its own dates, so a part that has ended
  disappears while the warning's other parts stay. Its affected area and text
  go with it.
- The warning on an open Warning card stays drawn, whatever its status or
  classification, so
  **Zoom to** and the highlight show where it is. It goes when the card
  closes.
- A warning that isn't drawn can't be picked on the map; find it in the list.

The **Not in force** group says "Not drawn on the chart", or "Ended ones
aren't drawn" with the setting on, and a row the map still draws ends with
"Drawn". These notes follow the view time, so they show while the panel reads
status at **View time**.

When the view time is pinned and S-124 data is loaded, the time stamp at the
top left of the map starts with "Display Not Real Time" and names the view
time, and it stays on the map while the time is pinned (S-98 §12.7.2).

#### How fresh the list is

Most warnings come from SECOM services (AMSA, KHRA and others) or online
feeds that the Library syncs. For those, each group header names the source
and when it last answered, such as "AMSA · 07:40", in your Local/UTC
preference; with several sources, a series appears once per source. Warnings
from a file you opened directly keep the issuing authority in the header.

- **Refresh**, beside the sort menu, asks every source of the listed warnings
  again, through the Library's refresh and sync. It is off when the warnings
  only come from files.
- When a source's latest refresh fails, its group starts with "AMSA hasn't
  answered since 07:40. You're seeing the warnings received then." and
  **Retry**, and the header reads "AMSA · 07:40, stale".
- **NEW** marks a newly received warning until you open its card, or until
  24 hours have passed since it was received, whichever comes first (S-98
  Ed 2.0.0 §12.12.1). Opening the card, from the list, the map or a link, is
  what counts as displaying it; panning past a drawn warning doesn't. A later
  refresh doesn't clear NEW, and NEW expires on its own, without a refresh.
  The summary adds "N new", and the Warnings button in the activity bar shows
  the count. "Received" means the warning first appeared in a refresh of its
  source. A source seen for the first time sets the baseline, so nothing in
  it is new until it refreshes again. A warning published well before the
  previous refresh isn't new, even when its dataset loads later. Files opened
  directly are never new. Receive times and opened warnings are kept in
  `navigational-warnings.json` in the data folder, so they survive a restart.
- **NEW options** (the filter button beside **Refresh**) narrow which new
  warnings show NEW: **Received within** a number of hours, and **Within** a
  number of nautical miles of own ship (while it's shown) or the active
  route. Both are off by default, so every new warning shows NEW. A warning
  with no location, or nothing to measure from, isn't filtered. A dot on the
  button shows that a filter is on.
- **Mark new since**, in the same options, is for a watch handover: it marks
  every warning received in the last 1, 4, 8, 12 or 24 hours NEW again, for
  the next 24 hours or until you open it.

## Work with time-varying data

S-104 water levels, S-111 surface currents and S-411 sea ice have timestamps.
SoundCharts draws every time-varying dataset at one *view time*.

### Time HUD

When a time-varying dataset is loaded, the time HUD appears at the bottom
center of the map. It shows:

- The mode: **Live** follows the current time; **Pinned** stays at a time you
  chose. Live is the default.
- The view time and how far it is from now.
- **Previous step** and **Next step** buttons, and **Live** to go back to now.
- **Open timeline**, which opens the timeline in place of the HUD. The **T**
  key does the same.

Select the time, then use the left and right arrow keys to step. In a window
less than 900 pixels wide, the timeline isn't available and the HUD is the only
time control.

Times use your **Settings** > **General** > **Time Format** choice: **Local
time** (your computer's time zone) or **UTC**.

### Timeline

The **Timeline** shows the data each layer has over time:

- Each layer has its own lane. **Collapse to strip** shows one combined strip,
  and **Show lanes** goes back.
- Highlighted ranges have data. Long stretches with no data are compressed to
  a short gap marked with its length, so clusters of data far apart in time
  stay easy to reach.
- **In map view** lists only the layers in the map view, and they set the axis.
  Layers outside the map view are counted; select the count to list them.
- **Show online** also shows data the Library knows about: dashed for online,
  outlined for data on disk. Select a band to **Get**, **Load** or **Reveal in
  Library**.
- Drag on the timeline to set the view time. Every time-varying dataset
  redraws at its sample nearest that time.
- The **Time window** presets set the visible range: **Now ± 6 h**, **Today**,
  **Next 48 h**, **This run**, **All loaded** and **In view**.
- The step menu sets what **Previous step** and **Next step** move by. **BY
  TIME** offers **10 min**, **1 h**, **6 h** and **1 day**. **BY DATA** offers
  one layer's samples, **Dataset or run boundary**, and **Data, skipping
  gaps**.
- **‹ Previous data** and **Next data ›** jump to the nearest data, skipping
  gaps.

When some layers have no data at the view time, a message says how many, and
selecting it lists them.

| Key | Action |
|---|---|
| Left arrow / right arrow | Previous or next step |
| Shift + left / right | A larger step |
| Option (Alt) + left / right | Previous or next data, skipping gaps |
| Home / End | Start or end of the axis |
| N | Go live (now) |
| + / − | Zoom the timeline in or out |
| 0 | Show all loaded data |
| T | Open the timeline |

The timeline keys work when the map or the timeline has focus.

## Validate datasets

Every product with validation rules in [Supported products](#supported-products)
is checked against rules from its IHO product specification. The **VALIDATION**
tab in the **Datasets** panel's inspector lists the selected dataset's
findings. S-401 has no rules yet; the tab says "Validation rules not yet
defined" for it.

- Each finding shows the rule ID, the severity (**Error**, **Warning** or
  **Info**), the message and the related feature: its feature object
  identifier (FOID) for vector features, or its HDF5 group path for coverage
  records.
- Select a finding that has a position or bounding box to zoom the map to it.
- Findings with a location are also marked on the map, colored by severity, so
  you can see clusters without scrolling the list.

S-57 datasets are checked twice: once against the raw S-57 records (such as
whether DSID and DSPM are present, and `M_COVR` coverage), then with the S-101
rules against the translated data. Findings from the second check are prefixed
`S101-as-S57/`, so you can tell whether a problem is in the S-57 input or in
the translation.

## Measure and plan routes

**Measure Mode** (the measure button on the map toolbar, **View** >
**Appearance** > **Measure Mode**, or Cmd+M on macOS) measures rhumb-line
distance and bearing. Select points on the map; double-click or press Enter to
finish. The status bar shows each leg and the total.

Routes are ordered waypoints joined by legs. They stay on the map and are saved
between sessions. An agent can also create and change them over the
[MCP server](#connect-ai-agents-over-mcp).

To edit a route, turn on edit mode: **Edit** at the bottom of the **Routes**
panel, the route button on the map toolbar, **View** > **Appearance** >
**Route Edit Mode**, or Ctrl+E (⌘E on macOS). Then:

- Select the water to add a waypoint at the end.
- Drag a waypoint to move it.
- Select a leg, or the ⊕ handle at its midpoint, to insert a waypoint that
  splits it. Drag the handle to place the new waypoint.
- Right-click a waypoint, or select it and press Delete, to remove it.
  Backspace removes the last waypoint.
- Turn **Edit** off, or press Esc or Ctrl+E again, when you're done.

While you edit, the route is drawn on a casing (white, or black on the Dusk
and Night palettes), other routes are dimmed, and a chip at the top left of the
map lists the gestures. Leg labels (`2,5 NM · 320°`) run along each leg and are
hidden on legs too short to fit them. Select a leg's row in the **Routes** panel
to highlight it on the map; its label always shows.

The status bar shows the latest leg's distance and bearing and the route total.

Every change to a route can be undone: Ctrl+Z (⌘Z) undoes and Ctrl+Shift+Z
(⇧⌘Z) redoes, or use the buttons under the route's name. Each route keeps its
own history, which includes changes an agent made. The route shortcuts don't
apply while you're typing in a text field.

The **Routes** panel lists your routes. **Add** creates a route. Each route's
menu has **Rename**, **Reverse** and **Remove**. Under the active route's name
are **Undo**, **Redo**, **Zoom to route** and **Reverse**. The route's
waypoints and legs are listed with each leg's distance, initial bearing and
geometry. Type a waypoint's name, or type or paste its position, and press
Enter; positions are in degrees and minutes (`50°46.024'N 001°15.558'W`), and
decimal degrees work too. A waypoint's menu has **Insert waypoint after** and
**Delete waypoint**.

A route needs only waypoints. The **Passage** card under the route's name holds
optional values for the whole route: **Draft · squat**, **Speed** (over ground,
in knots), **Departure** (empty uses the view time), **Required UKC** (static),
**Vessel** (name, MMSI, IMO, call sign, length and beam) and **Cross-track**
(one value for both sides, or `port / stbd`, in metres). A set field has a
solid border; an empty one is dashed and reads **+ Add**. Type a value and
press Enter, or clear a field to unset it. Draft, squat and UKC use the depth
unit from Mariner Settings (feet for any unit other than metres). On a new
route the card is one line; click it to open it.

Every leg inherits the passage values. Click a leg to open its detail and give
it its own speed, draft, cross-track or required UKC: an overridden field has a
blue label and a **Reset** link, and an inherited one is dashed and shows the
passage value. A leg with an override shows a blue dot and its values in the
list. The detail also switches the leg between a **Rhumb** line (loxodrome, the
ECDIS default) and a **Great circle** (geodesic), drawn as a curve, and adds a
**Note**. Safety contour and depth come from Mariner Settings, not the route.

With a speed, the route shows times. Each leg shows how long it takes, in the
list and on its map label (`2.5 NM · 320° · 12 min`), each waypoint shows its
ETA, and the line under the route's name shows the time under way and the
arrival (`9 WP · 24.1 NM · 2 h 10 min · arrives 09:21`). ETAs run from the
**Departure**; with no departure they run from the Timeline's view time (or
now, with no time-aware data loaded), move with it, and say "from view time".
Times follow your Local/UTC setting. If a leg has no speed, the ETAs stop
there and the route's line says so. The speed is the speed over ground you
enter; S-111 currents aren't applied.

**From measurement** copies the current Measure Mode path into a new route and
switches to Route Edit Mode.

The active route is emphasized on the map, and other routes are drawn more
faintly. The route model follows the S-421 route schema (route, waypoints and
legs).

Routes are saved in `routes.json` in the SoundCharts data folder, next to
`settings.json` (or under `--data-dir` or `S100_DATA_DIR`). Only values you set
are written: a route with only waypoints has no passage values, and a leg
stores a value only when it overrides the route's passage. Changes from the
editor or from an agent are saved shortly after you make them, and again when
you quit. An `--ephemeral` run loads saved routes but never writes them.

### Check a route

**Check route**, next to **Edit**, needs a route with at least two waypoints.
It checks the active route against the loaded data: land and drying ground,
chart coverage, water shallower than the **Safety Contour** and charted
dangers (from [Mariner settings](#mariner-settings)), restricted and S-122
protected areas, and S-124 navigational warnings, judged at each point's ETA.
With a draft, on the passage or on every leg, it also checks under-keel
clearance at each point's ETA: UKC = charted depth + water level − draft −
squat, against the **Required UKC** (0 m when unset). Charted depth comes from
loaded S-102, or from S-101 where there's no S-102 (lower confidence); water
level comes from loaded S-104. [Route checks and under-keel
clearance](../../docs/route-checks.md) explains every finding, its thresholds
and confidence. While it's on:

- **The map** washes the chart and colours the route by state at ETA:
  - Meets your settings, Tight (within 0.5 m of the required UKC) and Below
    your UKC setting. The Below state is drawn wider.
  - Lower confidence keeps its state's colour and is drawn dashed: the depth
    is from S-101, or the depth and water-level datums can't be confirmed to
    agree.
  - No data is grey and dotted.

  Route findings use the same tones: Must review in the Below red, Check in
  the Tight amber and Info in grey. Red is used only for Below your UKC
  setting and Must review. On the Night palette every state is dimmed to about
  a tenth of white's brightness, and the editing line is a fixed dim blue
  instead of your accent colour.

  A legend sits at the bottom left. A ring and a pill mark the minimum UKC,
  and a ghost vessel marks the cursor.
- **The panel** shows a verdict card instead of the waypoint list. The card has
  the verdict, the minimum UKC, where and when it occurs, and the sources.
  The verdict follows the clearance; a line under it explains lower
  confidence, such as "Vertical datums unconfirmed: depth and water level may
  not share a reference". Its
  window line lists the departures that meet your settings, such as
  "Departures 05:35–06:40 meet your settings". When the current departure is
  outside them, **Depart {HH:MM}** moves it to the nearest one that meets
  them.
  Below it, **Legs** lists each leg with its times and a min-UKC chip;
  hovering a leg moves the cursor to it.
  **Findings** lists what to look at, grouped **MUST REVIEW**, **CHECK** and
  **INFO**: each stretch below your UKC setting, depths charted from S-101
  only, water-level gaps (such as data ending before arrival), expired S-111
  runs, which aren't used in ETAs, and the route check's findings, such as
  land, dangers and areas the route crosses and warnings on it. A tag names
  the check behind each one. Clicking a finding zooms the map to it, outlines it dashed in its tone
  with a "Zoomed to finding" chip, and moves the profile cursor there. On the
  map, a small dot marks each finding, red for MUST REVIEW, amber for CHECK
  and grey for INFO.

  Without a draft there's no clearance: the verdict card is titled from the
  findings, such as "2 items to review", and says "Add a draft to check
  clearance" in place of the minimum UKC and leg chips. The route checks
  still run.
- **Object Information** folds to an edge tab, and clicking the tab opens it
  again.

- **The bottom dock** opens on **Clearance**, a tab next to **Timeline**. It
  shows the clearance profile along the route, against distance or ETA:
  - the seabed, hatched where it comes from S-101
  - the required-UKC band
  - the water level and the keel; where the keel is in the band or below it,
    the gap is shaded
  - leg boundaries, a strip of states and the minimum UKC

  Next to the profile, a readout shows the charted depth, water level, draft
  and UKC at the cursor, each with its source: cell, station and time step.
  **Sources** lists every dataset, datum and gap behind the result. Hovering
  the profile moves the ghost vessel, and hovering the route on the map moves
  the profile's cursor.

  Under the profile, the **Departure** band sweeps departures every 5 minutes
  across the loaded S-104 time range. Departures that meet your settings are
  green, those below your UKC setting are red, and those that arrive after the
  S-104 data ends are hatched. A knob marks the current departure. Drag the
  band, or press Left and Right, to set the departure: the check recomputes as
  you drag, and one drag is one undo step. Over a forecast longer than about
  three days, the band's step widens so it stays quick.

- **The Timeline** shows two lanes above the product groups while the
  passage has ETAs (a speed on every leg). **Passage** is a bar from
  departure to arrival with a tick at the minimum UKC in the verdict colour.
  **Departure window** shows the same runs as the Clearance band, with a tick
  at the departure; drag it to set the departure. **Follow passage**, next to
  the Pinned/Live readout, pins the view time to the ETA at the Clearance
  cursor (or at the minimum UKC), so S-104 and S-111 show what the ship meets
  there. Turning it on sets the route's departure first if it has none, so
  the passage doesn't move with the view time. Choosing another time on the
  Timeline, or going live, turns it off. Expired forecast runs keep their
  **Expired** tag on their lanes; S-111 currents aren't used in ETAs.

The route's colours follow the chart palette. Day draws it on a white
casing. Dusk and Night use a black casing and dimmer colours, and on Night the
editing line and waypoints lose most of their accent colour so they don't
glare. Every state keeps at least 3:1 contrast against its casing on Dusk and
Night, and red is used only for Below your UKC setting, which is also drawn
wider. The Clearance profile uses the same colours for the chrome theme in use.

Editing recomputes live: the route draws grey and dashed, and the verdict
dims, until the new result arrives. Turning **Check route** off restores the
map and the docks. The result is an aid for exploring the data, not for
navigation.

To run the same checks without SoundCharts, for example in CI, use
[`s100 route check`](../../docs/cli.md#route-check).

### Import and export routes

**Import route…**, at the top of the **Routes** panel, opens an S-421 or RTZ
route file (RTZ versions 1.0, 1.1 and 1.2). An S-421 file also opens as a
dataset from the Datasets or Library panel, or by drag and drop. The route
opens as a **received** route:

- It's read-only and isn't saved with your own routes. Its row says
  "received", and names its format, route ID and edition.
- An S-421 route can be drawn with its own S-421 portrayal (**Native**) or
  with the route tools (**Planning**). An RTZ route has no portrayal of its
  own, so it's drawn in Planning mode. **Check route** always draws Planning.
- **Your check settings** (draft, squat, required UKC and departure) can
  change. They're kept on this computer for the route, not written into it.
  Speed and cross-track limits stay as the file gives them.
- Opening the same route again selects it; a newer edition replaces it. An
  open RTZ route reopens after a restart while its file still exists.

To edit a received route, choose **Copy to edit** (it replaces **Edit**, or
it's in the route's menu). The copy is your own route, with a new route ID
that references the source's route ID and edition. What SoundCharts' route
can't hold, such as further schedules or extensions, is kept and written back
when you export the copy in the same format.

**Export…**, in a route's menu, writes any route as RTZ (1.2, 1.1 or 1.0) or
S-421. A received route exported unchanged keeps its route ID, edition and
status. **Also write my check settings** adds your draft, required UKC and
departure; they're left out otherwise. See [Route file
formats](../../docs/route-checks.md#route-file-formats) for what each format
holds.

## Show buoy observations

**NDBC buoy observations** shows the latest observations from NOAA National
Data Buoy Center buoys and C-MAN stations. Turn it on with its row under
**Other chart overlays** in the **Layer stack** panel; it's off until you do.

- Each station is a ring with a wind barb pointing into the wind (pennants
  50 kn, full barbs 10 kn, half barbs 5 kn) and, for buoys, the significant
  wave height, such as **1.1 m**, at 1:2,000,000 and larger.
- Observations older than 3 hours are grey; those older than 12 hours are
  removed.
- Select a station to see all its values with units in the **Pick report**:
  wind and gust in knots and m/s, wave height, periods and direction,
  pressure and its tendency, air, water and dew-point temperatures,
  visibility and tide, with the observation time and its age.
- SoundCharts fetches only the stations in the map view (plus a small
  margin), only while the row is on and the view is narrower than 25°, and
  each station at most every 10 minutes, NDBC's update interval. Stations
  that fail are retried less often, up to every 2 hours.

Not for navigation. See the
[DynamicSources.Ndbc README](../EncDotNet.S100.DynamicSources.Ndbc/README.md).

## Own ship and AIS vessels

### Show the simulated own ship

SoundCharts can show an own-ship position. It's simulated: a dead-reckoning
model that starts in the Solent at course 090° T and 5 m/s, which you steer
from the **Helm** panel, the `set_own_ship` MCP tool or the `--own-ship-*`
options. A future GPS or NMEA source can replace it without renderer changes;
see [Dynamic feature sources](../../docs/design/dynamic-feature-source.md) and
[Steerable own ship](../../docs/design/steerable-own-ship.md).

Because the position is simulated, it's off by default. Turn on **Settings** >
**Vessels** > **Own Vessel** > **Show simulated own-ship position**. The change
takes effect straight away. While it's off, SoundCharts publishes no own-ship
position, whatever the layer's visibility.

When it's on and has a position, SoundCharts opens with the map centered on the
own ship at harbor scale, instead of the whole world. `--bbox` or `--center`
and `--zoom` on the command line take precedence.

The own-ship symbol depends on the zoom:

- **Zoomed in** (the vessel is at least about 6 mm on screen): a true-scale
  hull outline, a cross at the GPS antenna position (the consistent common
  reference point, CCRP), and a heading line with an arrowhead.
- **Zoomed out**: a colored disc with the heading line and arrowhead.

Set the vessel's size and antenna position in **Settings** > **Vessels** >
**Own Vessel**: **Length (m)**, **Beam (m)**, **Antenna from bow (m)** and
**Antenna from port side (m)**. These match IEC 62388 and the AIS Type 5
dimensions A to D. Changes take effect straight away. See
[Own-ship symbology](../../docs/design/own-ship-symbology.md).

Show or hide the own ship with its row in the **Dynamic arrows** plane of the
**Layer stack** panel.

### Steer with the Helm panel

The **Helm** panel steers the simulated own ship: **Course (°T)**, **Speed
(kn)** and **Rate of turn (°/s)**, with **Port 5°**, **Stbd 5°**, **Steady**,
and **Hold** or **Resume**. Its readout shows the position, COG, SOG and
heading.

### Show AIS vessels

The AIS overlay shows live targets from [aisstream.io](https://aisstream.io),
which needs a free API key. In **Settings** > **Vessels** > **AIS Overlay**:

1. Turn on **Enable AIS overlay**.
2. Enter your **API key**, or set the `ENCDOTNET_AIS_STREAM_KEY` environment
   variable. The environment variable takes precedence. A key entered in
   Settings is stored in plain text in `settings.json`.
3. Restart SoundCharts. Changes to this section take effect at the next start.

SoundCharts doesn't subscribe to aisstream.io until you zoom in. The
subscription opens when the map view's latitude and longitude spans are both at
or below **Activate at viewport span (degrees)**, 50° by default. At start-up
the map shows the whole world, so nothing streams until you zoom in. Then the
subscription opens for the map view's bounding box and follows it as you pan
and zoom. Once open, it stays open for the rest of the session, even if you
zoom back out. Clear the field to subscribe as soon as SoundCharts starts. See
[AIS zoom-gated subscription](../../docs/design/ais-zoom-gated-subscription.md).

### The Vessels panel

The **Vessels** panel lists AIS targets nearest first. Its activity-bar icon
appears only while the AIS overlay is enabled.

- Each row shows the vessel's name, a ship-type symbol colored by class, its
  navigation status, and its range and bearing from the own ship.
- Select a row to center the map on the vessel at the current zoom. The detail
  pane below the list shows **Identity** (type, status, MMSI, call sign, IMO),
  **Motion** (speed, course, heading, rate of turn), **Relative to own ship**
  (bearing and distance), **Voyage** (destination, ETA) and **Dimensions**
  (length × beam and draught). Each field appears once the AIS report that
  carries it arrives. You can drag the divider between the list and the detail
  pane, and its position is saved.
- When the simulated own ship is on, it's pinned to the top of the list as
  **Own ship**.
- When the own ship is off, rows have no range and bearing, the detail pane has
  no **Relative to own ship** section, and the list is sorted by distance from
  the center of the map view. Because selecting a vessel centers the map on it,
  the selected vessel moves to the top.
- An empty list says why: **AIS overlay is off**, or **No vessels yet** when
  the overlay is on but hasn't received any vessels. The second is normal until
  you zoom in far enough to open the subscription.

### Follow an AIS target

The own ship can follow a live AIS target. Select a vessel and choose **Take
the helm**, in the **Object information** panel or in the **Vessels** panel's
detail pane. The own ship then takes on the target's position, course, speed,
heading and dimensions, and dead-reckons smoothly between the target's reports.

- The own-ship row shows **Helming** and the target's name, or **Waiting for**
  and the target's name until the target's first report arrives.
- The followed target is hidden from the AIS overlay, so it isn't drawn twice.
- Taking the helm selects the own-ship row, so **Release the helm** is in
  reach. Releasing it, or turning off the own ship, stops following. The own
  ship keeps the target's last position and course.
- The choice is saved and resumes at the next start.

See [Steerable own ship](../../docs/design/steerable-own-ship.md).

### Pick vessels and other live features

Select (or, on touch, press and hold) an own-ship or AIS symbol to identify it.
The **Object information** panel shows a **DYNAMIC FEATURES** section above the
dataset hits, with the source, the kind of feature, when it was last updated,
its position, course, heading and speed where known, and all its attributes
(for AIS: MMSI, vessel name, call sign and so on). One selection shows
everything under the pointer. An AIS hit has a **Take the helm** button. The
hit radius is 12 device pixels, the size of the AIS symbol's outer disc. See
[Dynamic source pick](../../docs/design/dynamic-source-pick.md).

## Settings

Open **Settings** from the gear icon in the activity bar. Settings are grouped
into categories:

| Category | What it contains |
|---|---|
| **General** | **Distance Units** and **Time Format**. |
| **Appearance** | **Accent Color**, **Chrome Theme**, **Color Profile** (Day, Dusk or Night), **Symbol Scale** and **Text Scale**. |
| **Chart display** | **Map** (**Basemap**, **Out-of-scale dataset outlines**, **Overscale indication**) and **Mariner Settings**. |
| **Vessels** | **Own Vessel** and **AIS Overlay**. |
| **Integrations** | **MCP Server** and **S-100 Feature Catalogue eXaminer**. |
| **Advanced** | **Base-plane rendering** (scene mode, performance profile and tile cache tuning) and **Maintenance**. |
| **Keys & certificates** | **Identities**, **Trusted authorities** and **System IDs**. |

**Advanced** > **Maintenance** has **Clear caches**, which deletes the on-disk
render caches and keeps your settings, and **Reset all settings**, which
returns SoundCharts to a just-installed state. Both restart SoundCharts.

### Keys and certificates

**Settings** > **Keys & certificates** holds what SoundCharts uses to prove who
you are and to decide whom to trust:

- **Identities**: MCP client certificates and their private keys, from the MCP
  management portal. **Import identity…** reads a `.p12` file or a PEM
  certificate and key, shows what the certificate says (MRN, vessel
  attributes, issuer, the authority it chains to, validity and key) and asks
  for a name. The identity in use is presented to SECOM services that ask for a
  certificate. A row warns when its certificate ends within 30 days, has ended
  or was revoked; the same warning appears next to the category name. A revoked
  identity that's in use stops being used straight away. Each identity has a
  reference ID (`sc-ident:…`) that the `set_secom_identity` MCP tool accepts.
- **Trusted authorities**: the built-in MCP root, which you can turn off but
  not remove, and roots you add from PEM files with **Add from file…**.
- **System IDs**: for protected (S-100 Part 15) datasets. Not available yet.

Private keys are kept in the platform key store: the Keychain on macOS, the
Windows certificate store on Windows, and on Linux .NET's certificate store
under `~/.dotnet/corefx/cryptography/x509stores`, which only file permissions
protect. `--ephemeral` runs keep them in memory. The `--secom-identity`
command-line option overrides the identity for its run, shown as a **From
command line** row. **Reset all settings** also deletes stored identities'
keys.

### What's saved between sessions

Settings are stored as JSON in `settings.json`, in an `EncDotNet.S100.Viewer`
folder in your per-user application-data folder. Saved settings include:

- Recent files.
- Panel layout: which panels are open in each dock, and divider positions.
- The main window's size, position and maximized state. The first time,
  SoundCharts opens at 80% of the primary screen's working area, centered, or
  maximized on screens smaller than 1366×768. After that it restores the last
  placement. If that placement is no longer visible, for example because its
  monitor was unplugged, the window moves to the primary screen. `--ephemeral`
  runs, and runs that turn on MCP from the command line, use a fixed 1100×700
  window and leave the saved placement alone.
- Palette and display category.
- Hidden viewing groups, display-plane toggles and the ice display mode.
- Mariner settings, depth and distance units.
- The own-ship overlay, its visibility, vessel dimensions and any AIS target
  it follows.
- The basemap and the base-plane rendering settings.
- Update-check preferences.
- Whether the MCP server is on, and its port.
- References to imported identities and the identity in use, trusted
  authorities you added and built-in roots you turned off. Never a private key
  or password.

Routes are saved separately in `routes.json`, and Library collections in
`collections.json`. SoundCharts migrates settings files from older versions;
values it doesn't find use their defaults.

## Logs

The **Logs** tab in the bottom dock, next to **Timeline**, lists the last
1,000 messages SoundCharts logged, newest at the bottom. Open it with
**Help** > **Show logs** or by choosing **Logs** in the dock's tab strip; it
never opens by itself. While it's hidden, its tab shows how many warnings and
errors arrived since you last looked.

- Each row shows the time, level, area (such as **Library**, **SECOM**,
  **Updates**, **AIS** or **Datasets**) and message. An identical message
  repeated in a row shows once, with a count such as **×3**.
- Select a row to see its full message, logger category and any exception.
- The level filter hides Debug messages by default. **Verbose** also records
  Debug messages; it changes only what the panel and feedback reports get,
  not the `--verbose` log level or what goes to an OpenTelemetry collector.
- Type in the search box to show only messages that contain the text.
- **Pause** stops the list moving while you read, and **Auto-scroll** keeps
  the newest message in view.
- **Copy** copies the messages shown, **Clear** empties the list and **Send
  feedback** opens the **Report Feedback** dialog, whose report includes
  these logs.

Log messages are in English. The logs stay in memory and are lost when
SoundCharts quits; use `--log-file` to keep them.

## Report feedback

To report a problem, choose **Help** > **Report Feedback…** or the feedback
button in the title bar, next to the theme toggle. The **Report Feedback**
dialog:

- Explains what's collected.
- Has a box for **Your feedback**.
- Previews a screenshot of the application window (or, if that fails, of the
  map). Clear **Include this screenshot of the application** to leave it out.
- Shows the full JSON payload and the recent log messages under **Show data
  that will be sent**.

The diagnostics include the app version, build and runtime; the map view
(center, zoom, coordinate reference system and rotation); each dataset's
product, visibility and validation error and warning counts; the most recent
error caught by the global exception handlers (type, message and stack trace);
any [crashes](#crash-recovery) detected at start-up; and the messages in the
[Logs](#logs) tab. Paths show your home folder as `~`.

When you choose **Submit Feedback**, SoundCharts:

1. Writes a ZIP bundle to your temp folder containing `diagnostics.json`, your
   `feedback.txt`, the recent logs as `logs.txt` and, if included,
   `screenshot.png`. The logs aren't put in the GitHub issue; attach the
   bundle if they help.
2. If you included a screenshot, saves it next to the bundle as
   `…-screenshot.png`, shows that file in your file manager, and copies the
   screenshot to the clipboard.
3. Opens a prefilled GitHub new-issue page in your browser.

Paste the screenshot into the issue with Cmd+V or Ctrl+V. If GitHub reports
"failed to upload image.png", drag the `…-screenshot.png` file into the form's
Screenshot field instead; file uploads work when pasting fails.

Nothing leaves your computer until you create the GitHub issue, and
SoundCharts uploads nothing itself.

## About and updates

**Help** > **About SoundCharts** (on macOS, **About SoundCharts** in the
application menu) shows the version, the build's full version with commit and
build date, and whether a newer release is available.

- The version comes from the release tag the build was made from. Local and
  development builds report `0.0.0-dev` and show that update checks aren't
  available.
- The dialog checks the
  [latest GitHub release](https://github.com/philliphoff/EncDotNet.S100/releases)
  and shows either **You're up to date** or **Update available** with the
  version, release highlights, publish date and download size. The check
  doesn't block the dialog, and fails quietly when you're offline. **Check
  now** checks again.
- **Update now** opens the release page so you can download the build for your
  platform. SoundCharts doesn't update itself. **Release notes** opens the
  full notes on GitHub.
- **Skip** stops notifications for that release only. The About dialog still
  shows it, so you can install it later, and later releases are still
  announced.

After start-up, a notification announces a new release you haven't skipped.
**View release** opens GitHub, **Remind me later** dismisses it until the next
check, **Skip this version** skips that release, and **Stop checking** turns
off automatic checks. Automatic checks run at most once a day. Offline
failures, skipped releases and development builds stay silent at start-up.

To test the update check against the real GitHub API from a development build,
set `S100_UPDATE_FORCE=1` before you start SoundCharts. Don't set it in shipped
builds.

### Crash recovery

Some crashes end the process before SoundCharts can record them, such as a
native fault in the GPU or SkiaSharp stack, `Environment.FailFast`, a stack
overflow, an out-of-memory kill or `kill -9`. To catch these, SoundCharts
writes a marker file for each running process (`viewer-session-{pid}.lock`,
in a `crash-markers` folder next to your settings) and deletes it when it
quits normally. At the next start, a marker whose process isn't running means
that session ended abnormally:

- A **Viewer recovered from an unexpected shutdown** notification appears,
  with a **Send feedback** button.
- Each detected session's start time, process ID and version, the end of
  `viewer-crash.log`, and the end of the most recent crashed session's log
  (from the rolling logs in the `logs` folder next to your settings, at most
  5 files of 1 MB) are added to the feedback report automatically. Your home
  folder is shown as `~` in those log excerpts. They're
  kept separately from the most-recent-error slot, so a later, non-fatal error
  doesn't replace them before you send feedback. Every detected crash is
  reported, not only the latest.

Markers are per process, so this works when you run several copies of
SoundCharts side by side: one copy's normal exit never removes another's crash
evidence. SoundCharts checks whether a process is alive by its ID and start
time, on Windows, macOS and Linux, so a reused process ID isn't mistaken for a
running session. `--ephemeral` runs don't write markers or rolling logs.

## Connect AI agents over MCP

SoundCharts can host a Model Context Protocol (MCP) server that gives AI agents
access to the loaded datasets. It's off by default, listens on `127.0.0.1`
only, and has no authentication. Turn it on with **Settings** >
**Integrations** > **MCP Server** > **Enable MCP server**. The status bar
shows the port and the number of connected clients.

Besides the standard tools, such as `list_datasets`, `describe_feature`,
`sample_coverage` and `pick_features`, SoundCharts adds tools that control the
running app:

| Tools | What they do |
|---|---|
| `render_to_image`, `capture_app_screenshot` | Capture the map, or the whole window including panels and the status bar, as a PNG. |
| `await_render_idle` | Wait until the map has finished drawing, so a capture that follows is complete. |
| `get_render_stats` | Report the cost of the last frame: duration, interval and draw calls by style. |
| `set_viewport`, `set_palette`, `set_display_category`, `set_display_mode`, `set_time_step` | Change the map view, palette, display category, display mode (such as the S-411 ice display mode, S-100 Part 9 §11.7) and time step. |
| `set_own_ship` | Position and steer the simulated own ship (`lat`, `lon`, `cog`, `sog`, `heading`, hold and resume), whether or not it's shown. |
| `open_dataset`, `close_dataset`, `close_all_datasets` | Load a file or exchange set the same way the **File** menu does, and report its ID, bounding box and load time; unload datasets by ID. |
| `create_route`, `list_routes`, `get_route`, `delete_route`, `append_waypoint`, `insert_waypoint`, `move_waypoint`, `delete_waypoint`, `set_leg_attributes`, `set_route_info`, `get_passage`, `set_passage`, `undo_route_edit`, `redo_route_edit`, `set_route_edit_mode`, `select_route_item`, `set_passage_card` | Build and edit routes, shown live in the **Routes** panel. Edits are undoable, with the viewer's own edits. |
| `list_panels`, `set_panel` | List the dock panels and their state, and show or hide a panel by ID (such as `Datasets`, `LayerStack`, `PickReport` or `Timeline`). |

The [MCP server](../../docs/mcp-server.md) page has the full tool list, which
tools change state, and an agent walkthrough.

## Automation / agent control

Command-line options start SoundCharts with datasets in a known state, turn on
MCP, and keep a run away from your saved profile:

```bash
mkdir -p /tmp/run
dotnet run --project src/EncDotNet.S100.Viewer -- \
  --ephemeral --mcp --mcp-port-file /tmp/run/mcp.url \
  --bbox 47.5,-122.5,47.7,-122.1 --palette Night \
  --log-file /tmp/run/viewer.log -v \
  path/to/dataset.h5
```

The command line only starts and isolates the process and sets the initial
view. To control a running instance (capture images, change the palette,
category, time, view or own ship, and open or close datasets), use the
[MCP tools](#connect-ai-agents-over-mcp). There is no MCP tool to quit. To end
a run, stop the process from the operating system; SoundCharts ignores
`SIGTERM`, so use `kill -9 <pid>`.

### Options

| Option | Purpose |
|---|---|
| `[datasets]` | One or more dataset files to open. |
| `--mcp` | Start the MCP server for this run, whatever the saved setting. |
| `--mcp-port <PORT>` | MCP port. `0`, the default, picks a free port. Implies `--mcp`. |
| `--mcp-bind <ADDRESS>` | MCP bind address. Use a loopback address. Implies `--mcp`. |
| `--mcp-port-file <PATH>` | Write the MCP endpoint URI to this file once the server is listening. Implies `--mcp`. |
| `--mcp-test-hooks` | Add test-only MCP tools, such as `set_test_clock`, which moves or freezes the app's current time. Implies `--mcp`. |
| `--secom-identity <PATH>` | Present this MCP client certificate (PKCS#12, or PEM with its key) to SECOM services for this run, instead of the identity chosen in **Settings** > **Keys & certificates**. The password, if any, comes from `SOUNDCHARTS_SECOM_IDENTITY_PASSWORD`. |
| `--settings <PATH>` | Use this settings file instead of the per-user one. |
| `--data-dir <PATH>` | Put the settings file and all caches under this folder. You can also set `S100_DATA_DIR`. |
| `--ephemeral` | Use throwaway settings that are never saved. Can't be combined with `--settings`. |
| `--center <LAT,LON>` | Center the map on this position. Needs `--zoom`. |
| `--zoom <LEVEL>` | Web Mercator zoom level, 0 to 24, for `--center`. |
| `--bbox <S,W,N,E>` | Zoom to this WGS-84 bounding box. Can't be combined with `--center` and `--zoom`. |
| `--palette <PALETTE>` | `Day`, `Dusk` or `Night`. |
| `--display-category <CATEGORY>` | `DisplayBase`, `Standard`, `OtherInformation` or `All`. |
| `--basemap <MODE>` | `None`, `Offline` or `Online`. The older values `true` and `false` mean `Online` and `None`. |
| `--time-step <STEP>` | Go to this time step: a zero-based index or an ISO 8601 UTC timestamp. |
| `--own-ship-pos <LAT,LON>` | Place the simulated own ship here. |
| `--own-ship-cog <DEG>` | Own-ship course over ground, in degrees true, from 0 up to (but not including) 360. |
| `--own-ship-sog <MS>` | Own-ship speed over ground, in metres per second, 0 or more. |
| `--log-file <PATH>` | Append structured logs to this file. |
| `--crash-log <PATH>` | Write the crash log here instead of `viewer-crash.log` in the system temp folder. |
| `-v`, `--verbose` | Log at Debug level. |
| `--demo-notifications` | Show sample notifications at start-up, to check how they look. For development. |

### MCP

Because a port of `0` isn't known in advance, use `--mcp-port-file` to find
it. The endpoint is also written to standard output as `[MCP] listening on …`.
A run that sets MCP options on the command line never saves the port to
`settings.json`.

### Isolate a run

`--settings <PATH>` uses another settings file. `--ephemeral` uses a throwaway
settings file that's never written, so command-line overrides and MCP ports
can't change your profile, and parallel runs don't collide.

`--data-dir <PATH>` moves everything SoundCharts writes under one folder: the
settings file, crash markers, and the disk caches (pattern clipping,
portrayal instructions, warm tiles, dataset metadata and the S-57 catalogue
cache). Point it at an empty folder for a fresh instance that you can delete
with one `rm -rf`, or fill the folder first to start with prepared settings or
caches. With `--data-dir`:

- `--settings <PATH>` still moves only the settings file; caches stay under the
  data folder.
- `--ephemeral` loads the folder's settings without saving them.
- An explicit `S100_VECTOR_TILE_DISK_DIR` still sets the tile cache location.

**Settings** > **Advanced** > **Maintenance** clears the same locations while
SoundCharts is running.

### Set the initial view

`--center` with `--zoom`, or `--bbox`, frames the map after the datasets load.
Either one turns off the automatic zoom to the data's extent, so the framing is
the same on every run.

`--palette`, `--display-category` and `--time-step` set the initial display
for this run only; saved values are unchanged. To change them while
SoundCharts is running, use the `set_palette`, `set_display_category` and
`set_time_step` MCP tools.

`--basemap` sets the basemap for the run. Use `None` or `Offline` to work
offline, or to measure dataset rendering without basemap tile activity.

The `--own-ship-*` options apply after the datasets load, whether or not the
own ship is shown. To move or steer it later, use `set_own_ship`.

### Capture images

Image capture uses MCP, not command-line options. `render_to_image` captures
the map, at any size and pixel density, from a copy of the live map.
`capture_app_screenshot` captures the whole window. Call `await_render_idle`
first so the capture shows a finished frame. The old `--screenshot`,
`--exit-after-screenshot`, `--close-after-screenshot`, `--full-window` and
`--window-size` options have been removed; call `open_dataset`,
`set_viewport`, `await_render_idle` and `render_to_image` instead.

### Example: drive SoundCharts from an agent

1. Start SoundCharts with an isolated profile and a free MCP port, and record
   the endpoint:

   ```bash
   mkdir -p /tmp/run
   dotnet run --project src/EncDotNet.S100.Viewer -- \
     --ephemeral --mcp --mcp-port-file /tmp/run/mcp.url \
     --bbox 47.5,-122.5,47.7,-122.1 \
     path/to/dataset.h5
   ```

2. Wait for `/tmp/run/mcp.url` to appear, and read the endpoint URI from it,
   or from the `[MCP] listening on …` line in standard output.
3. Connect an MCP client to the endpoint. Call `list_datasets`,
   `describe_feature` or `sample_coverage` to inspect features and values.
4. To capture an image, call `await_render_idle`, then `render_to_image` for
   the map or `capture_app_screenshot` for the window.
5. When you're done, call `close_all_datasets`, then stop the process with
   `kill -9 <pid>`.

## For contributors

This section is for people working on SoundCharts itself. SoundCharts is one
consumer of the EncDotNet.S100 libraries: the libraries do the
specification-aware work, and the viewer is mainly composition and Avalonia
views.

### Map host and dataset session

The live `MapsuiMapHost` is viewer composition, not a service contract.
Consumers depend on focused interfaces for layer bands, viewport and
navigation, coordinate conversion, snapshot rendering or redraw invalidation.
Late-bound services use typed `ICapabilityAccessor<TCapability>` instances, so
no consumer depends on the whole map host.

- Layer ordering and ownership are in the reusable `MapsuiLayerBands`
  component, and viewport behavior is delegated to `MapsuiMapNavigator`. Both
  work on `Mapsui.Map` without Avalonia.
- The optional `EncDotNet.S100.Renderers.Mapsui.Avalonia` adapter owns
  live-control attachment, dispatcher use, invalidation, coordinate conversion
  and framework capture.
- The viewer keeps automatic zoom after load, capability readiness,
  diagnostics, MCP and feedback policy, render-context construction and host
  lifecycle.
- `MapsuiDatasetLayerSession` owns time registration, product-specific
  snapping and gating, refresh cancellation and render serialization. The
  viewer's timeline is a projection of its time snapshot.

The session reports its render lifecycle and refresh failures as events
(`DatasetRenderStarted`, `DatasetRenderCompleted`, `DatasetRenderFailed`,
`LayersChanged`, `TimeRangeChanged`, `CurrentTimeChanged`). The viewer treats
them as host policy: it projects layer and time state, and logs refresh
failures. `DatasetRenderFailed` is best-effort and written to `Console.Error`,
not shown as a notification. The notifications and localized strings for the
load lifecycle stay in the viewer's own load path.

Loaded rows in the **Datasets** panel project the renderer-neutral
`MapDataset` and `MapDatasetSubLayer` snapshots; commands, localized labels,
selection and exchange-set registration stay in the viewer. Palette, ECDIS,
scale and mariner inputs are combined into the current `MapPresentationState`
before rendering and passed through
`IMapPresentationController.SetPresentationAsync`. `MapsuiDatasetLayerSession`
owns processor-to-layer rendering, replacement and removal, S-98
cross-product order and suppression, active, visible, opacity and sub-layer
state, scale windows, overlap suppression, time-aware registration and gating,
render cancellation, and coalesced time and presentation refreshes.
`MapPresentationState` creates product render contexts for the session, and
`GlobalTimeService` only projects the session clock into timeline bindings.

### Viewer coordinator boundary

`DatasetLoaderService` is a thin viewer coordinator. The reusable dataset
render lifecycle (processor-to-layer rendering, replacement, ordering, visible
and active state, S-98 composition, time gating, render-context construction,
render serialization and cancellation, and processor ownership) lives in
`MapsuiDatasetLayerSession` and `DatasetProcessorOwner`, in
`EncDotNet.S100.Renderers.Mapsui` and `EncDotNet.S100.Datasets.Pipelines`. The
coordinator's render methods (`RenderAndReplaceAsync`, `ReplaceLayersAsync`,
`SetPresentationAsync`, `ReRenderAtTimeAsync`) are thin wrappers over that
session. The coordinator still owns viewer host policy:

- **Load orchestration**: product detection (`ResolveSpecOrWarn`), portrayal
  catalogue prompts (`HasRequiredCatalogueOrWarn`), processor construction
  (`CreateProcessorAsync`), and load-generation guarding for concurrent loads
  and reloads.
- **UI defaults**: policies applied after registration
  (`ApplyPostRegistrationPolicies`): collapsing duplicate coverage, hiding
  S-104 gridded surfaces by default, surfacing S-101 update reports, and
  registering S-128 catalogues.
- **Notifications**: progress, success, cancel and error notifications and
  their localized strings (`CreateLoadProgressNotification`, `DriveTerminal`),
  plus recent files and the optional zoom after load.
- **View-model projection**: mapping the session's renderer-neutral
  `MapDataset` and sub-layer snapshots onto viewer `DatasetEntry` view-models
  with localized names and `INotifyPropertyChanged` (`ProjectSessionState`).
  This belongs in the viewer, not in the UI-framework-free session.

### Lazy loading and caches

The on-demand loading of [large exchange sets](#large-exchange-sets) is in
`Services/LazyLoading/` (`ExchangeSetLazyLoadCoordinator`, `LazyCellGate`,
`LruEvictionPolicy`, `CellUsageBand`). Cells are selected by viewport
intersection and a usage-band-to-scale gate, and loaded through a gate with
bounded concurrency. The 50-cell threshold is
`ExchangeSetLazyLoadCoordinator.CellThreshold`. Cells are registered in one
batch with
`DatasetsViewModel.AddRangeFromExchangeSet(IReadOnlyList<ExchangeSetCellRegistration>)`,
backed by `BulkObservableCollection`, so thousands of cells raise one
collection notification instead of one each.

The S-57 catalogue descriptor cache is `Services/Caching/DiskS57CatalogCache`,
stored under `caches/S57CatalogCache` and keyed by the `CATALOG.031` file's
modified time and size. The Library's index and catalogue caches are
`CollectionIndexCache` and `CollectionFeedCache`.

### Related libraries and design notes

Start with the library that does the work you're changing:

- Pipeline framework and shared types:
  [EncDotNet.S100.Core](../EncDotNet.S100.Core/README.md)
- Per-product processors and the S-98 interoperability rules:
  [EncDotNet.S100.Datasets.Pipelines](../EncDotNet.S100.Datasets.Pipelines/README.md)
- Per-product readers and validation rules: the `EncDotNet.S100.Datasets.S*`
  projects
- Vector, coverage and dynamic-feature renderers:
  [EncDotNet.S100.Renderers.Mapsui](../EncDotNet.S100.Renderers.Mapsui/README.md)
- Optional Avalonia live-control adapter:
  [EncDotNet.S100.Renderers.Mapsui.Avalonia](../EncDotNet.S100.Renderers.Mapsui.Avalonia/README.md)
- MCP server:
  [EncDotNet.S100.Mcp.Tools](../EncDotNet.S100.Mcp.Tools/README.md) and
  [EncDotNet.S100.Mcp](../EncDotNet.S100.Mcp/README.md)

Design notes for features that cross several projects:

- [Dynamic feature sources](../../docs/design/dynamic-feature-source.md)
- [Own-ship symbology](../../docs/design/own-ship-symbology.md)
- [S-98 interoperability](../../docs/design/s98-interoperability.md)

The internationalization conventions, the `IActivityTab` and activity-bar
contract, the `IDynamicFeatureSource` abstraction, the
`ICoverageStyle` and `IVectorPortrayalCatalogue` contracts, and
the `.resx` string convention are in
[`viewer.instructions.md`](https://github.com/philliphoff/EncDotNet.S100/blob/main/.github/instructions/viewer.instructions.md).
The app icon and installer art are described in
[Branding assets](Branding/README.md).
