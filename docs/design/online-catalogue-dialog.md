# Add online catalogue: design pass

> Status: **Built** (2026-11-08). Accepted after the canvas mock on 2026-11-07
> and built in the slices of §7: A1 #1348, B1 #1346, B2 #1349, B3 #1350 and
> C1 #1353. Follows the wizard as it grew from
> [#670](https://github.com/philliphoff/EncDotNet.S100/issues/670) (curated
> catalogues, add by URL) through #685 (NOAA S-1xx on AWS), #822/#883 (SECOM
> registry and the Connection step) and #1075/#1226 (weather sources).
> Code: `AddOnlineCatalogueWizardView.axaml`, `AddOnlineCatalogueWizardViewModel.cs`,
> `CatalogueDirectoryDialogView.axaml`, `CatalogueDirectoryDialogViewModel.cs`,
> `CatalogueScopeStepView.axaml`, `CatalogueTargetStepView.axaml`,
> `Secom/SecomConnectionStepView.axaml`, `Services/LibraryImportCoordinator.cs`,
> and the curated list in `EncDotNet.S100.Collections/KnownSources/known-sources.json`.

The wizard works and every path through it is sound: nothing chosen is lost
going back, SECOM services get their identity step, weather and forecast feeds
get their run options. What has changed is the volume. Step 1 was designed for
a handful of chart catalogues and now holds 32 curated entries of five different
kinds, up to ~40 more SECOM services once the registry is shown, and two
"escape hatch" rows. It still lives in a 640 × 600 dialog. This note audits it
and proposes numbered decisions, in two tiers (quick wins, restructure), with a
suggested PR split.

## 1. What the wizard does today

**Size.** The view is `Width="640" Height="600"`, and `LibraryImportCoordinator.ShowWizard`
also passes `.WithMaxWidth(640)` to the ShadUI dialog host. `DialogWindowFit`
caps the height at window height − 48; width is never adapted. The main
window's minimum is 800 × 560, so the smallest dialog is 640 × 512.
The body below the stepper is ~452 px tall.

**Paths.** The step header always shows four steps; which ones a path uses:

| Path (format) | Curated entries | 1 Catalogue | 2 Connection | 3 What to include | 4 Add to |
|---|---|---|---|---|---|
| NOAA ENC (`noaaEnc`) | 1 | ✓ | skipped, shows "Not needed" | region picker (200 px regions + areas) | ✓ |
| USACE IENC (`usaceIenc`) | 2 | ✓ | skipped | facet list | ✓ |
| chartcatalogs inland ENC (`chartCatalogs`) | 14 | ✓ | skipped | searchable list | ✓ |
| NOAA S-102 on AWS (`s100ExchangeCatalogue`) | 1 | ✓ | skipped | region picker + resolution | ✓ |
| NOAA S-104/S-111 on AWS (`s100ForecastModels`) | 2 | ✓ | skipped | models + forecast shape | ✓ |
| Weather (`weather`: GFS, ERDDAP, NHC, WPC, NWS) | 11 | ✓ | skipped | run note + keep downloaded; often "Nothing to choose" | ✓ |
| SECOM, curated or registry (`secom`) | 1 (+ registry) | ✓ | identity + probe; "Continue" / "Add and wait" | everything / ticked, sync, map view | ✓ |
| Custom URL (NOAA/USACE/chartcatalogs/S-100 feed) | user | add inside step 1, then as its format | skipped | as its format | ✓ |
| Entry points that skip step 1 | — | `StartAtIncludeAsync` (shared feed, "add currents for area") lands on step 3 with step 1 done | | | |

So eight of nine paths are three steps, and one in four entries is a weather
service whose step 3 may have nothing to choose.

**Step 1 composition** (from the screenshot, logical px, 640 × 600 dialog):
search box 36, list viewport ~295, registry row 36, URL row 36, gaps. The
selected card is ~127 px of the 295 px viewport, so 3½ rows are visible.

## 2. Audit

