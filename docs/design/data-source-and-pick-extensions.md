# Data source and pick extensions — Design Note

> Status: **Design / Research**. No production code yet. This note proposes
> two extension points that fit together but can ship separately: a single
> way to add data that doesn't come from a file (§2), and a way for any
> product or source to add its own content to the pick report (§3). §4 shows
> how they combine, §5 proposes a work breakdown and §6 lists open
> questions.
>
> Builds on [Dynamic feature sources](dynamic-feature-source.md),
> [Dynamic source pick](dynamic-source-pick.md),
> [Wind and wave data](weather-sources.md) (§8, product traits and
> code-defined portrayal) and the
> [Rendering-backend contract](rendering-backend-contract.md).

## 1. Scope

EncDotNet.S100 started as an S-100 library, and its extension points
reflect that. A new S-100 product is a `ProductRegistration`, a processor and
a portrayal. The weather work made products that aren't S-100 (GRIB2,
NetCDF, CAP) first-class on the same path, as long as they arrive as files.

Two kinds of extension are still harder than they need to be:

1. **Data that isn't a file.** AIS, own-ship, NDBC buoys and NMEA weather
   arrive as feeds or are computed. Each one is wired into SoundCharts by
   hand, and each gets a subset of what a file dataset gets.
2. **Pick report content that isn't attribute rows.** The S-411 egg code,
   S-104/S-111 station time series and S-124 warning cards are each a typed
   property on a core type, with a matching special case in every layer
   between the processor and the XAML.

Customer work makes both more pressing. An ice service that isn't S-411, or a
customer's own feed, should be able to draw on the map and show an egg code
in the pick report without changes to the core packages.

Out of scope:

- Loading plug-in assemblies at runtime (§2.5).
- New feed protocols, new symbology or new pick report designs. This note is
  about the seams, not what plugs into them.
- Time-scrubbable replay of live feeds, beyond noting where it would go
  (§6).

## 2. Data sources

### 2.1 What exists today

There are two ways to get data onto the map.

| | File datasets | Live sources |
|---|---|---|
| Contract | `IDatasetProcessor` plus optional capabilities (`ITimeAwareDatasetProcessor`, `ICoverageSampler`, `IVectorPortrayalSource`, `ICoveragePortrayalSource`) | `IDynamicFeatureSource` (`CurrentFeatures`, `Changed`) |
| Created by | `ProductRegistration.CreateFromPath` / `CreateFromSource`, after byte- or envelope-based detection | Hand-written DI wiring per source in `App.axaml.cs` (`AddAisOverlay`, `AddNdbcObservations`, `AddNmeaWeather`, own-ship singletons) |
| Drawn by | Part 9 drawing instructions (Lua, XSLT or the code-defined toolkit), lowered to the renderer-neutral `VectorScene` | `IDynamicFeatureRenderer`, which returns Mapsui `IFeature`. NDBC and NMEA weather go through `IDynamicFeaturePortrayal` (Part 9 instructions) and `PortrayedDynamicFeatureRenderer`; AIS and own-ship use the Mapsui-only `AisVesselRenderer` and `OwnShipRenderer`. |
| Layer order | S-98 planes, with dataset layers ordered among themselves | The overlay band, above every dataset (`MapsuiLayerBands.AddOverlayLayer`). `DynamicSourceMetadata.Plane` is shown in the layer stack but doesn't order the layer against datasets. |
| Pick | `HitTestFeatures`, `GetFeatureInfo` → `FeatureInfo` with typed `PickAttribute` rows | `DynamicSourcePickService`, 12 px point distance, `IDynamicFeatureDescriber` → `(Label, Value)` strings |
| Time | `ITimeAwareDatasetProcessor` | Always "now" |
| Headless, Skia, MCP | Yes | No |
| Settings | Library entry, display settings | One settings class per source (`AisOverlaySettings`, …) |

