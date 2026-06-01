# Dynamic Source Pick — Design Note (PR-D4)

> Status: **In progress** — describes the click-to-identify support
> for dynamic feature sources shipped by PR-D4. See
> [Dynamic feature sources](dynamic-feature-source.md) for the
> underlying push-driven abstraction (PR-D1) and
> [AIS dynamic feature source](ais-source.md) for the motivating
> AIS overlay (PR-D3).

## 0. Problem

PR-D3 wired AIS targets and own-ship onto the map via
`DynamicSourceOverlayHost`. The overlay layers are pure rendering —
they carry no Mapsui feature attributes and are *not* enumerated by
the existing `IPickService.HandlePick(MapInfo)` path, which only
walks dataset-owned layers. Clicking an AIS target therefore does
nothing.

PR-D4 closes the gap: a click anywhere on the map collects both
dataset hits (existing path) and dynamic-source hits (new path) and
displays them side-by-side in the Pick Report panel.

## 1. Decisions

### Q1. Pick-hit shape — **sibling type**

`DynamicPickHit` is a new internal record next to `PickHit`.

```
internal sealed record DynamicPickHit(
    string SourceId,
    string SourceDisplayName,
    string FeatureId,
    string? Kind,
    string DisplayLabel,
    DateTimeOffset LastUpdated,
    double Latitude,
    double Longitude,
    DynamicMotion? Motion,
    DynamicVesselGeometry? VesselGeometry,
    IReadOnlyList<DynamicPickAttributeRow> Attributes);
```

Rationale: `PickHit` is dataset-centric (FC-decoded feature type,
xlink references, dataset file name, station chart VM). Dynamic
features have a fundamentally different identity model — opaque
string id (MMSI for AIS, `"ownship"`), no FC, mutable position, free-
form `Attributes` dictionary. Mixing them invites optional-field
bloat. The pick report panel renders both with a small dispatch.

### Q2. Hit-test geometry & tolerance — **12 device-pixel radius, point-distance only for v1**

Hit testing happens in projected map units (Spherical Mercator). The
click delivers `(MPoint mapPoint, double resolution)`; tolerance is
`12 * resolution` (12 device pixels at the current zoom). Dynamic
features are converted with `SphericalMercator.FromLonLat`.

For v1 the tester treats every dynamic feature as a point regardless
of its `GeometryType` — only `Point` features ship today
(own-ship + AIS). When line/polygon dynamic features land they will
get their own paths via `Geometry.Distance`; this is noted in the
contract docs but unimplemented.

Per-renderer custom tolerance (e.g. "anywhere inside the hull
polygon") is **out of scope for v1** — see §3.

### Q3. Where pick is wired — **separate `IDynamicSourcePickService`**

A new service runs alongside `IPickService`. The click handler
(`MapInteractionController`) computes the world position from the
`MapInfo`, asks the dynamic-source service for hits, and forwards
both result lists to `IPickService.HandlePick(MapInfo, IReadOnlyList<DynamicPickHit>)`.
The pick service publishes both into `PickReportViewModel`.

Reasons:
- Different storage (registry of `IDynamicFeatureSource` vs loader
  of `IDatasetProcessor`).
- Easier to test (no `MapInfo` mocking; pure pixel math).
- Keeps `PickService` from becoming a god service.

### Q4. Pick report panel UI — **sectioned single list**

The panel grows a third section between the dataset hit-list and
the identity / attributes block:

```
┌─ FEATURES (3)              ─┐  ← existing dataset hit list (>1)
│  • Cargo vessel — MV ALPHA   │
│  • DepthArea — 12345         │
└──────────────────────────────┘
┌─ DYNAMIC FEATURES (1)      ─┐  ← new: shown when DynamicHits ≠ ∅
│  AIS — vessel.ais.cargo      │
│    MV ALPHA · 4s ago         │
│    47.602°N 122.330°W        │
│    COG 270° · SOG 12.4 kn    │
│    MMSI: 367123456            │
│    …                          │
└──────────────────────────────┘
[identity / references / attributes block — dataset only]
```

Both sections are visible at once when both have hits. Dataset
section is hidden when `Hits.Count == 0`; dynamic section is hidden
when `DynamicHits.Count == 0`. `HasPick` is now true if **either**
list is non-empty.

### Q5. Localisation — **all new strings via Strings.resx**

Keys added: `PickReport_DynamicSection`, `PickReport_LastUpdatedRelative`,
`PickReport_Mmsi`, `PickReport_VesselName`, `PickReport_CallSign`,
`PickReport_Heading`, `PickReport_Cog`, `PickReport_Sog`,
`PickReport_Dimensions`, `PickReport_Position`, `Tooltip_DynamicHit`.
Tooltips on every new control.

### Q6. Selection / highlight — **deferred**

A future PR will add an S-52-style square selection ring drawn
around the picked dynamic feature. Out of scope for PR-D4.

## 2. Threading & lifetime

- `DynamicSourcePickService` is constructed against the existing
  `DynamicFeatureSourceRegistryAccessor` (late-bound; the host
  attaches the registry when MainWindow finishes wiring). When the
  registry is unattached, `Pick(...)` returns an empty list — the
  click is a dataset-only pick, not an error.
- `IDynamicFeatureSource.CurrentFeatures` is documented as safe from
  any thread; the pick service is called on the UI thread (click
  handler) and reads the immutable snapshot.
- The hit-tester is pure and stateless; tests drive it directly.

## 3. Out of scope

- Selection ring / highlight on the map.
- ARPA-style multi-target track viewer.
- AIS aids-to-navigation, base stations, SAR aircraft.
- CPA / TCPA computations.
- Picking on time-step coverage products (S-102 / S-104 / S-111) —
  goes through dataset pick.
- Server-side / MCP exposure of dynamic picks.
- Per-source custom hit-test geometry (e.g. polygon hull regions).
  Easy follow-up: the `IDynamicSourceHitTester` becomes pluggable
  per `RendererKey`.

## 4. Test surfaces

- `DynamicSourceHitTesterTests` — point-in-tolerance hits, miss,
  visibility filter, ordering by distance, multiple sources.
- `DynamicSourcePickServiceTests` — integration with a fake
  registry; hits flow through the registry accessor.
- `PickReportViewModelTests` — `SetDynamicPicks` populates the
  `DynamicHits` collection and flips `HasPick` true; clears revert.
- `PickServiceTests` — calling `HandlePick(null, dynamicHits)`
  publishes the dynamic hits even with no `MapInfo`.

## 5. References

- S-52 / IEC 62388 — vessel symbology and pick ergonomics.
- IEC 61174 §8 — ECDIS interrogation requirements (aspirational;
  the standard does not normatively define dynamic-target
  interrogation, but the "show all object info under the cursor"
  pattern is the governing convention).
