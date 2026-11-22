# Catalogue panels — Design Note

> Status: **Proposed** (2026-11-21). Merges the Feature Catalogues and
> Portrayal Catalogues activities, which date from the viewer's first
> months, into one **Catalogues** activity. It builds on
> [Symbol dictionary](symbol-dictionary.md) (Chart reference, #1633, Phase 1
> in progress) and the accepted [Legends](legends.md) note, and it keeps
> their decisions. §8 lists where it touches them. The mocks are the
> **Catalogues · design pass (round 3)** artboard on the SoundCharts design
> canvas (<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>, Library & data
> page).

SoundCharts has two left-dock activities, **Feature Catalogues** and
**Portrayal Catalogues**. Each one lists a row per product with its version
and lets you add or remove a file. Neither one lets you look inside a
catalogue, and changes don't take effect until restart. This note asks what
jobs these panels should do and where those jobs belong. It recommends one
Catalogues activity that shows both catalogues of each product together,
applies changes live, and leaves reading catalogue content to
Chart reference.

## 0. Problem

- **The panels are split by file, not by product.** A mariner doesn't think
  "feature catalogue" or "portrayal catalogue". They think "which S-101
  edition am I using?". A producer thinks "draw my data with my draft
  catalogue". Each job needs both catalogues for a product, and none of them
  needs two lists.
- **They show nothing useful.** A row is a title, a version and a path.
  There's no sign of which catalogue is in use, whether it loaded, or which
  datasets it draws.
- **They don't work reliably** (§2): a feature catalogue added from the
  panel is always filed as S-101, changes need a restart, removing a
  portrayal catalogue doesn't restore the built-in one, and the list drops
  or doubles rows.
- **They use two activity bar slots** (orders 80 and 90) for one job.
- **Chart reference is coming.** It builds a mariner-facing browse of
  feature types, symbols, attributes and display settings from the same two
  catalogues (symbol-dictionary §4). The catalogue panels shouldn't compete
  with it as a second browser.

## 1. Users and jobs

| # | Who | Job | How often | Served today by |
|---|---|---|---|---|
| J1 | Mariner | "What is this, what does it look like, why is it drawn like that?" | Every watch | Object information, the Legend tab; Chart reference soon. Not the catalogue panels. |
| J2 | Mariner, inspector | "Which catalogue editions draw my chart? Are they current and do they match my data?" (S-98 20.1; S-164 InitialCatalogues, UpdateCatalogues, PowerUp) | At power-up, after an update, when a warning shows | Partly: version in the panel rows; a version warning on Datasets rows (`DatasetsViewModel.cs:291-298`) with no link to the catalogues |
| J3 | Mariner, IT | "Install the catalogue update that came with my data." | Rarely (catalogue updates) | No: by hand only; nothing installs from an exchange set (S-164 matrix, UpdateCatalogues) |
| J4 | Product spec developer, producer | "Draw my data with my draft FC/PC, see it change as I edit, see why it fails." | In bursts while developing | Partly: CLI `--feature-catalogue` / `--portrayal-catalogue` (`ViewerCommand.cs:16,20`) and the panels' Add, which needs a restart and hides errors |
| J5 | Developer | "Browse the feature catalogue model: codes, bindings, multiplicities, listed values." | Occasionally | The eXaminer website, through the FC row's Open button; Chart reference entries cover codes, acronyms and listed values |
| J6 | Developer | "Why did this rule pick this symbol?" | Occasionally | Not yet: Chart reference R11–R12 (Why this symbol, rule trace) |

J1, J5 and J6 are about **reading** catalogue content. J2, J3 and J4 are
about **managing** which catalogue is in effect. That split is what this note
organises around. Whether a file is a feature or portrayal catalogue isn't a
useful division for either group.

## 2. Audit of today's panels

What a user can do: see one row per product in each panel, add a file
(FC: an XML file; PC: a folder), remove a row they added, and in the FC
panel open the product's page on the eXaminer website. That's all.