At the library level, a dataset doesn't need to be a file:
`IS100MapSession.AddDatasetAsync(MapDataset, IDatasetProcessor, …)` accepts
any processor. Above that level, it does. SoundCharts' `DatasetEntry`
requires a `FilePath`, `DatasetLoaderService.LoadAsync` takes a
`DatasetEntry`, and `ProductRegistration` can only build a processor from a
path or an `IAssetSource`.

### 2.2 Problems

1. **Polled feeds don't fit either model.** NDBC refreshes every
   10 minutes. That's a dataset that reloads, not 1 Hz push data, but the only
   route into SoundCharts that doesn't involve a file is the live model. So NDBC
   observations can't be sampled (`ICoverageSampler`), aren't time-aware, and
   aren't visible to MCP. A refreshing CAP or ERDDAP feed would have the same
   problem.
2. **Two drawing paths for live data.** `IDynamicFeaturePortrayal` is
   renderer-neutral, but `IDynamicFeatureRenderer` is still a public contract
   that returns Mapsui types, and two of the four live sources use it.
3. **Every source is wired by hand.** Each one repeats the same steps: build
   an `HttpClient` or transport, read a settings class, wrap the source in a
   viewport-gated overlay (`DeferredAisFeatureSource`,
   `NdbcObservationsOverlay`), register it as `IDynamicFeatureSource`, and
   register a keyed renderer. Nothing about a source is described in one
   place that the UI could read.
4. **Live data is second-class everywhere but the Mapsui map.** It doesn't
   appear in headless renders, the Skia renderer, MCP `identify_features` or
   `s100 info`, and it always draws above every chart layer.

### 2.3 Constraints

- **Trimming.** The libraries are marked `IsTrimmable`, SoundCharts and
  `s100` run the trim analyzers on every build, and the DI helpers carry
  `DynamicallyAccessedMembers`. Plug-ins must be registered in code.
- **The processor is the unit everything else understands.** Picks, MCP, the
  headless compositor, S-98 ordering and the layer stack are all built on
  `IDatasetProcessor`. A new parallel contract would need all of them
  duplicated.
- **1 Hz updates must stay cheap.** AIS can update hundreds of targets a
  second. Re-running a whole dataset portrayal on each update isn't
  acceptable, so incremental updates have to survive.
- **No breaking change to `IDynamicFeatureSource`.** It has external
  implementations, and `DynamicFeatureTracker` already does the bookkeeping
  a push source needs. It stays as the way to write one.

### 2.4 Decisions

#### D1. A processor can announce that it changed

Add an optional interface to `EncDotNet.S100.Core`, next to the other
optional processor interfaces:

```csharp
public interface ILiveDatasetProcessor
{
    event EventHandler<DatasetChangedEventArgs>? Changed;
}

public sealed record DatasetChangedEventArgs
{
    public static DatasetChangedEventArgs All { get; } = new();
    public IReadOnlyList<string>? FeatureRefs { get; init; }  // null: everything may have changed
    public BoundingBox? Extent { get; init; }                 // null: anywhere
}
```

The map session treats `Changed` like a viewport or time-step change: it
re-requests the portrayal of the affected dataset, coalescing events that
arrive while a render is in flight. Picks and MCP already re-query the
processor, so they see new data with no further change.

This alone is enough for polled feeds: an NDBC, CAP or ERDDAP processor
refreshes its model and raises `Changed`.

#### D2. Every live source is also a processor

A `DynamicSourceProcessor` in `EncDotNet.S100.Datasets.Pipelines` wraps any
`IDynamicFeatureSource` with an `IDynamicFeaturePortrayal` and implements:

| Interface | From |
|---|---|
| `IDatasetProcessor.Spec` | The source's registration (§D4): a format name `SpecName` accepts, `IsS100 = false` |
| `HitTestFeatures`, `EnumerateFeatures`, `GetFeatureInfo` | `CurrentFeatures`, and `IDynamicFeatureDescriber` when the source implements it (see §3) |
| `IVectorPortrayalSource` | `IDynamicFeaturePortrayal.Portray` for each feature |
| `ILiveDatasetProcessor` | `IDynamicFeatureSource.Changed`, with `ChangedIds` as `FeatureRefs` |

