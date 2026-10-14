# Design brief: reading navigational warnings in SoundCharts

Status: brief for design, 2026-10-14. It's about how a mariner reads S-124
navigational warnings in SoundCharts: the message text, and warnings that have
no location. It covers #932 and the warnings that don't draw.

## 1. Why now

SoundCharts now receives live S-124 warnings. CCG publishes them openly over
SECOM, and AMSA publishes them over SECOM with an MCP identity (#882, #916).
AMSA's NAVAREA X and AUSCOAST series download, unpack and draw around
Australia, and the reader keeps Edition 2.0.0's warning text (#930). Today a
warning is drawn and can be picked, but its message can't be read where a
mariner looks first, and a warning with no location can't be found on the map
at all.

## 2. What a warning is (measured from AMSA's live data, 13 Oct 2026)

A warning is a dataset: one `NavwarnPreamble` (the message's identity) and one
or more `NavwarnPart` features (what it says and where), often with a
`NavwarnAreaAffected`.

- **The message.** Each part carries `warningInformation`:
  - `information/text`: the full message, 6–18 lines in AMSA's set. Here's
    NAVAREA X 106/26:

    ```
    SECURITE
       FM JRCC AUSTRALIA 262222Z SEP 26
       NAVAREA X 106/26
       SPECIAL PURPOSE VESSEL LAYLA / V2YX9 / 304716000 CONDUCTING CABLE RECOVERY OPERATIONS ALONG THE FOLLOWING LINE:
       A 14-45.98S /159-08.93E
       B 15-26.23S /158-28.20E
       …
       2NM CLEARANCE REQUESTED.
    NNNN
    ```

  - `information/language`: there may be one `information` per language.
  - `navwarnTypeDetails`: the type, a code list (for example 65 "Cable
    Operations", 208 "Presence of Scientific Equipment").
- **Its identity (preamble):**
  - the series and number: `messageSeriesIdentifier`, giving "NAVAREA X
    106/26";
  - its type: `warningType`, for example "NAVAREA Navigational Warning";
  - `navwarnTypeGeneral`, for example "Works";
  - the general area and locality (for example "NAVAREA X");
  - `publicationTime`.
- **When it applies:** `fixedDateRange` on the part (start, sometimes end,
  with times of day). For example, the bulletin is in force 8–15 Oct.
- **Where:** 23 surfaces, 11 points and 5 curves across AMSA's 22 parts.
  Portrayal draws a magenta NW symbol, with a dashed outline for areas and
  lines (S-124 2.0.0 portrayal catalogue, run as written).
- **No location:** 1 of the 22 parts has no geometry. It's NAVAREA X 110/26,
  AMSA's weekly **"warnings in force" bulletin**, which lists the numbers of
  every NAVAREA and AUSCOAST warning in force. Its catalogue entry gives
  world-wide bounds. Other services may send safety messages or cancellations
  the same way.
- **Volume:** AMSA has about 22 warnings in force at a time, across 5 series.
  Larger services may list many more (we haven't measured CCG's count).

## 3. What exists today (measured from code)

- **Map:** warnings draw through the S-124 portrayal catalogue. Text-only parts
  draw nothing.
- **Object Information** (the pick panel, `PickReportView`): it lists the
  features under the cursor. A picked part shows its attributes as rows; the
  message is one row, "Information › Text" (#931).
  - Every value is one line, cut off at the value column's width (by design),
    so each line of the message shows only its first characters ("FM JRCC
    A…").
  - The full text is only in a tooltip, or under "Open in Examiner".
  - The panel already has a **referenced-text card** (S-101 `TXTDSC` text
    files): a bordered card with a title and file name, the text, and Copy.
    It's the nearest existing pattern.
  - The preamble's identity ("NAVAREA X 106/26") isn't shown with the part.
    The part's `header` reference points to it, and the user has to follow it.
- **Library:** each warning dataset is an item, named by file (for example
  `124AU01NA10626`), grouped under its SECOM exchange set ("NAVAREA X").
  - Details show catalogue facts, not the message.
  - "Show on map" loads warnings as you pan. A world-bounds bulletin loads
    everywhere and draws nothing.
- **Datasets panel:** lists the loaded files, but not the warnings by title.
- **Route checking** (#887, #901): the findings list has an S-124 finding type
  ("a navigational warning that affects the route"). Nothing produces it yet
  (#888).
- **Timeline:** shows time-bound data. Whether a warning's `fixedDateRange`
  hides it outside its validity isn't defined for S-124 yet.
- **No warnings list exists anywhere.**

## 4. Problem areas

### A. Reading a warning's message

- The message is the reason to pick a warning. A truncated row isn't enough.
- How much room does it get in the panel (full height, collapsible, scrolling)?
- The text keeps its line breaks and indentation. Is it monospaced, like a
  telex, or proportional?
- Several languages: show one with a switcher, or all of them?
- Show the type ("Cable Operations"), the series and number ("NAVAREA X
  106/26"), and the validity ("from 26 Sep 22:22 UTC", "until 15 Oct") with the
  text, as a header, instead of as separate attribute rows.
- Copy, and Open in Examiner, stay available.
- Should this be S-124-specific, or a general treatment for long or multi-line
  text values (S-122 and S-127 regulations, S-101 text), with S-124 the first
  case?

### B. Warnings with no location

- They can't be picked, so Object Information never sees them.
- Where does a mariner read them? Options include:
  - a warnings list (panel or dock);
  - a notice badge ("2 text-only warnings");
  - an entry in the Datasets or Library panel that opens the text;
  - a marker at the series' area, such as the NAVAREA, with care: S-124
    portrayal draws nothing for them.
- Their world-wide bounds make "Show on map" load them everywhere. Should a
  text-only warning be "on map" at all?

### C. A list of the warnings in force

This is likely the natural home for both A and B. ECDIS practice is a list of
navigational warnings alongside the chart.
- **Columns:** series and number, type, area, validity, and whether it has a
  location.
- **Grouping:** by series (NAVAREA X, AUSCOAST) or by source. Sort by
  publication or by number.
- **Behaviour:**
  - selecting a warning with a location centres and highlights it on the map;
  - selecting one without a location shows its text;
  - selecting a cancelled or expired warning: already excluded today, since
    cancelled datasets are left out (#919);
  - new since the last refresh: a badge?
- **Filter:** in force now, or at the timeline's time; or affecting my route,
  once #888 produces S-124 findings.
- **Scale:** tens of warnings (AMSA) today; design for hundreds.
- **Relationship to the Library:** the Library lists files and sources. This
  list would list the messages in them.

### D. Time

- Should a warning outside its `fixedDateRange` be hidden, dimmed, or listed as
  "not yet in force" or "expired"?
- Does the timeline's time (not only "now") decide which warnings are in force?

### E. Route context (later, with #888)

- A warning that crosses the planned route is a route finding. Design the list
  and the finding so they link both ways: finding to warning text, and warning
  to "affects your route".

## 5. Constraints

- **Portrayal:** the S-124 portrayal catalogue decides what draws on the map
  (S-100 Part 9). Don't invent map symbology for text-only warnings without
  saying it's an addition.
- **Time:** all times are in the machine's zone, with the existing Local/UTC
  preference. Never show the map area's local time. Warnings are written in
  UTC (DTG "262222Z SEP 26"), so show both if that helps.
- **Accessibility and automation:**
  - text must be selectable and copyable;
  - new controls need AutomationIds, as elsewhere;
  - capture/MCP screenshots omit popups and flyouts, so prefer in-panel UI
    for anything that needs checking.
- **Existing styles:** the side panels (Object Information, Library, Datasets),
  the bottom dock tabs (route Clearance/Findings) and the referenced-text card.
  Light and dark themes.
- **Not for navigation:** AMSA's data says so ("TRIAL … DO NOT USE FOR
  NAVIGATION"), and the item already carries `notForNavigation`. Decide whether
  and how the warning view shows it.

## 6. Scenarios to mock against

1. **Pick a warning on the map:** NAVAREA X 106/26, the cable recovery line
   in the Coral Sea. Read the whole message, its type and validity, and copy it.
2. **Pick where two warnings overlap:** AUSCOAST 208/26 covers a large area
   off western Victoria, and 314/26 lies inside its bounds. Tell them apart and
   read each one.
3. **The weekly bulletin:** NAVAREA X 110/26 has no location. A mariner opens
   SoundCharts and wants to know what's in force: find and read the bulletin.
4. **See what's in force:** about 22 warnings across NAVAREA X and AUSCOAST
   (AMSA), plus other sources if added. Scan, filter to in force now, and jump
   to one.
5. **A warning in two languages:** S-124 allows one `information` per
   language. The samples we have are English only, so mock a bilingual one
   (for example English and French). Read either.
6. **Later:** a warning crosses my route. See it in Findings, read it, and see
   where it is.

## 7. Directions to explore (alternatives, not a decision)

- **Text card in Object Information:** a "Warning" card at the top of a picked
  part. Its header carries the series and number, type and validity; the body
  is the full text, with language tabs, Copy and Examiner. The attribute rows
  stay below.
- **A Warnings panel:** a left-panel list of warnings in force. Selecting one
  opens the same card, whether or not it has a location.
- **A Warnings tab in the bottom dock,** next to the route Findings: good for
  scanning a long list while keeping the chart visible.
- **Library-centred:** warnings as Library items titled by series and number,
  with the text in their details. It's the least new UI, but the Library is
  about sources and files, not messages.

## 8. Questions for design

1. Where do warnings live: Object Information only, a dedicated list, or both?
   Panel or dock?
2. Is the full-text card S-124-specific or a general long-text pattern?
3. How are text-only warnings surfaced? Do they appear on the map in any form?
4. Which header facts matter most at a glance: the series and number, type,
   validity, or area?
5. How do time (now versus the timeline's time) and validity change what's
   listed and drawn?
6. Several languages: switcher, preference, or both shown?
7. How does a warning link to the route's findings (#888)?
8. Is "not for navigation" shown on the warning?

## 9. Requested deliverables

- Mockups for scenarios 1–4 in the light theme, plus scenario 1 in the dark
  theme.
- The chosen home for warnings (Object Information card, list, dock or a
  mix), with the reasons and the rejected alternatives.
- Component notes for the text card: header fields, text styling, language
  handling, actions, empty and error states, and long-text behaviour.
- If there's a list: its columns, grouping, sorting, filters, selection
  behaviour and states (no warnings, loading, stale or unreachable source).
- A short handoff in the style of the earlier ones (HANDOFF.md with decisions,
  open questions and AutomationIds).

## 10. References

- Issues:
  - #932 (show warning text in full);
  - #930 (2.0.0 warning text and type);
  - #931 (nested attribute names);
  - #916, #919 and #925 (AMSA exchange sets and cancellations);
  - #887, #888 and #901 (routes and findings).
- Data: AMSA's live capture (13 Oct 2026), and CCG's 2.0.0 fixture
  `tests/datasets/S124/navwarn_ccg_ed2.gml`.
- Screenshots: Object Information today (truncated text), and AMSA's warnings
  on the map. Ask for them; they're kept outside the repository.