| # | Finding | Where |
|---|---|---|
| A1 | **Add Feature Catalogue files every catalogue as S-101.** The comment says "for now assume S-101". `CatalogueSpecDetection.DetectFeatureCatalogueSpec` exists and the CLI path uses it, but the panel doesn't. | `Views/FeatureCataloguesView.axaml.cs:25-27`; `Services/CatalogueSpecDetection.cs:42-55` |
| A2 | **Add Portrayal Catalogue fails silently.** A folder without `portrayal_catalogue.xml`, or one whose product id can't be read, just returns, with no message. It also re-implements `DetectPortrayalCatalogueSpec` inline and swallows exceptions. | `Views/PortrayalCataloguesView.axaml.cs:26-39`; `CatalogueSpecDetection.cs:21-36` |
| A3 | **Feature catalogue changes wait for a restart.** `AddOrUpdate` writes settings. `FeatureCatalogueOverrides.Open` reads them, but `FeatureCatalogueManager` caches the parsed catalogue per spec and nothing evicts it. | `CatalogueViewModels.cs:56-61`; `Services/FeatureCatalogueOverrides.cs:70-86`; `Features/FeatureCatalogueManager.cs:96-114` |
| A4 | **Removing a portrayal catalogue leaves it in use.** `Remove` deletes the setting but doesn't put the built-in source back on `PortrayalCatalogueManager`. The custom catalogue keeps drawing until restart. | `CatalogueViewModels.cs:232-238`; `Services/PortrayalCatalogueSeeder.cs:77-85` |
| A5 | **Loaded datasets are never redrawn.** Even where the manager is updated (`SetPath` in PC `AddOrUpdate`), the open datasets keep their old processors. | `CatalogueViewModels.cs:177-183` |
| A6 | **Built-in rows vanish after any add or remove.** `Reload` clears the list and adds back only persisted paths. Built-ins are added once, by `MainWindow`, at startup. | `CatalogueViewModels.cs:47-54, 168-175`; `MainWindow.axaml.cs:321-331` |
| A7 | **Command-line catalogues show as a second row.** Built-ins are added in the window constructor, then CLI catalogues are added with `AddTransient`, which doesn't check for an existing row. S-101 can appear twice, and nothing says which one is in use. | `MainWindow.axaml.cs:321-331, 648-669`; `CatalogueViewModels.cs:66-69, 188-192` |
| A8 | **No sign of what's in effect.** Rows don't say "in use", "overridden" or "failed to load". Precedence (CLI over settings over bundled) exists only in code. | `FeatureCatalogueOverrides.cs:67-69`; `PortrayalCatalogueSeeder.cs:41-85` |
| A9 | **The eXaminer button can show the wrong catalogue.** It shows whenever the eXaminer hosts the spec, so a custom draft catalogue's row opens the published edition's page. | `CatalogueViewModels.cs:86-87, 106-113` |
| A10 | **The two rows don't match.** FC rows show version and date. PC rows show version only, and their title comes from the curated name map. | `CatalogueViewModels.cs:106-113, 207-213` |
| A11 | **Dead code and odd wiring.** `AddCommand` is a no-op relay in both view models, and Add is a code-behind click handler. `MainViewModel` holds both view models only so that `MainWindow` can push rows into them. | `CatalogueViewModels.cs:35, 163`; `MainViewModel.cs:11-12, 64-65, 94-95` |
| A12 | **Warnings give no way to fix them.** The missing-catalogue toast says "Please select a portrayal catalogue for {0} first." without a button. The Datasets version warning doesn't link to the catalogues either. | `Services/DatasetLoaderService.cs:952-968`; `DatasetsViewModel.cs:291-298` |
| A13 | **Two activity slots** for one job: `FeatureCatalogues` (order 80, `BookOpen`) and `PortrayalCatalogues` (order 90, `PaintBrush`). | `App.axaml.cs:2230-2243` |