Once wrapped, a live source gets S-98 plane ordering, headless and Skia
rendering, MCP and the pick pipeline from the processor side, without its
own code changing.

The 1 Hz constraint is met by keeping the current Mapsui overlay path as an
**optimization for interactive maps**, not as the contract. When a source has
an `IDynamicFeaturePortrayal`, the session may draw it through
`PortrayedDynamicFeatureRenderer`, which applies incremental updates to a
`MemoryLayer`, and place that layer in its S-98 plane rather than the overlay
band. Headless renderers and MCP use the processor.

`IDynamicFeatureRenderer` becomes obsolete. AIS and own-ship move to
`IDynamicFeaturePortrayal`. Own-ship's true-scale hull and AIS's
class-coloured triangles are both drawable with the code-defined toolkit.

#### D3. A dataset entry doesn't need a file

SoundCharts' dataset model gains an origin. A `DatasetEntry` is either:

- **a file**, as today (`FilePath`, optional `Source` and `RelativePath`); or
- **a source**, identified by a source registration id (§D4) and its saved
  options.

`DatasetLoaderService` gets a path that builds the processor from the
registration and calls `IS100MapSession.AddDatasetAsync`, which already
exists. The Datasets panel, layer stack, visibility and removal then work for
both kinds. Details that only make sense for files, such as the file name
and size, are hidden for sources.

#### D4. One registration type for sources that aren't files

File products are found by detection. Sources are configured instead: with a
URL, an API key, a serial port or an area. Give them a sibling of
`ProductRegistration`:

```csharp
public sealed record SourceRegistration
{
    public required string Id { get; init; }                 // "ais.aisstream", "ndbc.realtime"
    public required string DisplayName { get; init; }
    public required string Spec { get; init; }               // a format name, e.g. "AIS" (SpecName gains these)
    public bool IsS100 { get; init; }                        // false for every source today
    public S98DisplayPlane DefaultPlane { get; init; }
    public Type? Options { get; init; }                      // options record, drives a generic settings page
    public SourceActivation Activation { get; init; }        // Always, or BelowViewportSpan(degrees)
    public required Func<SourceCreationContext, IDatasetProcessor> Create { get; init; }
    public IReadOnlyList<IPickContributor> PickContributors { get; init; } = [];  // §3
}
```

`SourceCreationContext` carries the options, an `ILoggerFactory`, a
`TimeProvider`, an `HttpClient` factory with the hardened TLS handler, and
the key lookup (environment variable first, then the saved key) that AIS
does by hand today.

Registrations go into a `SourceRegistry` alongside `ProductRegistry`, with a
DI helper:

```csharp
services.AddDataSource(AisSources.AisStream);
```

SoundCharts builds from the registry:

- an **Add source** list, which could share the "Add online source" wizard's
  category and search UI (`online-catalogue-dialog.md`);
- a settings page per source from its `Options` type, replacing per-source
  settings classes;
- viewport gating from `Activation`, replacing `DeferredAisFeatureSource` and
  the gating half of `NdbcObservationsOverlay` with one shared wrapper.

Behaviour that belongs to SoundCharts and not to the data stays there:
pirate mode, own-ship helm and "take helm" are add-ons layered over a source,
not part of `SourceRegistration`. The "take helm" action becomes a pick
action (§P4).

#### D5. Plug-ins are packages registered in code

A plug-in is a NuGet package that exposes registrations, and a host adds
them in its start-up code. `s100` and the MCP server gain the same
`AddDataSource` call. This keeps everything visible to the trim analyzers.

### 2.5 Alternatives rejected

- **Make every source a file product with a fake path.** It would work
  through `CreateFromSource` with a synthetic `IAssetSource`, but detection,
  Library probing and file metadata would all have to special-case it, and
  there's still nothing to say "refresh".