First impression: the eye lands on the selected NOAA ENC card, which is right,
but then on two dashed boxes at the bottom that look like drop zones and
compete with the primary list. The stepper reads as a row of truncated
sentences. Overall it feels like a form, not a catalogue browser.

| # | Observation | Where |
|---|---|---|
| A1 | The step subtitles truncate at 640 px: "NOAA ENC — all…", "Choose a catalo…". The `UniformGrid` gives each step ~138 px for a 22 px marker, 8 px gap and the text. | stepper `ItemsControl` |
| A2 | Step 2 reads "Connection · SECOM services" while NOAA ENC is selected. It's meant as "only for SECOM services", but reads as a statement about the current choice. After step 1 it becomes "Not needed", a step the user can see but never visits. | `Wizard_ConnectionForSecom`, `Refresh()` |
| A3 | The step count is fixed (`StepCount = 4`), so non-SECOM paths say "Step 3 of 4" and then finish at step 4 having skipped one. The footer and the stepper disagree with what the user actually did. | `StepOfText`, `Steps` |
| A4 | Step 1's subtitle repeats the selected entry's name while the selected card is right below it. Upcoming steps carry placeholder subtitles ("Choose a catalogue first", "Collection") that add noise, not information. | `Refresh()` |
| A5 | One flat list of 32 rows grouped only by **region**. Region is the wrong first cut: "North America" mixes NOAA ENC, USACE, S-102/104/111, NWS alerts, NHC, WPC, HF radar and a Canadian SECOM service, sorted alphabetically, so chart and weather sources interleave. Users arrive knowing *what kind* of data they want, rarely just the continent. | `Rebuild()` groups by `Region[0]` |
| A6 | "Worldwide" is a region group holding 7 weather/ocean entries; "Europe" holds 13 inland ENC lists. The groups are very uneven: the list is mostly inland ENC by count, while the most used entries (NOAA ENC, weather) are scattered. | `known-sources.json` |
| A7 | The SECOM registry, once shown, appends ~40 services as one more group at the bottom, after the curated regions, so they are only reachable by scrolling past everything or searching. | `ShowMatches()` |
| A8 | Every row's subline repeats the region already in its group header: "NOAA Office of Coast Survey · North America › United States" under "NORTH AMERICA". | `ProviderAndRegion` |
| A9 | The selected card expands in place to ~2.5 rows (chips, note, monospace URL, Terms link, plus reachability text and a Fix link for registry services). It pushes the next rows out of a viewport that only had room for four. Selecting a different row makes the list jump. | selected `StackPanel` |
| A10 | The two escape hatches are styled identically (dashed, full width, 36 px) though they do different things: one *reveals more entries in the list above*, the other *opens a form*. Both sit permanently below the list and take ~84 px (~20 %) of the body for things most users never use. Opening the URL panel grows the bottom stack to ~150 px and squeezes the list to two rows. | `StackPanel Grid.Row="2"` |
| A11 | The registry row's label leans on jargon ("MCP service registry"). The add-by-URL hint "NOAA, USACE, chartcatalogs or S-100 feed" is right-aligned grey text that reads like an unrelated label. | `Library_RegistryShow`, `Library_DirectoryAddByUrlHint` |
| A12 | The count "32" sits inside the search box at the right edge with no noun, so it reads like a typed value or a badge. | search `Panel` |
| A13 | Footer: Back is a disabled outline button on step 1; "Step 1 of 4" repeats the stepper; nothing tells the user what Next will do. The footer hint slot is only used on step 3. | footer `Grid` |
| A14 | The title says "catalogue" and the menu says "Browse online catalogues…", but a GFS GRIB filter, an NWS alert feed and a SECOM service aren't catalogues to most users. | `Wizard_Title`, `Menu_AddLibraryOnlineCatalogue` |
| A15 | Weather and single-download entries reach a step 3 that says "Nothing to choose: this catalogue lists one download" — a whole step to press Next. | `IsSingleEntry` panel |
| A16 | Step 3's NOAA region picker is a 200 px region column beside the areas list inside the same 600 px body; area names and right-aligned details (sizes, editions) compete for ~380 px. | `CatalogueScopeStepView` region grid |
| A17 | Accessibility: "limited" chips are 11 px text at 55 % opacity over the muted card tint, which is very likely below 4.5:1. Group headers are 11.5 px muted caps. Row hit targets are fine (~52 px). | chip styles |