The model underneath is sound: one override per product, precedence CLI >
settings > bundled, `PortrayalCatalogueManager.SetPath`/`SetSource` already
evict. The problems are in the UI layer and in the two halves being wired
separately.

## 3. Do we need separate FC and PC panels?

**No. One Catalogues activity replaces both.**

- **The product is the unit, not the file.** Every dataset needs both
  catalogues of its product. A mariner checking S-101 wants to see both
  editions together, and a producer swapping a draft usually swaps both. Two
  lists make the user line them up by eye.
- **Managing deserves a visible home.** Catalogues will arrive with
  exchange sets (J3), a draft that fails to load must be easy to find (J4),
  and an inspector asks which editions are in use (J2). A left-dock activity
  next to the chart keeps that one click away, and a change is redrawn right
  beside the panel.
- **Reading belongs to Chart reference.** J1 and most of J5 are what
  Chart reference is designed to do, and it already joins the two catalogues
  (a feature type's definition comes from the FC, its symbols from the PC).
  The Catalogues activity doesn't browse catalogue content; it links there.
- **The developer jobs don't need their own panel.** J4 needs live apply
  and visible errors, which the Catalogues activity provides. J6 is
  Chart reference R11–R12 in Object information. Errors go to Logs.

Placements considered and dropped:

- **Settings › Catalogues** (round 1 of the design pass). It hid catalogues
  behind the gear, and exchange-set delivery had no natural home there.
- **A Catalogues node in the Library** (round 2). The Library would get
  busy, and users don't think of catalogues as a collection of things the way
  they think of datasets.
- **Keep the FC panel as a technical model browser** (bindings,
  multiplicities). The eXaminer does this well. Q5 asks whether
  Chart reference should show codes instead.

## 4. Information architecture

| Job | Home | Entry points |
|---|---|---|
| J1 Read a feature, symbol, attribute or setting | **Chart reference** activity and Reference tab (symbol-dictionary §4) | As planned there: Object information, the Legend tab, the map's context menu, Help |
| J2 What's in effect, does it match | **Catalogues** activity. A one-line summary in the **Chart reference** product header and in the dataset inspector's **Dataset** tab | Datasets row version warning, dataset inspector, Chart reference header, Help › About (§6.5), the activity badge (D13) |
| J3 Install an update | **Catalogues › Add catalogue…** now. From exchange sets later, with the installed-data store (#1129) | The Library's exchange-set details, when an exchange set carries catalogues (later, §6.6) |
| J4 Use a draft catalogue | **Catalogues** (Add, Replace, Reload, Revert) and the CLI flags as today | The missing-catalogue toast's button |
| J5 Browse the FC model | **Chart reference** entries (codes, acronyms, listed values); the **eXaminer** link in the Chart reference product header and the Catalogues row menu | Chart reference, Catalogues |
| J6 Why this symbol | Object information (Chart reference R11–R12) | A pick |

## 5. Decisions

**D1 — One Catalogues activity replaces both.** A new left-dock activity,
id `Catalogues`, order 80, icon `BookOpen` (the Feature catalogues icon),
automation id `Activity.Catalogues`. `FeatureCatalogues` and
`PortrayalCatalogues` are deleted with their views, view models, strings and
automation ids, and `PaintBrush` is freed. A persisted
`LastSelectedActivity` of either old id opens Catalogues, so nobody lands on
an unexpected tab after upgrading.

**D2 — The unit is the product.** Every surface that shows catalogues shows
one row per product spec, with both catalogues on it. A product with only
one catalogue (S-104, drawn by an application portrayal, issue #937) says so
("Drawn by SoundCharts") instead of leaving a gap.

**D3 — One list, filtered by pills.** The panel is a header (CATALOGUES,
**Add catalogue…**, a ⋯ menu), a filter box with a count ("3 of 17"), a row
of pills, the product list and a details pane, in the Library panel's
layout. It lists every product the viewer knows: bundled, custom and from the
command line. The pills:

- **All** (default): every product, products with a loaded dataset first,
  then by spec code, as Chart reference's product list does
  (symbol-dictionary D4).
- **Loaded**: products with at least one dataset open.
- **Changed**: anything not built-in: custom, command line, or a product
  SoundCharts doesn't include.
- **Problems**: Error or Missing. Hidden when there are none.
- **Updates** (later, §6.6): a newer catalogue is available from an
  exchange set.

Each pill shows its count. The selected pill is remembered.

**D4 — Show the effective catalogue, and say what it overrides.** A row is
the product's pair: the spec, the name, tags only when something isn't the
default (*Custom*, *Command line*, *Not built in*, *1 problem*, *Missing*),
then "Feature 2.0.0 · Portrayal 2.1.0-draft" and how many datasets are
loaded. A red bar marks Error, an amber bar Missing, as dataset rows mark
their state. The details pane shows, for each catalogue, the one source in
use (*Built-in*, *Custom* with its path, or *Command line, this session*)
and what it overrides as a muted line ("Overrides built-in 2.0.0"). A product
never has two rows (fixes A6, A7, A8). States: **In use**, **Error**
(falling back to built-in, with the reason inline) and **Missing** (no
catalogue; datasets of this product won't open).

**D5 — One Add catalogue… for both kinds.** It accepts a feature catalogue
XML file, a portrayal catalogue folder, or a portrayal catalogue ZIP package
(how S-164 delivers them). It detects the kind and the product from the
content, never from the file name, and never assumes a product (fixes A1,
A2). It always ends in a confirmation sheet that names what is replaced and
how many loaded datasets are redrawn (§6.2). A file it can't read is refused
there with the reason, and the sheet offers **Choose another…** and Cancel.
Replace… in a row's menu does the same with the product fixed.

**D6 — Changes apply live.** Add, Replace, Revert to built-in and Reload
take effect without a restart. They evict the parsed catalogue and its
caches, raise `CataloguesChanged` (the event Chart reference R2 adds,
symbol-dictionary §3.4), and redraw the loaded datasets of that product. A
toast confirms ("S-101 is now drawn with portrayal catalogue 2.1.0-draft
(custom). 3 datasets redrawn.") with **Undo**, which puts back the previous
source. Revert to built-in actually restores the built-in (fixes A3, A4,
A5).

**D7 — One service owns precedence.** A viewer `CatalogueRegistry`
replaces `FeatureCatalogueOverrides`, `PortrayalCatalogueSeeder` and the
writes in the two view models. It holds CLI, persisted and bundled sources
for both kinds, applies them to `FeatureCatalogueManager` and
`PortrayalCatalogueManager`, exposes the effective state per product, and
raises the change. The Catalogues activity, Chart reference, the dataset
inspector and MCP read it. `MainViewModel` and `MainWindow` stop seeding
catalogue rows (fixes A11).

**D8 — Reading stays in Chart reference.** The Catalogues activity doesn't
browse catalogue content. Chart reference's product header carries a
one-line identity ("Feature 2.0.0 · Portrayal 2.0.0 · Built-in"), with
**Catalogues…** (opens the activity at that product) and, where it applies,
**eXaminer** (D9). A row's details pane has **Open in Chart reference**.
Chart reference already indexes "the catalogues the viewer has loaded"
(symbol-dictionary §3), so a custom catalogue reads as it draws.

**D9 — The eXaminer link is per product and honest.** It appears in the
Chart reference product header and the row menu. It shows only when the
eXaminer hosts the spec (as today). When the feature catalogue in use is
custom, it reads "Open published edition in eXaminer" (fixes A9). Object
information's feature and attribute links are unchanged; Chart reference R9
moves them.

**D10 — Warnings lead to the fix.** The Datasets row version warning gets
**Catalogues…**. The missing-catalogue toast gets **Choose catalogue…**,
which opens Add catalogue… with the product fixed. The dataset inspector's
**Dataset** tab gets a "Drawn with" line naming both catalogues and their
source, with **Catalogues…** (fixes A12). Every **Catalogues…** link opens
the activity with that product selected.

**D11 — Developers get Reload and visible errors, not a separate panel.** A
custom catalogue's details have **Reload**, **Revert to built-in** and
**Show in Finder/Explorer**. Load failures (parse errors, a missing
`portrayal_catalogue.xml`, a Lua error building the provider) go to Logs
under the area `Catalogues`, and the Error state links there. The CLI flags
stay as they are, shown as *Command line, this session*, not persisted and
not revertable from the panel.

**D12 — Custom products are first-class.** Adding a catalogue for a spec
that isn't bundled (an `S-1xx` under development) adds a product row with
the catalogue's declared name and the *Not built in* tag. A product with only
a feature catalogue shows its portrayal as Missing, with **Add portrayal
catalogue…** as the main action. **Remove product** removes the row.

**D13 — The activity badge counts problems.** The view model implements
`IActivityTabBadge`: the count is the number of products under Problems,
the name is "2 catalogue problems". A failed draft isn't silent while the
panel is closed. Zero hides the badge.

## 6. Layouts

The design pass artboard draws all of these at app scale. ASCII sketches
for reference:

### 6.1 Catalogues activity

```
┌ CATALOGUES ─────────────── [+ Add catalogue…] [···] ┐
│ [ Filter by product or name…             3 of 17 ] │
│ (All 17) (Loaded 3) (Changed 3) (Problems 2)        │
│                                                     │
│▌S-101 Electronic Navigational Chart [Custom][1 problem]
│   Feature 2.0.0 · Portrayal 2.1.0-draft    3 loaded │
│  S-102 Bathymetric Surface                          │
│   Feature 3.0.0 · Portrayal 3.0.0          1 loaded │
│  S-104 Water Level Information                      │
│   Feature 2.0.0 · Drawn by SoundCharts     2 loaded │
├─────────────────────────────────────────────────────┤
│ S-101 Electronic Navigational Chart        [Ref][···]│
│ 3 datasets loaded · drawn with a custom portrayal   │
│ FEATURE CATALOGUE                                   │
│   Version    2.0.0 · 2024-10-16                     │
│   Source     Built-in                               │
│ PORTRAYAL CATALOGUE                                 │
│   Version    2.1.0-draft · 2026-11-18               │
│   Source     Custom                                 │
│   Path       ~/specs/s101-pc-draft                  │
│   Overrides  Built-in 2.0.0                         │
│ ⚠ LateralBeacon.lua line 42: … Lateral beacons are  │
│   drawn with built-in 2.0.0.  Show in Logs          │
│ [Reload] [Revert to built-in] [Show in Finder]      │
└─────────────────────────────────────────────────────┘
```

- The row menu [···] has: Replace feature catalogue…, Replace portrayal
  catalogue…, Revert to built-in (only when overridden), Show in
  Finder/Explorer (only when custom), Open in Chart reference, and Open in
  eXaminer (D9).
- Automation ids: `Catalogues.Add`, `Catalogues.Filter`,
  `Catalogues.Pill.{All|Loaded|Changed|Problems}`, `Catalogues.Row`,
  `Catalogues.Row.Menu`, `Catalogues.Details.Reload`,
  `Catalogues.Details.Revert`.

### 6.2 Add catalogue… confirmation sheet

```
┌ Add catalogue ───────────────────────────────────────────────┐
│ ~/specs/s101-pc-draft                                        │
│                                                              │
│ [S-101] Portrayal catalogue 2.1.0-draft                      │
│         Electronic Navigational Chart                        │
│                                                              │
│ Replaces   Portrayal catalogue 2.0.0 (built-in)              │
│ Redraws    3 loaded S-101 datasets                           │
│                                                              │
│ ☑ Keep using it after restart                                │
│                                     [ Cancel ]  [ Use it ]   │
└──────────────────────────────────────────────────────────────┘
```

With **Keep using it after restart** cleared, the override lasts for the
session, like the CLI flags. For a product SoundCharts doesn't include, the
sheet says "Adds a new product" and the button reads **Add product**. When
the file can't be read, the sheet says why ("There's no
portrayal_catalogue.xml in this folder", "Not a feature catalogue: the root
element is …") and offers **Choose another…** and Cancel.

### 6.3 Chart reference product header (with symbol-dictionary R4)

```
┌ Chart reference ─────────────────────────────┐
│ [S-101 Electronic Navigational Chart    ▾]   │
│ Feature 2.0.0 · Portrayal 2.1.0-draft Custom │
│ Catalogues…   Published edition in eXaminer ↗│
│ [ Search                               ]     │
│ …                                            │
```

### 6.4 Datasets row and dataset inspector

```
 ⚠ 101GB00GB4X0010   S-101
   Made for feature catalogue 2.1.0; read with 2.0.0.
   Some attributes may be missing.  Catalogues…

 Dataset tab
   Drawn with   Feature catalogue 2.0.0 · Built-in
                Portrayal catalogue 2.1.0-draft · Custom
                Catalogues…
```

### 6.5 Help › About

Help › About gets a compact table of every product's catalogue editions
and sources (the "table of everything installed" the S-164 PowerUp row
lacks). It is read-only, links to the Catalogues activity, and is copied with
the rest of About into feedback reports.

### 6.6 Later: a catalogue that arrives with an exchange set

The Library's exchange-set details list the catalogues the set carries,
each compared with the one in use ("Newer than built-in 2.0.0 in use") and
with **Install**. Before install, the product's row in Catalogues shows an
*Update available* tag and appears under an **Updates** pill. After install,
the source reads "From UKHO weekly 47". This needs the installed-data store
(#1129).

## 7. What gets deleted or replaced

- `Views/FeatureCataloguesView.axaml(.cs)` and
  `Views/PortrayalCataloguesView.axaml(.cs)`, replaced by
  `Views/CataloguesView.axaml(.cs)`.
- `FeatureCataloguesViewModel` and `PortrayalCataloguesViewModel`, replaced
  by `CataloguesViewModel` (list, pills, details, badge). `CatalogueEntry`
  becomes `CatalogueProductRowViewModel`, and `ComposeTitle` moves with it.
- Both `AddActivityTab` registrations and their DI lines
  (`App.axaml.cs:1632-1635, 2230-2243`) become one; the examiner refresh hook
  for the FC panel (`App.axaml.cs:304-310`) moves to the new view model (the
  pick panel's half stays).
- `MainViewModel.FeatureCatalogues` / `PortrayalCatalogues` and the
  `MainWindow` seeding loops (`MainWindow.axaml.cs:321-331, 648-669`).
- `FeatureCatalogueOverrides` and `PortrayalCatalogueSeeder`, folded into
  `CatalogueRegistry` (D7).
- Strings: `Pane_FeatureCatalogues`, `Pane_PortrayalCatalogues`,
  `Tooltip_FeatureCatalogues`, `Tooltip_PortrayalCatalogues`,
  `Label_Activity_*Catalogues`, `Button_Add*Catalogue`,
  `Tooltip_Add*Catalogue` give way to one `Pane_Catalogues` set;
  `Status_SelectPortrayalCatalogue` is reworded with its action (D10).
- Settings keys: `CataloguePaths` becomes `PortrayalCataloguePaths`. Since
  there are no external users, the legacy `PortrayalCataloguePath`
  migration (`ViewerSettings.cs:73, 828-830`) is dropped rather than
  chained.
- Tests: constructor arguments in `ActivityTabRegistryTests`,
  `MultiDockActivityTabTests`, `LayoutPersistenceTests` and the pick tests;
  the FC half of `ExaminerViewModelTests` moves to the registry and
  Catalogues view model tests; `CatalogueEntryTests` moves with the row.

## 8. Fit with Chart reference and Legends

This note doesn't change any decision in either note:

- **symbol-dictionary D1** says "Feature catalogues keeps `BookOpen`". The
  Catalogues activity keeps it, so the clause still holds with the new name.
  The activity and tab icons D1 sets (`BookCompass`, `BookInformation`) are
  unchanged.
- **symbol-dictionary D4** lists "loaded products first, then every
  bundled catalogue". D12 adds custom products, which the index already
  supports because it reads the loaded catalogues. Q3 asks whether the
  product list shows them.
- **symbol-dictionary R2** adds `CataloguesChanged` to both managers. D6
  and D7 use the same event. Whichever slice lands first adds it.
- **legends D8** ("the Legend tab doesn't hold catalogues") is reinforced:
  catalogues are managed only in the Catalogues activity.

## 9. Open questions

1. **More than one edition per product?** The library can use a catalogue
   per layer and edition (S-164 MultipleRevisions); the viewer uses one per
   product. Proposal: one per product for now. The registry keys by
   `SpecRef` so editions can come later.
2. **Install from exchange sets: prompt or silent?** When a loaded exchange
   set carries a newer catalogue, should the viewer ask (a notification with
   Install) or install on load, as S-98 allows? Proposal: ask, from the
   Library and the "Add to Library" notification. Decide with #1129.
3. **Custom products in Chart reference's product list?** D12 makes them
   first-class here. Should symbol-dictionary D4 read "loaded, then bundled,
   then custom"?
4. **Should Chart reference show codes for developers?** It already shows
   codes and S-57 acronyms in entries and Copy identity. A "Show codes"
   toggle (bindings, multiplicities, value types) would cover J5 offline.
   Proposal: not now; the eXaminer link covers it.
5. **Watch custom catalogues for changes?** D11 gives Reload. A file
   watcher that reloads on save would make the J4 loop tighter. Is it worth
   the extra machinery?
6. **Ignore a CLI catalogue for this session?** Proposal: no. Restart
   without the flag.

## 10. Build slices

Each slice ships on its own with tests and a whats-new bullet.

1. **CR1 Registry and live apply.** `CatalogueRegistry` (D7) in
   `Viewer/Services`: sources per product and kind, precedence, effective
   state and errors, apply to both managers, eviction for the feature
   catalogue manager (a `SetSource` path or an `Evict`), `CataloguesChanged`
   (shared with symbol-dictionary R2, #1635), and a redraw of the loaded
   datasets of a changed product. Kind and product detection from content,
   ZIP packages included, built on `CatalogueSpecDetection`. The two old
   panels read and write through it as a stopgap, which fixes A1–A8 before
   any new UI. Tests: precedence, live replace, revert, error fallback,
   detection, redraw. MCP gets `list_catalogues` (effective state per
   product).
2. **CR2 The Catalogues activity.** The panel (§6.1) with pills, rows,
   details, row menu and badge (D13); Add catalogue… with its confirmation
   sheet (§6.2); the live-apply toast with Undo; the Logs area. In the same
   slice, delete both old activities and everything in §7. Tests: view model
   with a stub registry (pill counts, filtering, ordering, badge), a
   headless view test, the activity registry with `Catalogues` and without
   the two old ids, a persisted old id opening Catalogues.
3. **CR3 Entry points.** The Datasets version warning link, the dataset
   inspector's Drawn with line, the toast action (D10), and Help › About's
   table. Opening the activity at a product selects its row.
4. **CR4 Chart reference header.** The identity line, Catalogues… and the
   eXaminer link (D8, D9), after symbol-dictionary R4 (#1637).
5. **Later.** Install from exchange sets and the Updates pill with #1129
   (§6.6, Q2), a file watcher for custom catalogues (Q5), and more than one
   edition per product (Q1).
