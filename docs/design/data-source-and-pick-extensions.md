# Data source and pick extensions — Design Note

> Status: **Design / Research**. No production code yet. This note proposes
> two extension points that fit together but can ship separately: a single
> way to add data that doesn't come from a file (§2), and a way for any
> product or source to add its own content to the pick report (§3). §4 shows
> how they combine, §5 proposes a work breakdown and §6 lists open
> questions.
>
> The open questions of the first draft were settled on 2026-11-12. Their
> answers are in the sections they affect: layer order (D6), live sources
> and time (D2), secrets (D7), sections and time (P1) and section kinds
> (P6). A second round on 2026-11-13 settled the AML and CAP categories,
> fills under S-102 and a dataset's own category (all in D6).
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

Both extension points need a third thing: a place in the layer order that
doesn't depend on which product happened to come first. The note settles
that too (D6).

Customer work makes all of this more pressing. An ice service that isn't
S-411, or a customer's own feed, should be able to draw on the map and show
an egg code in the pick report without changes to the core packages.

Out of scope:

- Loading plug-in assemblies at runtime (§2.5).
- New feed protocols, new symbology or new pick report designs. This note is
  about the seams, not what plugs into them.
- Time-scrubbable replay of live feeds, beyond deciding where it would go
  (D2).

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
5. **Layer order is chosen case by case.** `S98DisplayPlane` is one fixed
   list that mixes S-98's two mechanisms (§D6). Plane names describe
   products (`DynamicArrows` exists for S-111), and each newcomer picks a
   slot and a priority by hand: weather fills in `OnDemandSurface` though
   they aren't official, isobars, fronts and observations in
   `OtherChartOverlays` at priorities 90, 92 and 100, and NATO AML in a new
   plane at 45. `MarinerOverlay` draws mariner data above all official data,
   the reverse of S-98 Main §9.2.1. Nothing tells the next product or
   source where it goes.

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

Once wrapped, a live source gets a place in the layer order (D6), headless
and Skia rendering, MCP and the pick pipeline from the processor side,
without its own code changing.

The 1 Hz constraint is met by keeping the current Mapsui overlay path as an
**optimization for interactive maps**, not as the contract. When a source has
an `IDynamicFeaturePortrayal`, the session may draw it through
`PortrayedDynamicFeatureRenderer`, which applies incremental updates to a
`MemoryLayer`, and place that layer at its position in the layer order (D6)
rather than in the overlay band. Headless renderers and MCP use the
processor.