What works and should be kept:

- The radio-card selection with tinted fill reads clearly, and the right-aligned
  format label ("S-102 · AWS", "NOAA ENC") is a good scan aid.
- Quality chips (Coverage outlines / Editions / Sizes, dimmed when limited) are
  honest and compact; they just need a better home.
- Search across name, provider, region and format, with the choice kept while
  filtered out.
- The fixed-height dialog with the footer pinned, so buttons never move between
  steps, and `DialogWindowFit` for short windows.
- Completed steps are clickable; going back keeps every tick.
- SECOM reachability is probed lazily on selection and explained in plain words
  with a direct Fix link.

## 3. Decisions

### Size

**D1. A wider, slightly taller dialog that adapts to the window.** Preferred
size **920 × 640**; minimum **640** wide (today's size); never taller than
window − 48 (unchanged `DialogWindowFit`) nor wider than window − 96. Extend
`DialogWindowFit` to also set `MaxWidth` from `ClientSize.Width`, and replace
both the view's `Width="640"` and the coordinator's `.WithMaxWidth(640)` with
`Width="920"` / `.WithMaxWidth(920)` (the host's max width would otherwise
silently keep it at 640). At the 800 px minimum window the dialog is 704 wide.

Width alone (the user's suggestion) fixes A1 and A16 and gives long names room,
but it does **not** add rows: the list is height-bound, and the selected card
and escape hatches still eat the viewport. Width pays off when it is spent on
columns (D5, D8), which is why D1 is sized for three columns rather than "a bit
wider". If D5/D8 don't happen, 800 × 640 is the right quick-win size. The extra
40 px of height goes to the body (452 → ~492), i.e. one more list row.

*Effort:* S. *Files:* `AddOnlineCatalogueWizardView.axaml`,
`Services/LibraryImportCoordinator.cs`, `Views/DialogWindowFit.cs`.

**D2. Responsive breakpoints on the body, not the dialog.** Measured on the
body's actual width: ≥ 840 px three columns (categories, list, details);
720–840 two columns (categories, list with details inline as today but compact,
D9); < 720 categories collapse to the segmented strip already used for facet
tabs in step 3 (`ListBox.facetTabs`) above the list. One `SizeChanged` handler
toggling classes; no separate layouts per step.

*Effort:* S–M. *Files:* `CatalogueDirectoryDialogView.axaml(.cs)`.

### Stepper and footer

**D3. Steps are per path.** The step list is rebuilt when step 1's selection
changes: three steps (Source, What to include, Add to) for everything except
SECOM, four (Source, Connection, What to include, Add to) for SECOM. The
Connection step is never shown as "Not needed". `StepCount` becomes
`Steps.Count`; `StepOfText`, `BackCommand` and `GoTo` use indices into the
current list instead of the hard-coded "3 → 1 when no connection" jump.

*Effort:* M (view model + tests). *Files:* `AddOnlineCatalogueWizardViewModel.cs`,
`AddOnlineCatalogueWizardViewModelTests.cs`, `Strings.resx` (drop
`Wizard_ConnectionNotNeeded`, `Wizard_ConnectionForSecom`).

**D4. Stepper subtitles only say what was chosen.** A done step shows its value
(the catalogue name, "Everything", "Puget Sound charts"); the current and
upcoming steps show no subtitle. With D1 each step gets ~210 px, so values fit;
keep the trimming + tooltip as a backstop. Rename step 1 to **Source** (pairs
with D15).

*Effort:* S. *Files:* `AddOnlineCatalogueWizardViewModel.cs` (`Refresh`), `Strings.resx`.

**D5. Footer: hide Back on the first step, say what Next does.** On step 1,
Back is hidden (not disabled) and the left of the footer shows the selection in
one line, "NOAA ENC — all U.S. waters · NOAA ENC", so the footer confirms the
choice even when the selected row has been scrolled away. From step 2 on, the
middle slot keeps its hint role (`FooterHint`) and otherwise shows nothing:
"Step 2 of 3" duplicates the stepper. The primary button keeps its existing
labels (Next / Continue / Add and wait / Add to Library).

*Effort:* S. *Files:* `AddOnlineCatalogueWizardView.axaml`, view model.

### Step 1 information architecture

**D6. Categories, not regions, are the first cut.** A left sidebar (160 px)
lists:

| Category | Today's entries |
|---|---|
| Charts | NOAA ENC, USACE × 2, inland ENC × 14 (17) |
| Depth, tides & currents | NOAA S-102, S-104, S-111 on AWS (3) |
| Weather & ocean | GFS × 4, NWS alerts, NHC, WPC, ERDDAP × 4 (11) |
| SECOM services | curated S-124 Canada (1) + registry when shown |
| My catalogues | the user's URL-added and shared-feed entries (n) |

each with its count. Region becomes the **second** grouping, as headers inside
a category ("North America · United States", "Europe", "Worldwide"), which is
also where the region headers actually help (14 inland lists by country).
Category is an explicit `category` field in `known-sources.json`, not derived
from `format`, because format and category disagree in places (HF-radar and
AOML currents are `weather` but sit naturally beside S-111, see Q3). Registry
services land in SECOM services; URL-added entries in My catalogues.

The dialog opens on **Charts** with NOAA ENC selected (as today, a one-click
Next). It remembers the last category in the session.

*Effort:* M. *Files:* `known-sources.json`, `KnownCatalogueSources.cs`
(`KnownCatalogueCategory` + parse), `CatalogueDirectoryDialogViewModel.cs`
(`SelectedCategory`, `Categories` with counts, grouping), `CatalogueDirectoryDialogView.axaml`,
`Strings.resx`, `CatalogueDirectoryDialogViewModelTests.cs`.

**D7. Search spans all categories.** Typing filters every category at once; the
sidebar shows per-category match counts (zero-match categories dim), the list
shows the matches from all categories under category headers, and clearing the
search returns to the selected category. The count moves out of the search box
into the sidebar (A12). Placeholder: "Search sources by name, provider or
country".

*Effort:* S (on top of D6).

**D8. Rows are two lines, details live in a side pane.** At ≥ 840 px body width
the list is a plain selectable list (no expanding card): line 1 name + format
label, line 2 provider + country only (region is in the header, A8). The
selected entry's details move to a 300 px right pane: name, provider and
region, chips, note, reachability and Fix (SECOM), catalogue URL with a copy
button, Terms & notices, Remove (user entries). Selection no longer moves the
list (A9), and the list shows ~8 rows instead of 3½. The radio marks go: in a
master-detail list the tint plus the details pane is the selection; keep
`AutomationProperties` naming the row (#784).

*Effort:* M. *Files:* `CatalogueDirectoryDialogView.axaml`, `CatalogueEntryViewModel`
(drop `ProviderAndRegion` region part when grouped; add `CopyUrlCommand`).

**D9. Compact inline details when there is no room for the pane** (and as the
quick-win version of D8). Chips move onto the provider line's right; the note
stays one wrapped line; the URL collapses to a "Catalogue URL" link that copies
it (tooltip shows it); Terms sits on the same line. Target: the selected row is
at most 1.5× an unselected row (~80 px instead of ~127).

*Effort:* S. *Files:* `CatalogueDirectoryDialogView.axaml`.

**D10. Escape hatches move into their categories.**
- *Show SECOM services from the registry* becomes the SECOM services
  category's own content: the curated entries, then a single inline action row
  "List services from the Maritime Connectivity Platform registry" (or the summary
  "43 services · read 14:02 · Refresh" once listed). Not on screen in any other
  category.
- *Add a catalogue by URL* becomes the top of **My catalogues**: an always-open
  URL field with "Check and add", the help text, then the user's entries below.
  A small "+ Add by URL" link at the foot of the sidebar jumps there from any
  category. The URL hint (A11) becomes the field's help line: "We recognise NOAA
  and USACE ENC catalogues, chartcatalogs lists and S-100 feeds."
- Search with no matches offers "Add by URL…" in the empty state.

This frees the ~84 px band below the list for every user and gives each action
a context that explains it (A10).

*Effort:* M (mostly moving XAML; registry VM unchanged). *Files:*
`CatalogueDirectoryDialogView.axaml`, `CatalogueDirectoryDialogViewModel.cs`,
`Strings.resx`. Automation ids `Catalogues.ShowRegistry`, `Catalogues.AddByUrl`
and `Catalogues.Url` stay on the moved controls (UI automation #776).

**D11. Quick-win interim for the escape hatches.** Without D6, collapse both
into one quiet link row under the list, left-aligned, no dashed borders:
"Can't find it? **Add by URL** · **Show SECOM registry services**". The URL
panel opens in place of the list's bottom third rather than below it. Rename the
registry label to drop "MCP" from the primary text (A11).

*Effort:* S. *Files:* `CatalogueDirectoryDialogView.axaml`, `Strings.resx`.

### Other steps

**D12. Skip the "Nothing to choose" step.** When the catalogue read finds a
single entry (`IsSingleEntry`), Next from step 1 goes straight to Add to, and
the What to include step shows as done with "One download" (still clickable to
read the explanation). Weather run notes and "Keep downloaded" move to the
review card in Add to for these entries.

*Effort:* M (the read happens on entering step 3 today; it needs to happen on
Next from step 1/2 with a loading state on the button). *Files:* wizard VM,
`CatalogueTargetStepView.axaml`. See Q4.

**D13. Step 3 uses the width.** With D1 the NOAA/S-102 region column can grow
to 240 px and the areas list keeps ~600 px for names and right-aligned details.
No structural change; check the facet tabs strip doesn't stretch past its
content.

*Effort:* S. *Files:* `CatalogueScopeStepView.axaml`.

**D14. Limitations, not capability pills.** *(Revised 2026-11-07 after review
of the mock.)* The Coverage outlines / Editions / Sizes pills go. Their labels
mean nothing without an explanation, and most entries have all three, so they
were mostly noise. A source that lacks one or more shows a small "Before you
download" box in the details pane with a plain sentence for each missing
capability only:

- no coverage: "No coverage on the map until it is downloaded."
- no editions: "New editions are not detected; refresh to check."
- no sizes: "Download sizes are not known in advance."

A source with all three shows nothing. In the compact inline layout (D9) the
same sentences are joined into one amber line under the note. This also removes
the 55 %-opacity contrast problem (A17).

*Effort:* S. *Files:* `CatalogueDirectoryDialogView.axaml`, `CatalogueEntryViewModel`
(a `Limitations` list replacing the chip flags), `Strings.resx`.

**D15. Rename to "Add online source".** Title "Add online source", menu
"Browse online sources…", step 1 "Source". Keep the class names, automation ids
and data paths. See Q1.

*Effort:* S. *Files:* `Strings.resx`, docs that name the dialog.

## 4. Proposed step 1

At 920 × 640 (body ≥ 840 px):

```
┌──────────────────────────────────────────────────────────────────────────────────┐
│ Add online source                                                            ✕   │
│ (1) Source              (2) What to include      (3) Add to                      │
│ ─────────────────────────────────────────────────────────────────────────────────│
│ [ Search sources by name, provider or country                                 ]  │
│ ┌──────────────────┐ ┌────────────────────────────────┐ ┌──────────────────────┐ │
│ │▌Charts        17 │ │ NORTH AMERICA · UNITED STATES  │ │ NOAA ENC — all U.S.  │ │
│ │ Depth, tides &   │ │▌NOAA ENC — all U.S. wat… NOAA ENC│ waters               │ │
│ │  currents      3 │ │ NOAA Office of Coast Survey    │ │ NOAA Office of Coast │ │
│ │ Weather & ocean11│ │ USACE Inland ENC — rivers  IENC│ │ Survey · United St.  │ │
│ │ SECOM services 1 │ │ USACE                          │ │                      │ │
│ │ My catalogues  0 │ │ USACE Inland ENC — buoys   IENC│ │ (Coverage outlines)  │ │
│ │                  │ │ USACE                          │ │ (Editions) (Sizes)   │ │
│ │                  │ │ EUROPE                         │ │                      │ │
│ │                  │ │ Inland ENC — Austria (D… chartc│ │ Choose states, Coast │ │
│ │                  │ │ chartcatalogs · Austria        │ │ Guard districts or   │ │
│ │                  │ │ Inland ENC — Bulgaria (… chartc│ │ regions next.        │ │
│ │                  │ │ chartcatalogs · Bulgaria       │ │                      │ │
│ │ ───────────────  │ │ …                              │ │ Catalogue URL ⧉      │ │
│ │ + Add by URL     │ │                                │ │ Terms & notices ↗    │ │
│ └──────────────────┘ └────────────────────────────────┘ └──────────────────────┘ │
├──────────────────────────────────────────────────────────────────────────────────┤
│ NOAA ENC — all U.S. waters · NOAA ENC                         Cancel   [ Next ]  │
└──────────────────────────────────────────────────────────────────────────────────┘
   160 px                     ~420 px                           300 px
```

SECOM services category (registry not yet listed):

```
│ │ SECOM services 1 │ │ NORTH AMERICA · CANADA         │ │ S-124 Navigational   │ │
│ │                  │ │▌S-124 Navigational warn…  S-124│ │ warnings — Canada    │ │
│ │                  │ │ Canadian Coast Guard           │ │ (Open) (No coverage) │ │
│ │                  │ │ ─────────────────────────────  │ │ …                    │ │
│ │                  │ │ ⊕ List services from the       │ │                      │ │
│ │                  │ │   MCP service registry         │ │                      │ │
```

My catalogues category:

```
│ │ My catalogues  0 │ │ Catalogue URL                                          │ │
│ │                  │ │ [ https://…/catalogue.xml              ] [Check & add] │ │
│ │                  │ │ We recognise NOAA and USACE ENC catalogues,            │ │
│ │                  │ │ chartcatalogs lists and S-100 feeds.                   │ │
│ │                  │ │ ───────────────────────────────────────                │ │
│ │                  │ │ No catalogues added yet.                               │ │
```

(The details pane is hidden while My catalogues has no selection.)

At 720–840 px the details pane folds back into the selected row (D9); below
720 px the sidebar becomes a segmented strip under the search box.

## 5. Open questions

- **Q1.** Rename to "Add online source" (D15)? It's more accurate now that half
  the entries aren't catalogues, but "catalogue" is used across the docs and the
  Library. *Decided: yes (A1).*
- **Q2.** 920 px three-column (D1+D8), or stop at ~800 px two-column with the
  compact inline card (D9)? Three columns gives the biggest density win; two
  columns is less work and keeps the familiar card. *Decided: three columns at 920 px (B2).*
- **Q3.** Categories by *what the data is* or by *how it's reached*? The
  proposal mixes them: SECOM services is a protocol category (it has its own
  Connection step), so a SECOM S-101 service would sit there, not in Charts; and
  HF-radar/AOML currents are weather-format but could sit in Depth, tides &
  currents. Alternative: allow an entry in more than one category. Recommendation:
  one category per entry, set explicitly in `known-sources.json`; currents
  observations stay in Weather & ocean. *Decided: by kind of data, one category per entry; currents observations in Weather & ocean (B1).*
- **Q4.** Is it acceptable for D12 to read the catalogue on Next from step 1
  (spinner in the button) so single-download sources can skip a step, or keep
  the explicit step for predictability? *Decided: yes, read on Next (C1).*
- **Q5.** Should the dialog remember the last category across sessions (a
  setting) or only within a session? *Decided: within the session only (C1).*
- **Q6.** Keep the registry opt-in ("List services…") or list it automatically
  when the SECOM category is opened? It's a network call to the MCP registry;
  today it is deliberately on request (#822). *Decided: on request (B3).*

## 6. Out of scope

- The Add collection manifest dialog (640 × 734) shares `WizardStyles` but is a
  single page; it doesn't need this pass.
- Map-based browsing of catalogues (pick a coverage area first). Worth a later
  look once categories exist.
- Changes to the SECOM Connection step (#883 layout stays) and to the review
  card beyond D12.

## 7. Build slices

*(Revised 2026-11-07: the restructure mock was accepted, so the quick-win
interim versions — D9 as the only details form, D11 — are dropped. Q1, Q2, Q3
and Q6 are taken as the mock answers them: "Add online source", three columns at
920 px, categories by kind of data with currents observations in Weather &
ocean, registry listed on request. Q4 and Q5 stay open; D12 waits on Q4.)*

Two tracks that touch different files and can run in parallel:

**Track A — wizard shell** (`AddOnlineCatalogueWizardView.axaml`,
`AddOnlineCatalogueWizardViewModel.cs`, `LibraryImportCoordinator.cs`,
`DialogWindowFit.cs`, wizard VM tests):

- **A1. Size, steps and footer** (#1348) — D1 (920 × 640, width capped to the window),
  D3 (three steps, four for SECOM), D4 (subtitles only for done steps, step 1
  renamed Source), D5 (no Back on step 1, selection in the footer), D13 (step 3
  region column), D15 (rename to "Add online source").

**Track B — step 1 directory** (`CatalogueDirectoryDialogView.axaml(.cs)`,
`CatalogueDirectoryDialogViewModel.cs`, `KnownCatalogueSources.cs`,
`known-sources.json`, directory VM tests), sequential within the track:

- **B1. Categories and search** (#1346) — D6 + D7: explicit `category` per entry,
  sidebar with counts, region as the second grouping, cross-category search.
- **B2. Details pane, limitations and breakpoints** (#1349) — D8, D14 (revised), D2 and
  D9 as the narrow-width form. Stacked on B1.
- **B3. Escape hatches home** (#1350) — D10: registry inside SECOM services, URL form at
  the top of My sources, sidebar "Add by URL", empty-state link. Stacked on B1
  (can run alongside B2 only if it avoids the list/details markup).

Both tracks add strings to `Strings.resx`; expect a trivial merge there.
**C1 — skip the single-download step and remember the category** (#1353; decided
2026-11-08: Q4 yes, Q5 within the session). Next on the Source step reads the
catalogue while the footer says "Reading the catalogue…"; a catalogue with one
download goes straight to Add to, with What to include done and still reachable
by Back. A failed read lands on What to include with the error and Try again.
SECOM sources and the entry points that start at What to include are
unchanged. An ended forecast's warning also shows on Add to, since its step is
skipped. The directory remembers the last category, and the last source
selected in each category, for the app's lifetime (`CatalogueDirectorySession`,
not persisted). Switching category selects that category's remembered source,
or its first; an empty category selects nothing, so the details and Next never
act on a source the list doesn't show (follow-up, 2026-11-09).

**Mock (2026-11-07).** The restructure is mocked on the "Add Online Source ·
restructure" board on the Library & data page of the design canvas
(artifact 2hWbgQ9wKM1gC2gM51mSCs): `project/Online-Source-Restructure.dc.html`,
frames R1–R8, each mounting the interactive `project/Online-Source-Dialog.dc.html`
with a preset. R1 Charts default, R2/R3 SECOM before and after listing the
registry, R4 cross-category search, R5 My sources with add by URL, R6 step 2
with three steps, R7 760 px (no details pane), R8 640 px (category strip).
Entries come from `known-sources.json`; registry services and the user feed
are placeholders. The mock uses "My sources" for the fifth category.