- **Keep live sources separate and add MCP, headless and S-98 support to
  them one at a time.** Each feature would be built twice. The adapter in D2
  gets all of them at once.
- **Load plug-in assemblies at runtime.** It defeats the trim analysis, and
  it raises code-trust questions SoundCharts doesn't have answers for. It can
  be revisited if a customer needs to drop a plug-in into an installed app.
- **Retire `IDynamicFeatureSource` in favour of `ILiveDatasetProcessor`.**
  A processor is a lot to implement for a feed of points.
  `IDynamicFeatureSource` with `DynamicFeatureTracker` stays the way to write
  a push source, and D2 turns it into a processor.

## 3. Pick contributions

### 3.1 How a pick works today

A pick has three stages:

| Stage | File datasets | Live sources |
|---|---|---|
| **Hit test**: what's under the cursor | `IDatasetProcessor.HitTestFeatures`, then `PickService` orders hits by S-98 15.1 (`PickOrder`: drawing priority, then geometry) | `DynamicSourcePickService`, separately |
| **Describe**: what to say about each hit | `FeatureInfo`: typed `PickAttribute` rows, plus the typed properties `StationSeries`, `EggCode` and `NavigationalWarning`, plus row flags `IsNavigationNotice` and `IsStaleNotice` | `IDynamicFeatureDescriber` → `(Label, Value)` strings, or default rows built from `Motion` and `Attributes` |
| **Present**: how to show it | `PickService` copies each property into `PickHit`; `PickReportView.axaml` has a template for each | `DynamicPickHit`, a sibling type; "Take helm" shows when `TryGetAisMmsi` finds an MMSI |

The special cases are added at three different points:

- **On the processor.** `GmlDatasetProcessorBase` has a virtual
  `BuildEggCode`, which `S411DatasetProcessor` overrides for the feature
  classes that carry SIGRID-3 attributes.
- **On the core type.** `FeatureInfo` has one property per special case.
- **In SoundCharts.** `PickHit`, `EggCodeViewModel`, the station series view
  model, `NavigationalWarningCardViewModel` and a template for each.

### 3.2 Problems

1. **Each new panel touches every layer.** The egg code needed a core
   property, a base-class hook, a `PickHit` property, a view model and a
   template. A customer-specific panel would need the same, in packages the
   customer doesn't own.
2. **Contributions are tied to a product, not to the data.**
   `IceEggCodeBuilder.Build` takes plain SIGRID-3 strings (`iceact`,
   `iceapc`, `icesod`, `iceflz`), so nothing about it is specific to S-411.
   A SIGRID-3 shapefile, a customer's ice service or a NetCDF ice product
   could produce the same egg, but the only hook is on the GML base class.
   Station series has the same shape: S-104 and S-111 today, NDBC buoys
   tomorrow.
3. **MCP and headless output see only rows.** `identify_features` and
   `describe_feature` return attributes, so the egg code, station series and
   warning card never reach an agent.
4. **Live picks are a parallel type.** `DynamicPickHit` has string rows only,
   and its one action is hard-wired to AIS.

### 3.3 Decisions

#### P1. Pick sections replace typed properties

`FeatureInfo` gains a list of sections. A section is plain data with a
stable kind and a fallback to rows:

```csharp
public abstract record PickSection
{
    public abstract string Kind { get; }                       // "ice.egg-code", "station-series", "nav-warning"
    public abstract IReadOnlyList<PickAttribute> ToRows();     // shown by a host that doesn't know Kind
}

public sealed record IceEggCodeSection(IceEggCode Egg) : PickSection
{
    public override string Kind => "ice.egg-code";
    public override IReadOnlyList<PickAttribute> ToRows() => ...;
}
```

`FeatureInfo.Sections` is `IReadOnlyList<PickSection>`.

- The existing properties stay for one release as shims that read the first
  section of their kind, then go away.