**Time.** Live sources stay at "now" in this work, and
`DynamicFeatureTracker` gains no history yet. The adapter is where history
goes later: a source that keeps history makes the adapter an
`ITimeAwareDatasetProcessor`, and the rule is that a live source shows its
state at the view time when its history covers that time, and its latest
state otherwise. AIS track trails, NDBC station series and replay of
recorded AIS or NMEA logs all build on that.

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
    public InformationCategory Category { get; init; } = InformationCategory.Supplementary;  // D6
    public Type? Options { get; init; }                      // options record, drives a generic settings page
    public SourceActivation Activation { get; init; }        // Always, or BelowViewportSpan(degrees)
    public required Func<SourceCreationContext, IDatasetProcessor> Create { get; init; }
    public IReadOnlyList<IPickContributor> PickContributors { get; init; } = [];  // §3
}
```

`SourceCreationContext` carries the options, an `ILoggerFactory`, a
`TimeProvider`, an `HttpClient` factory with the hardened TLS handler, and
the secret lookup of D7, which replaces what AIS does by hand today.

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

#### D6. Layer order comes from information category and draw class

S-98 has two mechanisms for ordering, and they do different jobs:

- **Display planes and drawing priority** (S-100 Part 9 §11.6, S-98 Main
  §7.2.9–7.2.10, `S100_IC_DisplayPlane` in an Interoperability Catalogue)
  are normative. They order drawing within a product and, through the
  catalogue, across the S-100 products it names. The Part A §A-6.9.1
  Level 1 example interleaves S-102 with the ENC so soundings, aids to
  navigation and obstructions stay above the grid.
- **The priority of information list** (S-98 Main §9.2.1, from
  MSC.530(106)/Rev.1) is informative. It ranks what mustn't be obscured,
  not a drawing order: 1 alerts, 2 official points, curves and surfaces,
  3 notices, manual input and warnings, 4 cautions, 5 official colour fills,
  6 official on-demand data, 7 radar and AIS, 8–9 mariner's data. Annex A
  §4.4 gives the tools: transparency at Level 0, interleaving at Level 1.

`S98DisplayPlane` folds both into one numbered list, which is why every
newcomer has to pick a slot (§2.2, problem 5). Instead, every layer, whether
a product's sub-layer or a source, declares two facts, and the order is
derived from them.

**Information category**, after §9.2.1, plus one category S-98 doesn't
have. A layer takes the category of what it shows, whoever produced it.
`Supplementary` is only for information §9.2.1 has no place for. Whether
data is official or S-100 is shown separately, as it is today (`IsS100` and
the "Not an S-100 product" badge).

| Category | §9.2.1 | Default for |
|---|---|---|
| `Alert` | 1 | ECDIS overscale, caution and other indications |
| `OfficialChart` | 2, 5 | S-101, S-401, S-57 |
| `NoticesAndWarnings` | 3 | S-124, S-412, CAP (marine weather warnings, like S-412) |
| `Caution` | 4 | Caution features of official products |
| `OfficialOverlay` | 2 | S-122, S-125, S-127, S-128, S-131, S-201, S-411, NATO AML |
| `OfficialOnDemand` | 6 | S-102, S-104, S-111, S-129 |
| `Vessel` | 7 | AIS targets, own-ship |
| `Mariner` | 8, 9 | S-421 routes, route checks, the mariner's own annotations |
| `Supplementary` | (none) | Weather models (GRIB2, NetCDF), weather analyses and tropical cyclone tracks, NDBC, NMEA weather, generated predictions, customer feeds |

`OfficialOverlay` separates official products that aren't the ENC from the
ENC itself, so they never interleave with ENC symbols by drawing priority
alone. §9.2.1 doesn't make that split; it's a library choice, as
`OtherChartOverlays` is today. NATO AML is official data from a hydrographic
office, though not for civil navigation, so it's an official overlay, with a
within-category priority above the other official overlays.

The registration gives the default category for each layer kind, and a
dataset can declare its own. Tide predictions that SoundCharts computes and
writes as S-104 stations are `Supplementary`, not `OfficialOnDemand`, so
official S-104 draws above them, as "S-104 always wins" in the tide
predictions brief requires. The pick report and MCP can then tell predicted
data from official data by category.

**Draw class**: `Fill` for area fills and coverage surfaces,
`LineAndSymbol` for curves, points, arrows, isolines and barbs, and `Text`.

The stack, from bottom to top:

1. The base: `OfficialChart` fills, then fills that replace chart content.
   S-102 replaces the ENC depth areas (Part A §A-6.9.1), so it's base-chart
   content, not an overlay. A replacing fill is named by a catalogue rule or
   by its registration.
2. Other fills, lower priority first: `Supplementary`, `Mariner`,
   `OfficialOnDemand`, `OfficialOverlay`, `Caution`, `NoticesAndWarnings`.
   Every fill above the base is translucent (Annex A §4.4.1), so none of
   them hides what's beneath.
3. Lines and symbols, lower priority first: `Supplementary`, `Mariner`,
   `OfficialOnDemand`, `OfficialOverlay`, `Caution`, `NoticesAndWarnings`,
   `OfficialChart`.
4. Text, in the same order (S-98 Main §10.8 draws text last).
5. Vessels: AIS targets, then own-ship.
6. Alerts.

Within one step, a within-category priority orders peers (it replaces
`WithinPlanePriority`), then drawing priority, then the Part 9 type order.

Two more rules complete the order:

- **Vessels draw above the chart.** §9.2.1 ranks radar and AIS (7) below
  official points (2). ECDIS practice draws own-ship and targets on top,
  because they're sparse, live and safety-critical, and the list is
  informative. Vessels get their own step above chart text and below
  alerts.
- **Interoperability Catalogue rules still win** for S-100 products. They
  operate on the derived order, as the R-rules do on planes today, so
  Level 1 interleaving keeps working.

What changes from today:

- S-111 arrows and weather barbs draw below ENC line work and symbols, as
  §9.2.1 reads. R-111-A's placement above the ENC becomes a catalogue rule
  if we want to keep it.
- Mariner data draws below official data instead of above it.
- Weather fills still draw above S-102, as they do today, because S-102 is
  in the base.
- AML fills still draw above the chart, S-102 and weather, and below
  cautions and warnings, as the AML design pass chose. AML lines and
  symbols now draw under ENC line work instead of over it, so the ENC
  safety contour stays dominant over AML contours without depending on its
  halo.
- S-129 moves from the chart overlays to official on-demand data, which
  §9.2.1 names explicitly (layer 6, under-keel clearance).
- NDBC, NMEA weather and other supplementary data draw under official
  symbols. AIS and own-ship stay above the chart, now in the vessel step
  rather than the overlay band.
- `ProductRegistration.DefaultPlanes` becomes a category for each layer
  kind, `DynamicSourceMetadata.Plane` becomes a category, and a dataset can
  override its registration's category.
  `S98DisplayPlane` becomes the derived key, or goes away. The layer stack
  groups by category.
- Visual baselines that include S-111, weather symbols, AML or mariner data
  change.

#### D7. Secret options are stored by reference

An options record marks secret properties with `[Secret]`. The settings
page masks them and saves only a reference (`sc-secret:…`, as SECOM's
identities use `sc-ident:…`). The value goes into an `ISecretStore`: the
Keychain on macOS, Credential Manager on Windows, libsecret on Linux. This
follows `ISecomKeyStore`, which keeps SECOM private keys out of
`settings.json` the same way.

The lookup is the environment variable the registration names, then the
store. Where there's no store (a headless Linux machine or CI runner),
SoundCharts says it can't save the secret and names the environment
variable. `s100` and the MCP server use environment variables only. An AIS
key already in `settings.json` moves to the store the first time SoundCharts
reads it.

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
- **A fixed plane list, renamed by role and extended when needed.** It's the
  smallest change, but each newcomer still picks a slot and a number, and
  a conflict still means a new plane. It's what we have now.
- **Interoperability Catalogue only, with built-in defaults.** It's purely
  S-98, but a catalogue can't name products that aren't S-100, which is
  where most new data comes from.
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
- Each kind is registered (P6).

**Sections are snapshots.** Whatever a section shows that depends on time is
in the section itself: a station series holds the whole series, a warning
its validity. SoundCharts hands each presenter the view time and display
settings, as `StationChartContext` does for station charts today, and the
presenter moves its own now-marker or status. Neither the pick nor the
section is re-run when the time changes.

A station series section carries its channels (height, or speed and
direction), so the presenter no longer chooses a chart by product
(`hit.ProductSpec switch { "S-104" => …, "S-111" => … }` in
`PickReportViewModel`). Tide predictions written as S-104 stations, and NDBC
series later, draw the same way.

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

Hosts find a kind's presenter and serialization through the kind registry
(P6).

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

#### P6. Section kinds are registered

MCP serializes with source-generated metadata, so it can only write types
it's told about up front. `McpJson` already lists the subtypes of
`SampledValue` this way. A plug-in's section type would otherwise reach MCP
as its base type only.

Each kind registers its name, type, serialization metadata and a one-line
description:

```csharp
services.AddPickSectionKind(
    "acme.ice-chart",
    AcmeJsonContext.Default.AcmeIceChartSection,
    "Acme ice chart: stage of development and the analyst's note.");
