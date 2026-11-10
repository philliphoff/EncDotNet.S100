# Viewer view model dependencies — Design Note

> Status: **Proposed** (#1392). This note looks at why SoundCharts' view
> models still take many services after #1203, tests the idea that they
> run workflows that belong in command handlers, and proposes slices.
> Open questions for review are in §6.

## 0. Problem

#1203 made the viewer's dependencies required and split `MainViewModel`
and `McpServerHost`. Several view models are still large:

| View model | Args | Lines | Commands |
|---|---|---|---|
| `LibraryPanelViewModel` (+ `.Weather.cs`) | 12 (+4 set as properties) | 2,476 | 39 |
| `DepthOverTimeViewModel` | 9 | 505 | 0 |
| `RoutesPanelViewModel` | 8 | 1,359 | 27 |
| `NavigationalWarningsViewModel` | 8 (+3 set as properties) | 1,344 | 9 |
| `LibraryItemViewModel` | 8 | 865 | 1 |
| `CatalogueDirectoryDialogViewModel` | 7 | 1,130 | 9 |
| `SettingsViewModel` | 5 | 1,847 | 7 |
| `PickReportViewModel` | 5 (5 constructors) | 1,433 | 7 |

The question in #1392 was whether these view models do too much of the
command work themselves, and whether that work should move into
per-use-case command handlers ("sagas").

## 1. Findings

**The workflow idea explains part of it, not most.** Each large view
model has about one real workflow, meaning a multi-step operation over
several services with async, error or cancellation handling. The
Library panel is the exception, with four. Most commands are
presentation: a toggle, a filter, a selection, or one call to a domain
service. Those don't need a handler.

The argument counts come from five causes. In rough order of how many
arguments each accounts for:

1. **Nullable arguments that are never null.** There are 86 nullable
   service-typed constructor parameters across 35 view models. Every one
   sampled is registered in `App`, so it is never null at runtime. The
   null branches (hidden rows, `?? TimeFormat.Local`, `?? UtcNow`) exist
   only for tests, which goes against the rule #1203 set. They don't add
   to the count, but they hide which dependencies are real, and they keep
   test-only code paths in production. `PickReportViewModel` has four
   extra constructors and a `SetPick` method that only tests use.
2. **Cross-cutting readers.** The most common are `TimeProvider` (12
   constructors), `ITimeFormatProvider` (12, plus 4 `Func<TimeFormat>`),
   `IMarinerSettingsProvider` (5, plus 3 `Func<DepthUnit>`),
   `IThemeService` (4) and `Action<Action>` dispatch (6). View time is
   spelt two ways: `GlobalTimeService` (concrete) and `IViewTime`.
   Grouping them saves 1–3 arguments in about ten view models, or 15–20%
   overall.
3. **Several panels in one class.** `LibraryPanelViewModel` holds the
   tree, the list, the bulk download bar, tap hits, details and the
   weather-run jobs. Those groups barely touch each other, and each brings
   its own services. The Settings pages are already separate views
   (`Views/Settings/*.axaml`), but all bind one `SettingsViewModel`.
4. **View models used as services.** Other code takes these view models
   to reach state or logic that has no service of its own:
   - `SettingsViewModel` is registered as `IRouteWeatherLimits` and
     `IWaterLevelAdjustmentSettings` (`App.axaml.cs:1572-1575`). `App`
     drives the MCP host, theme, own ship and eXaminer from its events,
     and `RouteWeatherSettingsTools` sets its properties.
   - `NavigationalWarningsViewModel` makes itself the `WarningNavigator`
     of `PickReportViewModel` and of `RouteFindingsService`, a service.
     It subscribes to `PickReportViewModel` changes. The drawing service's
     factory sets its `Drawing` property (`App.axaml.cs:1793`), a hidden
     cycle.
   - `ViewerLibraryEditor` (MCP `library_action`), `ViewerLibraryController`
     and `LibraryTimeSource` take the Library panel. They call
     `LoadRowsAsync`, `DownloadRowsAsync`, `CreateItem` and
     `SourceDisplayName` on it.
5. **Workflows in view models.** These are the real cases:
   - **Library download and open** (`LibraryPanelViewModel.DownloadRowsAsync`
     :1507). The steps are: close the copies a download replaces,
     download, re-index package sources, announce unpacked packages, open
     with source labels, and reopen. It is called by six panel commands,
     `LibraryTimeSource` and MCP `library_action`. It overlaps
     `LibraryOperations.DownloadAsync` in Collections, which the headless
     MCP host already uses.
   - **Library weather runs** (`LibraryPanelViewModel.Weather.cs`). These
     are load latest, pin, the storm advisory check, and final-storm
     markers, which write files from the view model. They run on the
     view model's minute timer, so tests build a whole panel to test a
     background job. They overlap `LibraryOperations.LoadLatestRunAsync`.
   - **Open a navigational warning** (`NavigationalWarningsViewModel`
     :605-715). The steps are: relax the filters so the row shows, take
     the processors, open the feature in Object Information, frame the
     map, and mark it seen. It is called by the view, MCP
     `select_navigational_warning`, the pick report and route findings.
   - **Add a catalogue by URL** (`CatalogueDirectoryDialogViewModel`
     :480). The steps are: validate, dedupe, probe, classify and persist.
     MCP `add_library_source` repeats the probe but doesn't persist. The
     dialog's SECOM registry listing is also repeated in MCP
     `list_secom_services`, and the two already differ: only the dialog
     maps probe exceptions to Unreachable.
   - **Reset or clear application data** (`SettingsViewModel` :1442).
     The steps are: confirm, remove identities, reset or clear caches,
     and restart.

   Some places already follow the handler idea: `LibraryRouteOpener`,
   `LibraryImportCoordinator`, `ViewerLibraryEditor` (with
   `LibraryEditOutcome<T>`), `PickService` and `LibraryOperations`. Some
   of what looks like a missing handler is a duplicate of one that exists
   below the viewer.

**The route edits are duplicated rather than orchestrated.** Insert
waypoint, delete waypoint and edit leg build their undoable edits in both
`RoutesPanelViewModel` (:661, :678, :694) and `McpTools/RouteTools.cs`
(:385, :488, :640). The midpoint and extend insert rule exists only in
the view model. That calls for shared edit commands, not a saga.

**Item and chart view models build themselves from services.**
`LibraryItemViewModel` reads load and download state through injected
services and callbacks. `DepthOverTimeViewModel` and the S-104/S-111
station charts take six data values plus view time, time format and
theme. `PickService` constructs them, so a service builds view models.

## 2. Per view model

| View model | What drives its count | Workflows | Proposed shape | Args after |
|---|---|---|---|---|
| `LibraryPanelViewModel` | several panels; 6 cross-cutting; workflows | download and open, open as route, weather runs | shell over `LibraryTreeViewModel`, `LibraryListViewModel`, `LibraryDetailsViewModel` (+ tap hits); handlers below | 3–4 (children ~5 each) |
| `LibraryItemViewModel` | lazy state reads, a Retry callback, formatting | none | plain state: `LibraryItemState` plus a shared row context; parent supplies Retry/Cancel | 2–3 |
| `RoutesPanelViewModel` | child pass-through (check, findings); formatting; exchange | import, copy to edit (mostly dialogs) | inject `PassageCheckViewModel` itself; `RouteExchangeFooterViewModel`; shared `RouteEditCommands` | 4–5 |
| `NavigationalWarningsViewModel` | acts as navigator; VM-to-VM coupling; 3 property dependencies | open a warning | `OpenNavigationalWarningWorkflow`; `ObjectInformationSelection` service; `RouteScopeViewModel` child; constructor-only dependencies | 4–5 (from 11 real) |
| `CatalogueDirectoryDialogViewModel` | `Func<>` delegates standing in for services | add by URL, registry listing | `AddCatalogueByUrlWorkflow`, `SecomServiceDirectory`; entry/category VMs to own files | 5 |
| `SettingsViewModel` | acts as the settings service; ~90 bound properties | reset, clear caches | `ViewerSettingsService` owns load/validate/persist/events; one page VM per Settings page; `ResetApplicationDataWorkflow` | 1–3 per page |
| `PickReportViewModel` | formatting context; test-only constructors | none (already `PickService`) | one constructor; `ExaminerLinks`; warning cards child; `PickAttributeFormatter` | 4 |
| `DepthOverTimeViewModel` (+ S-104/S-111 charts) | data passed as arguments; ambient trio | none | `(card data, StationChartContext)`; `PickService` returns data, the pick report builds the card | 2 |

## 3. Proposal

### 3.1 Workflows

A workflow is one use case that several hosts call. It's not a home for
every command.

- **Name and place.** Call it `<Verb><Noun>Workflow`, for example
  `DownloadLibraryItemsWorkflow`, next to its feature (`Library/`,
  `Services/PassageCheck/`, ...). The existing `LibraryRouteOpener` and
  `LibraryImportCoordinator` keep their names. Renaming them isn't worth
  the churn.
- **Shape.** A sealed class whose constructor takes domain services only,
  six or fewer. It has one entry point, `RunAsync(request, CancellationToken)`,
  returning an outcome with the value or an error. Generalise
  `LibraryEditOutcome<T>` so MCP maps errors as it does today.
- **Below the viewer first.** Where Collections already has the
  operation (`LibraryOperations`), the workflow wraps it and adds only
  what the app needs, such as closing and reopening copies, source labels
  and toasts. It doesn't repeat it.
- **Errors and toasts.** Expected failures go in the outcome. User-facing
  toasts stay in the workflow, as `LibraryLoadService` and
  `LibraryRouteOpener` do, so an MCP-driven action shows the same toast.
- **UI effects.** Effects such as "show the Routes panel" or "expand the
  unpacked groups" are an injected `Action` or an event the view model
  handles.
- **Threading.** Workflows that touch bound state run on the UI thread.
  View models call them from an `AsyncRelayCommand`. MCP callers marshal
  as they do now (`UiThread.InvokeAsync`, `IRouteEditInvoker`).
- **Callers.** MCP tools and controllers take the workflow, never the
  view model.
- **Tests.** Build the workflow over fakes of its domain services and
  assert on the outcome, as `LibraryShowOnMapServiceTests` does. View
  model tests then cover state and commands only.

Background jobs aren't workflows. The weather-run refresh and storm check
become a `WeatherRunMonitor` service with its own clock, which uses the
weather workflows.

### 3.2 Services out of view models

- **`ViewerSettingsService`** owns load, validation, persistence and typed
  change events. It provides `IRouteWeatherLimits` and
  `IWaterLevelAdjustmentSettings`. `App`'s wiring and
  `RouteWeatherSettingsTools` use it, not the view model.
- **`ObjectInformationSelection`** owns the selected hit. The pick report,
  the warnings panel and the drawing service subscribe to it. This
  replaces injecting `PickReportViewModel` into the warnings panel and the
  `WarningNavigator` properties.
- **`LibrarySourceNames`** replaces `LibraryPanelViewModel.SourceDisplayName`,
  so `App`, the MCP adapters and `LibraryTimeSource` stop resolving the
  panel.
- MCP adapters read row state from `LibraryItemState` (as
  `HeadlessLibrary` does), not `LibraryItemViewModel`.

Panel-local state that MCP drives on purpose stays on the view model: the
warnings scope and filters, the Clearance axis, the shell dock. That is
the "what the user sees" contract.

### 3.3 Cross-cutting readers

- Use `IViewTime` everywhere, and retire `GlobalTimeService` as a view
  model dependency.
- Add one **`DisplayFormatting`** reader holding the time format, depth
  unit and their change events, so `ITimeFormatProvider` plus
  `IMarinerSettingsProvider` becomes one dependency.
- Add **`StationChartContext`** (view time, formatting, theme) for the
  S-104, S-111 and depth-over-time charts.
- Add a **`UiDispatcher`** (Post, RunOrPost, Coalesce) in place of raw
  `Action<Action>` delegates.
- Formatting stays in view models. MCP reads the strings the user sees,
  so moving it into converters would split that contract.

These save little on their own. Use them while splitting a view model,
not as a separate sweep.

### 3.4 Nullable arguments

Make service arguments non-nullable unless the feature really can be
absent at runtime, and delete the null branches. Where a feature can be
absent, build it as a child view model that exists only when its service
does. The callback and variant parameters that are null in production
(entry remove and fix actions, header rows, group commands) stay
nullable. Tests that relied on nulls get small builders, as #1203's
`TestMapTools` and `TestMcpServerHost` did.

## 4. Slices

Each slice keeps the UI and the MCP tool list identical (same check as
#1382: `tools/list` schema hashes). The order puts the ones with outside
callers first:

1. **Library download and open workflows.**
   - Add `DownloadLibraryItemsWorkflow` and `OpenLibraryItemsWorkflow`
     over `LibraryOperations`, plus `LibrarySourceNames`.
   - `ViewerLibraryEditor`, `ViewerLibraryController` and
     `LibraryTimeSource` stop taking the panel.
2. **Weather runs out of the Library panel.** Add `WeatherRunMonitor` plus
   the load-latest, pin and storm-marker workflows. The weather tests stop
   building a panel.
3. **Library panel split.** Split it into tree, list, details and tap-hit
   child view models, and make `LibraryItemViewModel` plain state.
4. **Open a navigational warning.**
   - Add `OpenNavigationalWarningWorkflow` and
     `ObjectInformationSelection`.
   - Pass the warnings panel's dependencies through its constructor only.
   - Split out `RouteScopeViewModel`.
5. **Settings service and pages.** Add `ViewerSettingsService`, one page
   view model per Settings page, and `ResetApplicationDataWorkflow`.
6. **Catalogue directory.** Add `AddCatalogueByUrlWorkflow` and
   `SecomServiceDirectory`, shared with MCP `add_library_source` and
   `list_secom_services` (this fixes the Unreachable drift). Remove the
   `Func<>` arguments.
7. **Routes panel.**
   - Add `RouteEditCommands`, shared with `McpTools/RouteTools.cs`.
   - Inject `PassageCheckViewModel` directly.
   - Split out `RouteExchangeFooterViewModel`.
8. **Pick report and station charts.** Leave one constructor, and add
   `ExaminerLinks`, `PickAttributeFormatter` and `StationChartContext`.
   `PickService` returns data, not view models.

Slice 3 depends on 1 and 2. The others are independent of each other.
Section 3.3's readers and 3.4's nullable cleanup ride along in whichever
slice touches the view model.

## 5. Not in scope

- Changing the MVVM toolkit or the DI container.
- A mediator or message bus. Workflows are called directly; the problem
  here is where logic lives, not how it's dispatched.
- Behaviour changes.

## 6. Open questions

- **Q1 Naming.** `…Workflow`, or `…Handler`, or "saga"? This note uses
  `Workflow` because the classes are called directly and hold no state
  between calls, which "saga" usually implies.
- **Q2 Toasts.** Should toasts stay in workflows, so MCP-driven actions
  show them as they do today? The alternative is to return them in the
  outcome for the view model to show.
- **Q3 Settings shell.** Should the shell take its six pages as
  `IEnumerable<ISettingsPage>`, or as named arguments? Named arguments
  make seven, over the six-argument guideline.
- **Q4 Order.** Should slices 1–3 (Library) go first as listed, or should
  slice 4 (warnings) go first because it removes a dependency cycle?