- The navigation and stale notices become sections too, so `PickAttribute`
  goes back to describing attributes.
- Kinds are dotted strings. Built-in kinds have no prefix; a customer's kinds
  use their own prefix (`acme.ice-chart`) so they never collide.

#### P2. Contributors produce sections

```csharp
public interface IPickContributor
{
    PickSection? Contribute(PickContext context);
}

public sealed record PickContext
{
    public required IDatasetProcessor Processor { get; init; }
    public required SpecRef Spec { get; init; }
    public required string FeatureRef { get; init; }
    public required string FeatureType { get; init; }
    public required IReadOnlyList<PickAttribute> Attributes { get; init; }
    public IFormatProvider? FormatProvider { get; init; }
}
```

A contributor is registered in one of three places:

| Where | Applies to | Example |
|---|---|---|
| `ProductRegistration.PickContributors` | Every feature of that product | S-124 → warning card |
| `SourceRegistration.PickContributors` (§D4) | Every feature of that source | AIS → vessel summary |
| `PickContributorRegistry`, globally | Any feature the contributor accepts, from any product or source | SIGRID-3 attributes present → egg code |

The global registration is what makes contributions independent of where
the data comes from. The egg contributor checks for SIGRID-3 attributes and
calls `IceEggCodeBuilder`. It then works for S-411, a SIGRID-3 shapefile, or
a customer feed that carries the same codes, and `BuildEggCode` on
`GmlDatasetProcessorBase` goes away.

Contributors run when a feature is described, not when hits are found. Hit
testing and S-98 15.1 ordering don't change. When two contributors produce
the same kind for one feature, the most specific wins: source or product
registration first, then global, then registration order.

SoundCharts' `PickService` and MCP's `IdentifyFeaturesService` each build
`FeatureInfo` their own way today. Both call one shared step after
`GetFeatureInfo` that runs the contributors, so a contribution shows up in
both. A processor can still put sections in `FeatureInfo` itself when only
it has the data, for example a station series read from the HDF5 file.

#### P3. Each host presents sections by kind

- **SoundCharts.** A lookup from kind to a view model factory and a
  `DataTemplate`, registered in DI. `PickHit` holds sections instead of one
  property per kind. An unknown kind shows its `ToRows()`, so a plug-in that
  only adds data still shows something, and one that also ships a template
  shows its own panel.
- **MCP.** Sections serialize as typed JSON with their kind, in
  `identify_features` and `describe_feature`. Each built-in section has a
  JSON shape described in the tool schema. An unknown kind serializes its
  rows.
- **Headless and `s100 info`.** Rows from `ToRows()`.

#### P4. Pick actions are contributed too

```csharp
public sealed record PickAction(string Id, string Label, Func<PickContext, bool> AppliesTo);
```

A host registers actions and handles them by id. "Take helm" becomes an
action registered by the own-ship feature, which applies to features that
carry an MMSI, replacing `TryGetAisMmsi` in `PickReportViewModel`. "Open
warning" and "Show time series" can follow.

#### P5. Live hits join the same pipeline

Once live sources are processors (§D2), `DynamicPickHit` merges into
`PickHit` and the "sectioned single list" of
[Dynamic source pick](dynamic-source-pick.md) Q4 becomes one list.
`IDynamicFeatureDescriber` maps onto `GetFeatureInfo` in the adapter: its
title becomes the feature type name and its properties become rows. The
default rows built from `Motion` and `VesselGeometry` become a built-in
`vessel` section.

### 3.4 Alternatives rejected

- **Keep adding properties to `FeatureInfo`.** It works for the core team,
  but a customer can't add one without forking, and every host has to learn
  every property.
- **An open dictionary of extension data on `FeatureInfo`.** It's simpler,
  but loses typing, has no fallback for hosts that don't understand an
  entry, and gives MCP nothing to describe in a schema.
- **Contributors on the processor only.** That's the `BuildEggCode` model
  today. It ties every contribution to the code that reads the file, which is
  what stops the egg code being reused.