```

`McpJson` builds the section's type discriminators from the registry, as it
does for `SampledValue`, and the tool schema lists each kind's description.
An unknown kind falls back to its rows. SoundCharts uses the same registry
to find a kind's presenter.

Built-in kinds have no prefix. Other kinds start with their owner's name
(`acme.ice-chart`), and registering a name twice fails at start-up.

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
   options record, and its API key is stored by reference (D7). It draws as
   supplementary data under official chart symbols (D6), shows egg codes in
   the pick report and in MCP, and renders headless. No core package
   changes.

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
   `IPickContributor`, the global registry, the kind registry and the
   SoundCharts presenter lookup. Move the egg code to a global contributor
   and a section, with the `FeatureInfo.EggCode` shim. Serialize sections in
   MCP. This proves the pattern on the case with one consumer and a builder
   that works with any data source.
2. **Station series and warnings as sections.** Move `StationSeries`, with
   its channels in the section, and `NavigationalWarning`, then the two
   notice flags. Remove the shims in the following release.
3. **Layer order by category and draw class.** Add the categories and the
   derived order, give each product registration its categories, let a
   dataset override its category (tide predictions), move the R-rules onto
   the derived order, and rebaseline.
4. **`ILiveDatasetProcessor`.** Add the interface and the session's
   re-render on `Changed`.
5. **`DynamicSourceProcessor`.** Add the adapter. Move NDBC and NMEA weather
   onto it first, since they already use `IDynamicFeaturePortrayal`.
6. **AIS and own-ship portrayals.** Move both to `IDynamicFeaturePortrayal`
   and mark `IDynamicFeatureRenderer` obsolete.
7. **`SourceRegistration` and source dataset entries.** Add the registry,
   categories for sources and the `DatasetEntry` origin. Move AIS, NDBC,
   NMEA weather and own-ship registrations onto it, with the shared
   activation wrapper.
8. **Secret store.** Add `ISecretStore`, `[Secret]` and the AIS key
   migration.
9. **Add source UI and generic settings pages.**
10. **Pick actions.** Move "Take helm" to a contributed action, and merge
    `DynamicPickHit` into `PickHit`.

Slices 1–2, slice 3 and slices 4–9 are independent of each other. Live
sources take their place in the layer order once slices 3 and 5 are in.
Slice 10 needs slices 2 and 5.

## 6. Open questions

- **IEC 62288 and the vessel step.** D6 draws AIS and own-ship above chart
  text, following ECDIS practice rather than a literal reading of S-98 Main
  §9.2.1. IEC 62288 §5 and the IMO symbol guidance (SN.1/Circ.243/Rev.2)
  hold the presentation rules for own-ship and targets. Check them before
  slice 3 ships.

The other questions of the second draft were settled on 2026-11-13: NATO
AML and CAP categories, fills under S-102 (the base step), and a dataset's
own category, all in D6.
