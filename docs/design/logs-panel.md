# Logs panel design pass

> Status: **Proposed** (2026-10-30). Nothing here is built yet. Follows the
> Logs tab shipped for
> [#1080](https://github.com/philliphoff/EncDotNet.S100/issues/1080)
> (`LogsView.axaml`, `LogsViewModel.cs`, `LogsToolbarPanel.cs`).

The Logs tab works: it's fast under bursts, it filters, it collapses repeats
and it feeds the feedback report. But it reads like a form, not a log. This
note audits it against how log viewers usually look (IDE output panes,
browser consoles, Console.app) and proposes a set of numbered decisions an
implementation PR can follow.

## 1. What's wrong today

From the live panel (Timeline/Logs dock, a GRIB2 load error selected):

| # | Observation | Where |
|---|---|---|
| A1 | Only the time column is monospaced. Messages, levels and areas use the UI font, so numbers and durations (`in 697.9ms`, `in 1.5ms`) don't line up, and ids and paths are hard to scan. | row template |
| A2 | Row text renders **semibold**, the same weight as the details title. Nothing in `LogsView.axaml` asks for it; it comes from the list item theme. Every row shouts. | `ListBox.accentList` rows |
| A3 | "Info" and the area repeat in full ink on every row, so the eye lands on the two least useful columns. Info is the baseline and should be the quietest thing in the row. | level / area columns |
| A4 | Errors and warnings are only told apart by the colour of one short word. Scrolling 1,000 rows, an error is easy to miss. | `TextBlock.logsLevel` |
| A5 | The selected error isn't on screen: Auto-scroll followed new rows and carried the selection out of view, while the details pane still shows it. | `LogsView.ScrollToNewest` |
| A6 | Pause, Auto-scroll and Verbose are toggles whose "on" state is a faint grey fill. Auto-scroll is on in the screenshot and it's hard to tell. Copy and Clear have icons, the toggles don't. | toolbar |
| A7 | The level ComboBox and the search box are ~32 px tall; the tool buttons are 24 px. The toolbar has two baselines. | toolbar |
| A8 | Verbose (what the buffer **captures**) and the level filter (what the list **shows**) are two controls for one idea. The view model already couples them (`IsVerbose` flips the filter). | `LogsViewModel.IsVerbose` |
| A9 | The exception wraps at the pane width, so a stack frame breaks mid-identifier, and the source path is cut off. It also shows `/Users/<name>/…`, while Copy redacts the home directory to `~`. Screenshots get pasted into issues too. | details pane |
| A10 | The details pane is a fixed 380 px and can't be widened, though it's where the long text is. | details pane |
| A11 | Times are always local `HH:mm:ss.fff`. Every other time in the viewer follows the Local/UTC preference (`TimeFormat`), and there's no date anywhere, so a session that runs past midnight is ambiguous. | `LogRowViewModel.TimeText` |
| A12 | The monospace stack is spelled out as `Menlo,Consolas,Courier New,monospace` in 34 places across the viewer. | app-wide |

## 2. Decisions

### Typography

**D1. The row is monospaced.** Time, level, area and message all use one
monospace face at 11 px, regular weight, so columns, numbers and repeated
message shapes line up. Column headers, the toolbar and the details labels
stay in the UI font.

**D2. One shared mono resource.** Add `MonoFontFamily` (and
`MonoFontSize` 11) to `App.axaml` resources and replace the 34 literal
stacks. Prefer `SF Mono, Menlo` on macOS, `Cascadia Mono, Consolas` on
Windows, `DejaVu Sans Mono` on Linux, then `monospace`.

**D3. Rows are regular weight.** Pin `FontWeight="Normal"` on the row
template (and find which theme style makes them semibold, A2). Weight is kept
for the details title only.

### Hierarchy and levels

**D4. The message is the primary column.** Message text is full ink. Time and
area are muted (secondary foreground, ~60%). Info's level text is muted too:
Info is the baseline.

**D5. Levels get a gutter glyph, and problems get a row tint.** A 14 px
gutter at the row's left holds a glyph for Warning (⚠, `Warning`) and Error
or Critical (⊗, `ErrorCircle`) in the level colour. Error rows also get a
faint destructive tint (≈6%), warning rows a fainter warning tint (≈4%), so
problems are findable while scrolling. The exception icon that sits before
the message today moves into the gutter: an entry with an exception shows
the error glyph whatever its level. Debug rows keep their 65% opacity.

**D6. Short level labels.** The level column shows `Info`, `Warn`, `Error`,
`Debug`, `Trace`, `Crit` (localized short forms, new `LogLevelShort_*`
strings) at 44 px. The full name stays in the row's accessible name and the
details header.

### Toolbar

**D7. One level control, and it owns capture.** Drop the Verbose toggle. The
level filter becomes: *Errors*, *Warnings and errors*, *Info and above*
(default), *Everything, Debug included*. Choosing *Everything* turns on Debug
capture (what `IsVerbose = true` does now) and a muted "Capturing Debug"
hint shows next to it; choosing any other level turns capture back off. The
tooltip says Debug is only captured from that moment.

**D8. Problem counts as quick filters.** After the search box, two compact
chips show the counts in the buffer: `⊗ 1` and `⚠ 3`. Clicking a chip sets
the level filter to *Errors* or *Warnings and errors*; clicking it again
returns to the previous level. Chips are hidden at zero. This replaces
nothing; the tab badge keeps counting unseen problems.

**D9. Follow the tail instead of Pause and Auto-scroll.** Remove both
toggles. The list follows new rows while it's scrolled to the bottom. Any of
these stops following: scrolling up, selecting a row, or focusing the
search. While not following, new rows still append below (the viewport is
anchored, including when the buffer evicts from the front), and a floating
pill at the list's bottom edge says `↓ 12 new`; clicking it (or `End`)
scrolls to the newest row and resumes following. This fixes A5, because a
selection pins the view.

**D10. Equal heights and grouped actions.** Every toolbar control is 28 px.
Order: level · search · counts … `Copy all` · `Clear` | `Send feedback`.
Copy is renamed **Copy all** (it copies every shown entry) to tell it from
copying one entry (D13). Clear keeps its icon and gets a tooltip saying it
also empties the logs a feedback report would carry. `LogsToolbarPanel`'s
wrap rule is unchanged.

### Details pane

**D11. A structured header.** `⊗ Error · Datasets` in the level colour, then
the full timestamp with date (`2026-10-30 16:29:52.795`, in the user's
Local/UTC preference), then two icon buttons: **Copy entry** and close.

**D12. Readable exceptions.** The message is monospaced and wraps. The
exception is monospaced, **does not wrap**, and scrolls horizontally inside
its own box with a subtle code background. Its first line (type and message)
is full ink; the `at …` frames are muted. Home-directory paths are redacted
to `~` in the pane as in Copy (`HomePathRedactor`), so screenshots don't leak
the user name. Category moves under the exception as a muted key/value line.

**D13. The pane is resizable.** A `GridSplitter` on its left edge, default
380 px, min 280 px, max 60% of the dock. The width persists in settings.

### Time, keyboard and menus

**D14. Times follow the Local/UTC preference.** Rows show `HH:mm:ss.fff` in
the `TimeFormat` zone; the row tooltip and the details header carry the date.
When the shown entries span more than one day, the first row of each new day
gets a thin date divider.

**D15. Keyboard.** `Cmd/Ctrl+F` focuses search, `Esc` clears the selection
(and then the search), `Cmd/Ctrl+C` copies the selected entry, `End` resumes
following, `↑/↓` move the selection and keep the details in step.

**D16. Row context menu.** Copy message · Copy entry · Show only *Area* ·
Hide *Area*. The area choices add an `area:` token to the search box
(`area:Datasets`, `-area:Commands`), so the filter stays visible and
editable rather than becoming hidden state.

## 3. Out of scope

- An area multi-select dropdown. D16's search tokens cover it until someone
  asks.
- Structured log properties (scopes, event ids). `LogEntry` carries the
  formatted message only.
- Lowering `Viewer command completed` to Debug. The screenshot is dominated
  by `timeline.scrub` completions at Info; that's a logging-content question
  for #1080 item 4, not a panel one, but it's worth a look: with D4 they get
  quieter, not fewer.

## 4. Suggested slices

1. **Typography** — D1–D3, D4, D6, D12 (mono, weight, muted columns,
   exception box, redaction). Mostly XAML plus `MonoFontFamily`.
2. **Levels and toolbar** — D5, D7, D8, D10.
3. **Follow the tail** — D9, D15.
4. **Details and time** — D11, D13, D14, D16.

The mock for this round is the **Logs Panel · design pass** artboard on the
*Security & updates* page of the SoundCharts design canvas
(<https://claude.ai/artifact/2hWbgQ9wKM1gC2gM51mSCs>): today's panel, the
proposed panel with an error selected and following stopped, and parts for the
level menu (D7), level rows (D4–D6), the follow pill, day divider and row menu
(D9, D14, D16).