## 4. How the two fit together

Two worked examples show the extension points combining.

**A customer ice service.** The customer has an HTTP API that returns ice
polygons with SIGRID-3 attributes, updated every few hours.

1. Their package defines a `SourceRegistration` whose `Create` returns a
   processor that fetches polygons into memory, portrays them with the
   code-defined toolkit, and raises `Changed` after each refresh (§D1, §D4).
2. They add no pick contributor: the global egg contributor (§P2) sees the
   SIGRID-3 attributes and adds the egg code.
3. The source appears in **Add source**, gets a settings page from its
   options record, draws in its S-98 plane, shows egg codes in the pick
   report and in MCP, and renders headless. No core package changes.

**NDBC observations.** Today NDBC is a live source with its own overlay
wrapper and string pick rows.

1. Its `SourceRegistration` sets `Activation` to below a viewport span,
   replacing the gating in `NdbcObservationsOverlay` (§D4).
2. `DynamicSourceProcessor` makes it a processor (§D2), so it's in MCP and
   headless renders.
3. NDBC's realtime files already hold weeks of rows, which the parser reads
   and the source combines into the latest observation. If the source keeps
   those rows, a station series contributor can turn them into the same
   `station-series` section S-104 and S-111 use, and the pick report draws
   the same chart (§P2, §P3).

## 5. Work breakdown

Each slice ships on its own and leaves the tree working.

1. **Pick sections with the egg code.** Add `PickSection`,
   `IPickContributor`, the global registry and the SoundCharts lookup. Move
   the egg code to a global contributor and a section, with the
   `FeatureInfo.EggCode` shim. Serialize sections in MCP. This proves the
   pattern on the case with one consumer and a builder that works with any
   data source.
2. **Station series and warnings as sections.** Move `StationSeries` and
   `NavigationalWarning`, then the two notice flags. Remove the shims in the
   following release.
3. **`ILiveDatasetProcessor`.** Add the interface and the session's
   re-render on `Changed`.
4. **`DynamicSourceProcessor`.** Add the adapter. Move NDBC and NMEA weather
   onto it first, since they already use `IDynamicFeaturePortrayal`.
5. **AIS and own-ship portrayals.** Move both to `IDynamicFeaturePortrayal`
   and mark `IDynamicFeatureRenderer` obsolete.
6. **`SourceRegistration` and source dataset entries.** Add the registry and
   the `DatasetEntry` origin. Move AIS, NDBC, NMEA weather and own-ship
   registrations onto it, with the shared activation wrapper.
7. **Add source UI and generic settings pages.**
8. **Pick actions.** Move "Take helm" to a contributed action, and merge
   `DynamicPickHit` into `PickHit`.

Slices 1–2 and 3–7 are independent of each other; slice 8 needs both.

## 6. Open questions

- **Sections that follow the clock.** The station series chart follows the
  global time today, which is why `PickService` keeps the processor alongside
  the snapshot. Should a section be able to refresh itself (an
  `ITimeAwarePickSection` with a resample function), or should the host
  re-run the pick when the time changes? Re-running is simpler; resampling
  avoids a full re-describe on every clock tick.
- **Live sources and time.** Once live sources are processors,
  `ITimeAwareDatasetProcessor` could give them track history and replay.
  Is that wanted, and should the tracker keep history to support it?
- **Plane placement of the Mapsui fast path.** Moving AIS out of the overlay
  band into its S-98 plane changes what draws above it. Is "always on top"
  a feature users rely on for own-ship?
- **Kinds for customer sections.** Is a dotted prefix enough, or do we want
  a registry of kinds with their JSON schemas for MCP?
- **Options and secrets.** The generic settings page needs to know which
  options are secrets. AIS keeps its key in `settings.json` today, with an
  environment variable as the alternative. Marking secrets with an attribute
  on the options record would let a later change move them to the operating
  system's credential store without touching each source.
