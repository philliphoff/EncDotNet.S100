# Time overlays on the map

> Status: **Built** (2026-10-31): D1–D13; Q1–Q5 settled in §7. Slices 1–2 in
> PR #1199, slices 3–4 in the follow-up PR. Follows the Timeline epic
> [#707](https://github.com/philliphoff/EncDotNet.S100/issues/707): the map
> stamp ([#713](https://github.com/philliphoff/EncDotNet.S100/issues/713) B4),
> the Time HUD ([#712](https://github.com/philliphoff/EncDotNet.S100/issues/712),
> PR #727) and Display Not Real Time ([#993](https://github.com/philliphoff/EncDotNet.S100/issues/993)).
> Touches `Views/TimeHudView.axaml`, `MainWindow.axaml`,
> `ViewModels/MainViewModel.cs`, `ViewModels/TimelineViewModel.cs` and the
> readout row of `Views/TimelineView.axaml`.

A user with weather wind barbs on the map reported two problems:

1. The map shows the same time twice. Top left: `● 10/31/2026 2:00 PM · in 23 min`.
   Bottom centre: `PINNED | 10/31/2026 2:00 PM  in 23 min | ‹ › | Live | ▭`.
2. The bottom bar changes width when the time is stepped. It's centred, so ‹ and ›
   move under the pointer. A second click on › can land on ‹ or **Live** and
   undo the step.

This note explains why each overlay exists, why they now overlap, and why the
bar moves. It then proposes numbered decisions an implementation PR can
follow.

## 1. What's there today

### 1.1 The two overlays

| | Map stamp | Time HUD |
|---|---|---|
| Where | Top left, 12 px in (`MainWindow.axaml:648–678`) | Bottom centre, 14 px above the edge (`MainWindow.axaml:630–636`, `Views/TimeHudView.axaml`) |
| Added by | #713 B4 (slice 1) | #712 (slice 6, PR #727) |
| Purpose | A read-only caption, so the map (and any capture of it) says which time it shows while the Timeline **dock** holds the controls | The Timeline's everyday controls while the dock is **closed**, so nothing larger than a 40 px bar sits over the map |
| Interactive | No (`IsHitTestVisible="False"`) | Yes: time (focuses the bar for ←/→), ‹ ›, Live, dock toggle; message chip with an action |
| Text | `StampText` (`TimelineViewModel.cs:362–370`) | `ModeLabel`, `CurrentTimeLabel`, `OffsetText`, `drawing…` (`TimeHudView.axaml:118–154`) |
| Visible when | `IsMapStampVisible => Timeline.IsNotRealTime \|\| (Timeline.IsActive && IsBottomDockShown)` (`MainViewModel.cs:387`) | `IsTimeHudVisible => Timeline.IsActive && !(IsBottomDockShown && IsTimelineTabSelected)` (`MainViewModel.cs:381`) |

As designed in #712/#713, the two were **mutually exclusive**. The stamp shows
while the dock is open and the HUD is hidden, and the HUD shows while the dock
is closed. Both are inside `MapArea`, so `capture_app_screenshot` region
`Map` (`Services/AppScreenshotProvider.cs:60`) includes whichever one is shown.

### 1.2 What each one shows, by state

The time format follows the Local/UTC preference (`TimeFormatting.Format`,
`ActiveFormat`).

| State | Map stamp | Time HUD bar |
|---|---|---|
| Live, dock closed | hidden | `[● LIVE] 10/31/2026 2:00 PM  now │ ‹ › │ ▭` |
| Pinned future, dock closed | hidden | `[PINNED] 10/31/2026 2:00 PM  in 23 min │ ‹ › [Live] │ ▭` |
| Pinned past, dock closed | hidden | `[PINNED] 10/31/2026 8:00 AM  5 h 37 ago │ ‹ › [Live] │ ▭` |
| Drawing a new time | grey dot, `10/31 1:00 PM · drawing 2:00 PM…` | adds `drawing…` before the separator |
| Dock open, **Timeline** tab | `● LIVE · …` or `● … · in 23 min` | hidden |
| Dock open, **Logs or Clearance** tab | **shown** | **shown** (the dock toggle then switches the dock to Timeline, `MainViewModel.cs:1500–1508`) |
| Pinned with S-124 / dated S-101 data (`IsNotRealTime`) | **always shown**: `Display Not Real Time · … · in 23 min` | shown too while the Timeline tab isn't visible |
| Window < 900 px | dock hidden (`DockMinimumWindowWidth`, `MainViewModel.cs:344`) | HUD is the only control; the dock toggle stays but is disabled |
| Map < 640 px / < 540 px | — | drops the offset, then the PINNED pill (`TimeHudView.axaml.cs:17–20`, `.axaml:76–82`) |

The bottom dock's own readout row (`Views/TimelineView.axaml:283–412`) has a
mode pill, the time and the offset on the left. On the right it has ⏮ ‹ [step
menu] › ⏭ [Go live] [window preset].

## 2. Problems

### 2.1 Why both overlays show (complaint 1)

The overlap is a regression, not a design choice. The two rules drifted
apart:

- **#922 (Clearance tab, 2026-10-12)** gave the bottom dock more than one tab.
  It changed the HUD rule to "hidden only while the dock shows the **Timeline
  tab**" (`ed74cfb1`, `MainViewModel.cs:381`). That's right: on the Clearance
  or Logs tab the Timeline controls are gone, so the HUD comes back. But the
  stamp rule still says "shown while the dock is shown", **whatever the tab**.
  With the Logs tab open (the new Logs panel, #1080, makes this common), the
  map gets both.
- **#993 (2026-10-18)** made the stamp stay visible "always while the display
  isn't real time" (S-98 §12.7.2). With the dock closed, the HUD is also
  shown, so a pinned map with navigational warnings has both, and the stamp
  is the only one that says *Display Not Real Time*.

The screenshot shows a plain stamp (no *Display Not Real Time*) over weather
data, which isn't date-dependent. So it's the first case: the dock was open on
a non-Timeline tab.

### 2.2 Why the bar moves under the pointer (complaint 2)

The bar is a horizontal `StackPanel` in a `Panel` with
`HorizontalAlignment="Center"` (`TimeHudView.axaml:85, 114, 116`). Its width
is the sum of its children, and several of them change size or appear when
the time changes. **Any** width change ΔW moves the left edge by ΔW/2. A
control at the left also moves by however much the content before it grew.

Children that change on a step (rough widths at 12–13 px):

| Child | Live → Pinned (› pressed) | Changes while stepping |
|---|---|---|
| Mode pill | `● LIVE` (dark, dot, ~54 px) → `PINNED` (outlined, no dot, ~59 px) | — |
| Time (mono) | same | `2:00 PM` → `12:00 PM` adds a character (~8 px) |
| Offset | `now` (~22 px) → `in 1 h` / `in 23 min` (~40–55 px) | `in 1 h` → `in 11 h 30` → `in 2 d 4 h` |
| `drawing…` | appears after every step while the map draws (~50 px), then disappears | yes, twice per step |
| Live button | appears **right of ›** (~52 px with spacing) | — |

Live → Pinned grows the content left of ‹ by about 35 px and the content
right of › by about 52 px. ‹ › move left by roughly (35 − 87/2) ≈ 8 px, and a
filled **Live** button now starts 6 px right of ›. Then `drawing…` appears
left of ‹ and pushes ‹ › right by about 25 px until the frame lands, then
they jump back. A user who clicks › repeatedly is clicking a moving target,
and the targets next to it, ‹ and **Live**, both undo the step. The
centring, the content-sized text and the controls appearing *between* the
pointer and the edge all add to the problem.

**The dock has the same bug.** In `TimelineView.axaml:334–411` the step group
is a right-aligned `StackPanel`. **Go live** appears inside it, right of ⏭,
when the time is pinned, so ⏮ ‹ [step] › ⏭ jump about 80 px left on the first
step. The step menu's label (`StepLabel`) also changes width with the step
kind, which moves › and ⏭.

### 2.3 Other findings

| # | Finding | Where |
|---|---|---|
| P1 | **One state, three ways.** In Live the HUD reads `● LIVE`, the time, then `now`; the stamp adds a fourth. Pinned reads `PINNED`, the offset **and** a **Live** button. The words carry the mode; the pill is redundant with them. | HUD bar |
| P2 | **"Live" means two things.** The dark `LIVE` pill is a *status*. The `Live` primary button is an *action*. The dock calls the same action **Go live** (`TimelinePanel_GoLive`). Next to `PINNED`, a button reading `Live` looks like a second status. | `TimeHud_Live` vs `TimelinePanel_GoLive` |
| P3 | **"PINNED" is jargon** and suggests a map pin (we already have route waypoints and pick markers). It's all caps, which reads as an alarm, but being pinned is a normal choice. | `TimelinePanel_Pinned` |
| P4 | **Hierarchy.** The pill, the 13 px semibold mono time and the accent-coloured offset all compete. In the pinned state the filled primary **Live** button is the loudest thing on the map, louder than the time. | HUD bar |
| P5 | **Not theme-aware.** The live pill is hard-coded `#18181B` with white text (dark-on-dark in the dark theme). The chip tints `#D1FFFBEB`/`#D1FEF2F2` and the text `#B45309`/`#B91C1C` are light-theme only. The dock readout repeats the `#18181B` pill. | `TimeHudView.axaml:16–39, 50–55`; `TimelineView.axaml:20–26` |
| P6 | **The dock toggle is cryptic.** `PanelBottom` is a generic layout glyph with no label. In a narrow window it stays visible but disabled (`CanExecute => !IsWindowNarrow`), a dead button with no explanation. When the dock is open on another tab it *switches tab*, but the tooltip still says "Open timeline (T)". | `TimeHudView.axaml:181–189`; `MainViewModel.cs:1500–1509` |
| P7 | **Discoverability of the step.** ‹ › step "the next time sample", but the HUD doesn't say how far (hour, sample of which layer). The dock has a step menu; the HUD has nothing, so you only find out by stepping. | `Tooltip_TimelineNextStep` |
| P8 | **Keyboard.** ←/→/N/T work while the map, the Timeline or the HUD has focus (`MainWindow.axaml.cs:1662–1692`). But the only hint is the time's tooltip ("click, then ← → to step"), and a pointer user has no reason to click the time. Tab order inside the HUD is fine. | `Tooltip_TimeHudTime` |
| P9 | **Accessibility.** The bar has no group name. The mode is only in a pill with no automation name (the live pill has no `AutomationId` either). The offset isn't part of any accessible name, so a screen reader hears the time but not whether it's live. | `TimeHudView.axaml:118–150` |
| P10 | **Display Not Real Time** lives only in the stamp. While the HUD is the time surface, the S-98 indication is in a different corner from the control that caused it (the pin). | §2.1 |

## 3. Decisions

### One time surface at a time

**D1. Exactly one time overlay on the map.** The rule depends on whether the
Timeline tab is on screen:

| Timeline tab on screen? | Map shows |
|---|---|
| No: dock closed, window < 900 px, or dock on Logs / Clearance | **Time HUD** (controls) |
| Yes | **Map stamp** (caption) |

In code: `IsMapStampVisible => Timeline.IsActive && IsBottomDockShown &&
IsTimelineTabSelected`, which is the exact complement of `IsTimeHudVisible`
while the timeline is active. That fixes complaint 1 and restores the
#712/#713 intent.

**D2. Display Not Real Time follows the surface, not the stamp.** When
`IsNotRealTime` is true, whichever surface is shown says so:

- **Stamp:** unchanged (`Display Not Real Time · time · offset`).
- **HUD:** the status slot (D5) reads `NOT REAL TIME` in the warning tone,
  instead of the plain pinned dot. The full phrase *Display Not Real Time*
  is the slot's tooltip and accessible name.

The HUD is in the `Map` capture region, so captures still carry it.
`IsMapStampVisible` no longer ORs in `IsNotRealTime`.

**D3. The two surfaces read the same, in the same order.** Each one is a
status dot, the time, then the offset. They differ only in role: the stamp
is a caption (no controls, top left, small), the HUD is the control (bottom
centre). The stamp drops the `LIVE ·` prefix and uses the green dot plus a
muted `now`, exactly as the HUD does (D5). Then the user reads one format
everywhere, including the dock's readout.

### A bar whose controls never move

**D4. The HUD has a fixed geometry.** The bar is a `Grid` with fixed column
widths, not a content-sized `StackPanel`. Its total width therefore doesn't
change with the mode, the time, the offset or drawing. Centring is harmless
because there's nothing to re-centre. The width may change **only** when:

- the map crosses a width class (`compact` < 640, `tiny` < 540, D10),
- the time format or culture changes (Local ↔ UTC, a 12/24-hour locale), or
- the dock toggle's availability changes (window crosses 900 px, D9).

None of these can happen during a click on ‹ or ›. As a belt and braces, the
controls group is on the **right** of the bar and the variable text on the
left, so even a future slot that misbehaves can't push the buttons from the
pointer side.

Slots, left to right (regular class):

| Slot | Width | Content |
|---|---|---|
| Status | 14 px | dot: green (Live), accent (pinned), grey pulsing (drawing), warning (not real time) |
| Time | measured, see D6 | `CurrentTimeLabel`, mono, left-aligned |
| Offset | 84 px, left-aligned, ellipsis, full text in tooltip | `now` (muted) · `in 23 min` · `5 h 37 ago` · `in 2 d 4 h` |
| Separator | 9 px | — |
| ‹ | 28 px | previous step |
| › | 28 px | next step |
| Live | 64 px | toggle, D5 |
| Separator | 9 px | — |
| Dock | 28 px | D9 |

**D5. Live becomes one fixed-size toggle; the mode pill goes.** A single
`ToggleButton` in a fixed 64 px slot, always present, replaces both the
`● LIVE` / `PINNED` pill and the `Live` button that appears and disappears:

- **Live:** checked, quiet fill, green dot and `Live`. Clicking it again does
  nothing (it's already live). The tooltip says "Showing now and keeping up
  with it."
- **Pinned:** unchecked, outlined, `Go live` (the dock's word, see P2). The
  tooltip is the current `Tooltip_TimelineNow`.

The left status dot and the muted `now` carry Live at a glance. The pinned
state needs no word: the accent dot, the offset (`in 23 min`) and an
unchecked **Go live** say it. That drops `PINNED` from the HUD (P1, P3).
The button is never the primary (filled accent) style, so it no longer
outshouts the time (P4).

**D6. Stable text slots.** The time slot's width is measured **once per
format** from a template string with the widest digits and meridiem
(`12/28/2026 12:00 PM`, or `28.12.2026 23:00`, or `2026-12-28 23:00Z`),
and not from the live text. It's recomputed on `TimeFormatChanged`. The
time keeps the mono face. The offset uses `MonoFontFamily` too (or
`FontFeatures="+tnum"` if the UI font supports tabular figures), so
`in 1 h` → `in 11 h` doesn't jitter. It's left-aligned so its start never
moves.

**D7. `drawing…` leaves the row.** While the map draws, the status dot goes
grey and pulses, and the time button's tooltip reads `drawing 2:00 PM…`. No
text is inserted into the bar. If drawing takes longer than ~1 s, the
existing message chip above the bar (F4, which grows upward and doesn't move
the bar) shows `Drawing 2:00 PM… · Cancel`, using the existing
`CancelDrawingCommand`.

**D8. Step buttons keep their place at the ends of the range.** At the first
or last step ‹ / › are disabled, not hidden. (They aren't hidden today
either; this pins it down as a rule.)

### The dock toggle

**D9. Say what the toggle does, and hide it when it can't.** Keep the
`PanelBottom` icon, but its tooltip and accessible name follow the state:
**Open timeline (T)** with the dock closed, **Show timeline (T)** with the
dock open on another tab. Below 900 px, where the dock can't show, hide it
(the width change happens on a window resize, never under a click).

### Narrow maps

**D10. Narrow classes swap whole slots.** `compact` (< 640 px) drops the
offset slot. `tiny` (< 540 px) also shortens **Go live** to an icon-only
toggle (28 px, `Live` glyph, same tooltip). Each class has fixed widths, so
D4 holds within a class. There's no pinned label to drop any more (D5).

### Theme, keyboard, accessibility

**D11. Theme brushes only.** The surface stays `MapOverlaySurfaceBrush`. The
dot and toggle use `SuccessColor`, `AccentBrush`, `MutedColor`, and the
warning/error tokens the rest of the viewer uses. Delete the hard-coded
`#18181B`, `#D1FFFBEB`, `#D1FEF2F2`, `#B45309` and `#B91C1C` (HUD and dock
pill) in favour of theme resources, with dark variants defined next to
`MapOverlaySurfaceBrush` in `App.axaml`.

**D12. Name the parts.** The bar gets `AutomationProperties.Name` "Time".
The time button's name becomes the full reading, e.g. "2:00 PM, in 23
minutes, pinned" or "2:00 PM, live". The Live toggle exposes its toggle
state. The status slot gets `AutomationId="TimeHud.Status"` (and the old
`TimeHud.PinnedPill` id is retired; the preview packages allow clean
breaks). The tooltips on ‹ › add the step and its key: "Next hour (→)",
"Next sample of cbofs (→)", from the same `StepLabel` the dock uses (P7,
P8).

### The dock readout

**D13. The dock's step group gets the same rule.** In
`TimelineView.axaml`, **Go live** becomes the same always-present toggle in
a fixed slot, and the step menu button gets a `MinWidth` that fits its
longest label (measured as in D6). Then ⏮ ‹ [step] › ⏭ stay put across
Live/Pinned. The dock's mode pill follows D5 (dot plus offset, no `PINNED`
word) so the dock, the HUD and the stamp read alike.

## 4. Mock-ups

The HUD, regular width. `│` is a separator, `[ ]` a button; column positions
are the same in every row.

```
Live
╭──────────────────────────────────────────────────────────────────────────╮
│ ●  10/31/2026  2:00 PM   now          │  ‹   ›  [● Live ]  │  ▭          │
╰──────────────────────────────────────────────────────────────────────────╯

Pinned, future (after pressing ›)
╭──────────────────────────────────────────────────────────────────────────╮
│ ●  10/31/2026  3:00 PM   in 1 h       │  ‹   ›  [Go live]  │  ▭          │
╰──────────────────────────────────────────────────────────────────────────╯

Pinned, past, while drawing (grey pulsing dot, nothing inserted)
╭──────────────────────────────────────────────────────────────────────────╮
│ ◌  10/31/2026  8:00 AM   5 h 37 ago   │  ‹   ›  [Go live]  │  ▭          │
╰──────────────────────────────────────────────────────────────────────────╯

Pinned with navigational warnings loaded (warning tone)
╭──────────────────────────────────────────────────────────────────────────╮
│ ▲ NOT REAL TIME  10/31  3:00 PM  in 1 h│  ‹   ›  [Go live]  │  ▭         │
╰──────────────────────────────────────────────────────────────────────────╯
```

For the not-real-time row, the time slot shortens to the date-less form so
the total width is unchanged. Whether that's acceptable is open question Q2.

The message chip stays above the bar, centred, growing upward:

```
              ╭──────────────────────────────────────────╮
              │ No data for 2 of 5 layers    Next data › │
              ╰──────────────────────────────────────────╯
╭──────────────────────────────────────────────────────────────────────────╮
│ ●  10/31/2026  3:00 PM   in 1 h       │  ‹   ›  [Go live]  │  ▭          │
╰──────────────────────────────────────────────────────────────────────────╯
```

The map stamp (dock open on the Timeline tab only), top left:

```
╭──────────────────────────────╮
│ ●  10/31/2026 2:00 PM · now  │      Live
╰──────────────────────────────╯
╭──────────────────────────────────╮
│ ●  10/31/2026 3:00 PM · in 1 h   │  Pinned
╰──────────────────────────────────╯
```

## 5. Out of scope

- A time picker in the HUD (F6 stays: clicking the time focuses the bar).
- Backdrop blur behind the surfaces (#733).
- Map- or data-area local time. Times are always the user's Local/UTC
  preference in the machine zone.
- Putting charts and notices on the clock; Live stays the default.

## 6. Suggested slices

1. **One overlay at a time** — D1, D2 (HUD side: the warning status slot),
   D3. `MainViewModel` visibility rules and `StampText`. Tests in
   `MultiDockActivityTabTests` for every row of §1.2, especially Logs and
   Clearance tabs and `IsNotRealTime` with the dock closed.
2. **Fixed HUD geometry** — D4–D8, D10. Rewrite the bar as a fixed-column
   `Grid` with the Live toggle; move `drawing…` to the dot and the chip. A
   `TimeHudViewTests` case asserts that the screen bounds of
   `TimeHud.Previous`, `TimeHud.Next` and the Live toggle are **identical**
   across Live → step → drawing → step (`in 1 h` → `in 11 h 30`) → Go live,
   and across 12- and 24-hour cultures within one format.
3. **Dock readout** — D13, with the same bounds test on
   `Timeline.PreviousStep` / `Timeline.NextStep` / `Timeline.GoLive`.
4. **Toggle, theme and accessibility polish** — D9, D11, D12: dynamic dock
   tooltip and hiding, theme tokens for the pill and chip tints, automation
   names, step-aware tooltips. Update `docs/` viewer pages and
   `whats-new.md` for the visible changes.

## 7. Settled questions

Settled 2026-10-31: the owner took the proposals.

- **Q1. The word for Pinned.** It leaves the map surfaces (D5); the HUD and
  stamp carry the mode through the Live / Go live toggle and the offset. The
  word *Pinned* stays where it already is outside the map: the dock's
  accessible names, docs, and MCP's `mode: "pinned"`. No rename.
- **Q2. Not real time in the HUD.** Built (slice 2) as the second,
  fixed-height line: *Display Not Real Time* in full, in the warning tone,
  above the controls row, with the status dot in the warning colour. The HUD
  is anchored at the bottom, so the line grows the bar upward and never moves
  ‹ ›. This replaces the `NOT REAL TIME` slot with the date-less time from the
  mock-up: that version changed the time format on the first step, and the
  full phrase is the S-98 §12.7.2 wording.
- **Q3. Clicking Live while live.** A no-op, so repeated clicks are safe.
- **Q4. Stamp with the Timeline tab shown.** Kept: the dock's readout is far
  from the map, and captures of the `Map` region need the time.
- **Q5. Offset slot width.** Fixed 84 px; longer gaps ellipsize with the full
  text in the tooltip (as in the D4 table). No per-culture measuring.
