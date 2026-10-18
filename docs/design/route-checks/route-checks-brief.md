# Design brief: route checks in Check mode

Status: design round 1 (2026-10-17). This brief belongs to issue [#958](https://github.com/philliphoff/EncDotNet.S100/issues/958), the design round for [#888](https://github.com/philliphoff/EncDotNet.S100/issues/888) (route checks against land, drying ground, dangers, the safety contour, restricted areas, S-122 protected areas and S-124 warnings, plus RTZ and S-421 exchange). [#959](https://github.com/philliphoff/EncDotNet.S100/issues/959) implements the result. It extends the #887 round (`docs/design/route-ukc/` on branch `claude/route-ukc-design`), which designed Check mode, the verdict card, the Clearance tab and the Findings list (F1–F5) for under-keel clearance only.

The mock is the **Route Checks** artboard in the *SoundCharts S-100 Viewer* design canvas: <https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs> (page *Navigation & time*, file `project/Route-Checks.dc.html`). It's built with the *EncDotNet.S100* design system (<https://claude.ai/artifact/UywKszqyKDATtEERvBqWZT>). The handoff is [HANDOFF.md](HANDOFF.md).

Screenshots 1–7 in `screenshots/` come from main at `53ae46fc` (after #976 and #977), and screenshot 8 from `f3eba309` (after #953), driven through the viewer's MCP server at the default 1100×700 window. Two data sets:

- **Generated:** the scenario generator's `warnings-on-route` scenario (`tests/EncDotNet.S100.Testing.Scenarios`, fictitious *Synthetic Sound* at 41°S 135°W): two S-101 cells, seven S-124 warnings, the S-122 seal sanctuary, S-125 channel buoys, and the S-421 route `421ZZ00WARN` re-entered as a viewer route (West entrance → East berth, 2,9 NM). The app clock is frozen at 1 Mar 2026 06:30Z. Passage values are the scenario's: 8 kn, departure 06:00Z, and, where a draft is used, 7,0 m + 0,4 m squat, 1,0 m required UKC, 150 m cross-track.
- **UKHO:** the S-101 trial cells in `~/SynologyDrive/Charts/UKHO Trial Datasets/S-101`, the UKHO S-102 trial cells and the Solent S-104 astronomical predictions (dcf8), with #887's 9-waypoint Nab Tower → Southampton route, draft 11,5 m, 12 kn, 1,0 m required UKC, 150 m cross-track, departing 1 Apr 2021 07:10Z.

Times in the screenshots are in the machine's zone (US Pacific), per the app's time rule.

## 1. Why now

#888's motivation still holds: S-124 is meant to feed route checking rather than sit in a separate list, and the worry people raise most is information overload. The product answer is route-centred: *"these 3 dangers and 1 warning affect your route"*. #887 built the frame (a mode, a verdict, a findings list) but every part of it assumes a draft and a tide. #953–#957 are now adding producers that need neither. This round decides how those results look in the frame #887 left, before #959 builds them.

## 2. What exists today (measured on main 53ae46fc)

| Area | Today | Where |
|---|---|---|
| Check passage toggle | Footer toggle, disabled unless every leg has a draft; tooltip "Add a draft to check clearance" (screenshot 1). #953 is removing that requirement. | `Views/RoutesView.axaml` (`Routes.CheckPassageToggle`), `RoutesPanelViewModel.CanCheckPassage` |
| Verdict card | Clearance only. Titles by priority: "No data for part of the passage" / "Arrival is after the water-level data ends" (neutral) → "Below your UKC setting in N places" → "Meets your settings · tight" → "Meets your settings". Big min-UKC number, confidence lines, window line with *Depart HH:MM*, and the one "Not for navigation" footer. | `Routes.Check.Verdict*`, `Strings.resx` |
| Findings list | Groups MUST REVIEW / CHECK / INFO; tags Clearance, Route check, S-124, Confidence, Data (the last three sources exist in `RouteFindingSource` but only clearance, confidence and data findings are produced). No filters, no merging. | `RouteFinding.cs` (Navigation), `RouteFindingsService` |
| Volume | The Solent route lists **32 findings**: 11 Must review "Below your UKC setting" fragments and 21 Check rows, 16 of them "Charted depth from S-101 only", many "0,0 NM" long (screenshot 5). | MCP `get_findings` |
| Map | Route coloured by UKC state at ETA on a 10 px casing; 30 % wash; legend "UKC AT ETA" bottom left; the selected finding gets a dashed outline and a chip. No markers for unselected findings, no corridor. The legend covers the route at 1:40 000 (screenshot 3). | `Tools/RouteCheckOverlay.cs`, `RouteOverlayRenderer.cs`, `RouteCheckColors` |
| Warnings | S-124 areas, lines and points are portrayed; the Warning card (#976) shows a picked warning's full text in Object Information; the Navigational warnings panel (#977) lists every warning with a Now / View time toggle. Neither knows about routes. Check mode folds Object Information, so the card is out of reach while checking. | `NavigationalWarningsService`, `NavigationalWarningCardViewModel` |
| Route exchange | The route ⋯ menu has Rename / Reverse / Remove. No import or export entry point. `RouteS421Mapping` and `S421DatasetWriter` exist but only tests use them; there is no RTZ code. | `RoutesView.axaml` (`Routes.Row.Menu`) |
| Palettes | Day = #887 handoff; Dusk = engineering values from #933; Night = handoff hues with tuned greys. Night state colours are 3–4× brighter than S-101 Night's brightest chart colour (#934). | `RouteCheckColors`, `RouteOverlayColors`, `ColorContrast.AccentFor` |

### What the screenshots show

1. **`today-01-no-draft-warnings-unjoined.png`.** Generated scenario, no draft. The route runs straight through the survey-area warning (001/26), past the off-station buoy (003/26) and across the seal sanctuary (green dashed). *Check passage* is disabled. Nothing joins the route to any of them.
2. **`today-02-check-mode-ukc-only.png`.** Same route with the scenario's draft. Check mode says "No data for part of the passage" (no S-102 or S-104 in this scenario) and the Clearance tab fills the bottom dock with an empty profile. The two warnings the route crosses aren't mentioned.
3. **`today-03-findings-miss-warnings.png`.** The Findings tab: two Check rows (S-101 depth, no tide). The legend sits over the warnings and the sanctuary.
4. **`today-04-warning-card-below-fold.png`.** A pick on warning 001/26 at the route. The Warning card is below the Depth section and the feature list; at the default window only its first lines are visible, and with three docks open the map is ~360 px wide.
5. **`today-05-solent-32-findings.png`.** UKHO Solent at 07:10. "Below your UKC setting in 11 places", −14,7 m at Calshot Spit. The verdict card is 175 px tall, and 32 findings follow it, most of them fragments.
6. **`today-06-night-finding-zoom.png`.** Night palette, zoomed to the Calshot finding. The red and green route is far brighter than anything on the Night chart. Restricted-area symbols sit beside the route; nothing reports them.
7. **`today-07-warnings-panel-not-route-aware.png`.** The Navigational warnings panel (#977) on the generated scenario: four in force, three not. You can't tell which affect the route, or when you're there.
8. **`today-08-no-draft-check-after-953.png`.** Main `f3eba309`, after #953 (PR #978) merged. The generated route checks without a draft: the card says "No findings for your settings · Add a draft to check clearance" (no route-check producers have landed yet). The route draws grey and dashed, the No-data style, which the handoff replaces with the normal accent (A3). The Clearance tab still opens, showing only "Add a draft to check clearance"; the handoff hides it (A2). The toggle still reads *Check passage*.

## 3. Vocabulary the design should settle

| Term | Meaning | Notes for UI |
|---|---|---|
| Check route | The mode. Runs every check that the route's data allows. | Replaces "Check passage" as the toggle label: clearance is one part. |
| Clearance | The #887 UKC check. Needs a draft (and S-102/S-101 + S-104 for a full answer). | A row on the verdict card, not the whole verdict. |
| Route check | Static checks along the corridor: land, drying ground, dangers, safety contour, restricted areas, S-122 areas, leaving chart coverage. | Tag "Route check". |
| Warning | An S-124 navigational warning that crosses the corridor or lies near it, judged at the ETA. | Tag "S-124"; filter "Warnings". |
| Corridor | The leg's cross-track limits, port and starboard. A half-width (#555). | "Cross-track corridor" in the legend. |
| Must review / Check / Info | The three groups (F1). | Unchanged. Red only for Must review. |
| In force at ETA | A warning's validity compared with the time the vessel is there (or view time when no speed). | "in force · you pass 06:08". |

## 4. Problem areas, with the recommended answers

### A. Check mode without a draft (#953)

*Question:* what does Check mode look like when the route has no draft?

**Answer.** Check mode is for every route with two waypoints. The toggle becomes **Check route**. Without a draft:
- the verdict card's title comes from the findings ("2 to review", "1 to check", "No findings for your settings");
- its Clearance row reads "Not checked · + Add draft" (the link opens the Passage card at Draft);
- the Clearance tab is hidden from the bottom dock; nothing opens it;
- the route keeps its normal active-route style (accent) under the wash. It never takes a UKC colour, so green can't be read as "meets UKC";
- leg rows show only the finding badge (§D).

With speed and departure set, warnings and S-122 periods are still judged at ETA. With no speed, at view time.

### B. Verdict: priority between route findings and UKC

*Question:* do Must review route findings drive the verdict title alongside UKC, and in what priority?

**Answer.** Yes. One verdict, worst group wins, from any source:
1. **Must review** (any source) → destructive. The title names the worst item by a fixed kind order: crosses land → crosses drying ground → enters a prohibited area → danger in the corridor → below your UKC setting → warning in force on the route → other Must review. The line under it names it and counts the rest ("Leg 6 → 7 · Calshot Spit · and 2 more to review").
2. **Arrival is after the water-level data ends** (neutral) → only when nothing is Must review. Static hazards don't depend on tide.
3. **Check** → warning tone. "Meets your settings · N to check" with a draft; "N to check" without.
4. Nothing → "Meets your settings" with a draft; "No findings for your settings" without.

Under the title, three summary rows (Clearance, Route, Warnings), each a button that applies the matching findings filter. The min-UKC value moves into the Clearance row. The departure window line stays, with "Route-check findings don't change with departure" when they exist. The footer stays the only "Not for navigation", now listing every source used.

### C. Map: leg colouring, findings, warnings, corridor

*Question:* colour legs by their worst finding, or keep UKC only? How do findings, warnings and corridor shading combine without clutter?

**Answer.** Keep UKC colouring on the line only. Draw route findings on top:
- **Spans** (drying ground, a restricted area, a warning area the route crosses) get a **sleeve**: a 16 px band along the route in the group's tone at 24 % (20 px, 38 % when selected).
- **Points** (a wreck, an off-station buoy) get a **marker** 18–20 px above the line on a 1 px stem, so the UKC colour stays visible. Shape and tone carry the group: octagon (Must review), triangle (Check), circle (Info). Off-track features get a dotted leader from the marker's foot to the feature. Markers within 24 px cluster into one with a count.
- Info markers appear only when their finding is selected.
- **Corridor:** two dotted edge lines for the whole route; a fill only on the selected finding's leg.
- **Labels:** only the selected finding has a pill. Leg labels follow A7 as before.
- **Legend:** one box, "UKC AT ETA" (with a draft) plus "FINDINGS". It folds to a "Legend" chip; the folded state is remembered.

Rejected: colouring a leg by its worst finding. It mixes two meanings on one line, hides where on the leg the problem is, and a 70 m buoy warning would turn a whole leg red over a passing clearance.

### D. Panel density

Not asked, but screenshot 5 forces it. Findings with the same code merge into one row with a count (×9) in Check and Info; Must review never merges. Expanding a merged row shows its stretches as sub-rows. Info is folded by default. Leg rows get a badge (worst route-check group and count) beside the min-UKC chip.

### E. Warnings view and warning text (#932, #957)

*Question:* a separate "Warnings affecting this route" view or a filter? How is warning text presented?

**Answer.** A filter. The Findings list gets source chips: All · Clearance · Route · Warnings. *Warnings* is "warnings affecting this route". The Navigational warnings panel gets a scope toggle "All | On {route}" only while Check route is on, and a line on each affected row ("On route · 06:08", "Near route · 06:19"). The viewport-scoped list from #888 is dropped: the map already is the viewport view.

Selecting an S-124 finding expands its row into the #976 Warning card (text clipped at 5 lines, *Show all*), because Object Information is folded in this mode. A link opens it in the Navigational warnings panel. Severity (for #957): crosses the corridor and in force at ETA → Must review; within 0,5 NM (configurable) and in force → Check; not in force during the passage, or cancelled → Info, with the reason.

### F. Import and export (#960, #961), in the #391 model

*Question:* one Import / Export flow for RTZ and S-421; format choice; what's dropped on import.

**Context.** The 2026-10-16 "Revised direction" comment on [#391](https://github.com/philliphoff/EncDotNet.S100/issues/391) (now superseded by #960) set the model: an S-421 dataset you open becomes a **read-only route** shown in **Native** mode (its own portrayal), which can switch to **Planning** mode (the route renderer, read-only but checkable); editing needs **Copy to edit**, which makes an owned route that keeps its source and the S-421 content the viewer can't edit. Round 1's first draft of this section imported files straight into editable routes, which is the approach #391 replaced. This version follows #391.

**Answer.**
- **Import route…** is an icon button in the Routes header. It opens a file as a **received route**, read-only, with no dialog on success. The format and version are sniffed from the content. An S-421 file also loads as a dataset and starts in Native mode; RTZ has no native portrayal and opens in Planning mode. An S-421 dataset you open explicitly becomes a received route the same way; datasets loaded by panning a Library source don't (#391 concern 6).
- **Copy to edit** (footer, ⋯ menu, Ctrl+E, waypoint menu) opens the summary that round 1 put on import: what the copy keeps, and what it can't hold (kept and written back on export), with the copy's identity. Nothing is lost by opening, so the summary belongs where the viewer's model starts dropping things.
- **Export…** sits in the route ⋯ menu for both kinds of route. The dialog picks RTZ (1.2 by default; 1.1 and 1.0 available) or S-421 (edition 1.0), shows the route's identity in the file, and lists only the fields this route has that the chosen format can't hold. Findings and verdicts are never exported.
- **Checking a received route** works like an owned one. Check route switches it to Planning mode while on and back afterwards, keeps the S-421 action-point layer on (the route renderer doesn't draw action points, #391 concern 4), and puts Copy to edit where Edit was. Closing the dataset ends the check cleanly.
- **Check settings (#391 concern 2; to confirm).** Draft, squat, required UKC and departure are *your* settings, not the route's: a local per-route layer, editable on received routes, defaulting to values in the file, and not exported unless you tick *Also write my check settings*. The Passage card shows them as **Your check settings**, above the route's own speed and cross-track (read-only on a received route).
- **Copy identity (#391 concern 9; to confirm).** A copy gets a new route ID, a previous-route reference to the source's ID and edition, edition 1 and status Planned. A received route exported unchanged (say RTZ → S-421) keeps its identity.

### G. Dusk and Night tones (#934)

*Question:* Dusk/Night tones for findings, folded in with #934.

**Answer.** Findings reuse the state tones: Must review = Below, Check = Tight, Info = Low confidence. And #934's five questions:
1. **Night comes down** to about 0,10 relative luminance per state (today 0,10–0,22), keeping 3:1 on the black casing and about 2,6:1 on DEPVS and LANDA. Proposed: Meets `#226746`, Tight `#735614`, Below `#A4323C`, Low confidence `#57606F`, No data `#515A65`.
2. **Day Tight darkens** from `#E0A008` (2,3:1 on its white casing) to `#B07D00` (3,6:1).
3. **Dusk keeps #933's values** and the black casing: the Dusk chart is dark even though the Dusk chrome is light.
4. **No data becomes dotted** (1,5/3,5) while Low confidence stays dashed (6/4): a second cue besides brightness.
5. **Night editing uses a fixed `#3F64A6`**, not a desaturated user accent.

## 5. Constraints

- Avalonia + ShadUI, Light/Dark (and S-100 Dusk/Night) chrome; Day/Dusk/Night chart palettes for the map. Docks as in #887. Strings in `Resources/Strings.resx`; an AutomationId on every interactive element (#776); MCP parity for every new state.
- Never "Safe". "Not for navigation" appears once, on the verdict card. Red only for Below your UKC setting and Must review.
- Implement from the specs only. No GPL sources.
- Tests use the scenario generator or synthetic fixtures; no real ENC data committed.
- Design against #953's and #954's issue scopes; both are in progress.

## 6. What's in the mock

- **2a** No draft, generated scenario, Light/Day. Live: findings select, filter chips work, the legend folds.
- **2b** Draft set, UKHO Solent, combined verdict, Light/Day.
- **2c** S-124 finding selected with the Warning card inline, Dark chrome, proposed Night tones.
- **2d** The map treatment on Day, Dusk and Night, and the tone table with old values.
- **2e** Copy to edit (what the copy keeps). **2f** Export dialog and the ⋯ menu.
- **2h** A received S-421 route checked without a draft: read-only badge, Planning while checking, check settings split from route values, Copy to edit.
- **2g** Components: six verdict variants, leg rows with badges, a merged row, the warnings panel on a route, markers.
