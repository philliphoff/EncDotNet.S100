# AIS dynamic feature source — Design Note

> Status: **Design accepted** — implementation lands in the same PR
> as this note (PR-D3). Closes the dynamic-feature-source thread
> opened by PR-D1 (#124), continued in PR-D2 (#130, own-ship singleton
> source), PR-D2.1 (#131, layer-stack wiring), and PR-D2/Q2 (#136,
> own-ship symbology).

## 0. Scope

Ship the **second concrete `IDynamicFeatureSource`** — AIS targets
streamed from [aisstream.io](https://aisstream.io) — and prove the
`DynamicVesselGeometry` sidecar that the own-ship work pre-staged
against its primary intended use case.

This is a single PR against `main`, branch
`philliphoff/pr-d3-ais-sample`.

In scope:

1. A push-driven AIS abstraction (`IAisMessageSource`,
   subscription-based, with optional bounding-box filtering and
   in-place subscription updates).
2. A concrete driver targeting aisstream.io's WebSocket service.
3. A concrete `AisDynamicFeatureSource` projecting AIS reports onto
   `DynamicFeature` with per-MMSI static-data merging and stale-target
   aging.
4. An AIS-specific renderer (`AisVesselRenderer`) sharing hull-outline,
   arrowhead, and CCRP-cross logic with `OwnShipRenderer` via a new
   internal `VesselSymbology` helper.
5. Viewer integration behind a `ViewerSettings.AisOverlay` flag.
6. A new `samples/EncDotNet.S100.Samples.Ais` Avalonia mini-app.

Out of scope (each captured in §13):

- Local antenna / serial / TCP NMEA-0183.
- Replay-from-recorded-JSON driver (offline mode for the sample).
- AIS message families beyond `PositionReport` and `ShipStaticData`.
- S-52 sleeping / lost / dangerous-target pictogram variants.
- CPA / TCPA computations.
- MCP-server exposure of the AIS overlay.
- Any change to `DynamicFeature` / `DynamicMotion` /
  `DynamicVesselGeometry` shapes.

---

## 1. Why aisstream.io for the first integration

The PR's request originally framed the first integration as a
recorded-AIVDM-log driver. After scouting the .NET AIS landscape
(`dotMorten/NmeaParser` does not decode AIVDM payloads;
`ais-dotnet/Ais.Net` does but is itself BETA-quality and adds a
non-trivial dep), the user redirected this PR to target
aisstream.io directly. The resulting design is materially simpler:

| Property | Recorded AIVDM log | aisstream.io WebSocket |
|---|---|---|
| Wire decoder needed | Yes (`Ais.Net` or hand-rolled) | **No** — service ships JSON |
| Bounding-box filtering | Client-side after decode | **Native protocol feature** |
| Sentinel-value handling (511/360/102.3/128) | Decoder must surface | **Live at the JSON boundary** |
| Multi-vessel demo data | Synthetic, ~30 KB | **Live, real-world** |
| Test isolation | Inject AIVDM bytes | Inject decoded JSON via transport seam |
| Live demo viability | None | **Yes** (with API key) |

The trade is that the live-service path requires authentication (an
aisstream.io API key) and has a BETA SLA caveat. The middle
abstraction (`IAisMessageSource`) insulates callers from that
specific service so a future swap to a self-hosted decoder, a local
antenna, or another aggregator is mechanical.

---

## 2. Architectural shape

```
┌─────────────────────────────────────────────────────────────┐
│ aisstream.io WebSocket | future antenna | future aggregator │
│                  driver-native wire protocol                │
└─────────────────────────────┬───────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo      │
│  ─ ClientWebSocket + System.Text.Json (BCL only)            │
│  ─ IAisStreamIoTransport seam (testability)                 │
│  ─ Sentinel-to-null collapse at this layer                  │
│  ─ Reconnect with exponential backoff                       │
│  ─ API-key redaction in logging                             │
└─────────────────────────────┬───────────────────────────────┘
                              │ IAisMessageSource (our abstraction)
                              │ Subscribe(AisSubscriptionRequest)
                              │     → IAisSubscription
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  EncDotNet.S100.DynamicSources.Ais                          │
│  ─ AisDynamicFeatureSource : IDynamicFeatureSource          │
│  ─ DynamicFeatureTracker<AisPositionReport> for snapshots   │
│  ─ Per-MMSI ShipStaticData cache merged at projection time  │
│  ─ 6-min aging sweep (ITU-R M.1371 longest interval)        │
│  ─ UpdateArea(BoundingBox?) → TryUpdateArea or resubscribe  │
└─────────────────────────────┬───────────────────────────────┘
                              │ IDynamicFeatureSource
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  EncDotNet.S100.Viewer.Services.DynamicSources              │
│  ─ DynamicSourceOverlayHost (existing — unchanged)          │
│  ─ Resolves AisVesselRenderer via RendererKey "vessel.ais"  │
└─────────────────────────────┬───────────────────────────────┘
                              ▼
┌─────────────────────────────────────────────────────────────┐
│  EncDotNet.S100.Renderers.Mapsui.DynamicSources             │
│  ─ AisVesselRenderer + OwnShipRenderer  (both call into…)   │
│  ─ VesselSymbology helper (extracted hull/arrow/CCRP/COG)   │
└─────────────────────────────────────────────────────────────┘
```

Three responsibilities, three libraries:

| Library | Knows about |
|---|---|
| `EncDotNet.S100.DynamicSources.Ais` | AIS records, subscription contract, DynamicFeature projection, aging |
| `EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo` | Only aisstream.io's wire protocol |
| `EncDotNet.S100.Renderers.Mapsui` (extended) | Only how to draw vessels, given dimensions and motion |

The **viewer** never sees AIS-specific records — it sees only
`IDynamicFeatureSource`, exactly as it does for own-ship. This is
the design contract.

---

## 3. Q1 — Where does the sample live?

**Decision: (c) both.** A new `EncDotNet.S100.Samples.Ais` Avalonia
mini-app under a new `/samples/` solution folder, *and* an in-viewer
flag (`ViewerSettings.AisOverlay`) that registers the same source
behind a UI toggle.

The shared abstraction (`IAisMessageSource`) makes this duplication a
single line of DI plumbing on each side. The standalone sample is
the demonstration vehicle for AI / route-planning agents that don't
want to spin up the viewer; the in-viewer integration is the
contract test that the abstraction composes with the existing
overlay host.

The sample reads its API key from the `ENCDOTNET_AIS_STREAM_KEY`
environment variable. With no key it surfaces a clear error and
exits — there is **no offline mode in this PR**. Adding a
`LocalRecordedJsonSource` for offline replay is a sibling future PR
(see §13).

---

## 4. Q2 — Source library shape

**Decision: a standalone class library** —
`EncDotNet.S100.DynamicSources.Ais` — referenced by the viewer, the
sample, and any future agent / MCP host. Following the same pattern
as the dataset libraries.

Rationale: own-ship was viewer-internal because the synthetic
dead-reckoner is a viewer toy. AIS is reusable across many hosts; a
dedicated library is the right granularity.

Dependencies:

- `EncDotNet.S100.Core` (for `BoundingBox`, `DynamicFeature`,
  `DynamicFeatureTracker`, `IDynamicFeatureSource`, etc.).
- Nothing else — no Mapsui, no Avalonia, no JSON, no WebSockets.

The driver library
(`EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo`) takes the
WebSocket / JSON deps and depends on the abstraction library plus
BCL only.

---

## 5. Q3 — Wire decoder

**Decision: none.** aisstream.io publishes already-decoded JSON over
a WebSocket. The driver is built on `System.Net.WebSockets.ClientWebSocket`
+ `System.Text.Json` (both in the BCL). No `Ais.Net`. No
`NmeaParser`. No third-party dep.

We deliberately do not take a dep on the upstream
`aisstream/ais-message-models` C# package. Hand-rolled JSON contract
types for the two message families we consume (`PositionReport` and
`ShipStaticData`) total under 100 lines and decouple us from the
service's BETA-stage versioning churn.

---

## 6. Q4 — Test fixtures

**Decision: synthetic JSON-lines fixtures replayed through a
transport seam.** No live aisstream.io traffic in CI under any
circumstance.

The driver factors its WebSocket I/O behind a small interface:

```csharp
internal interface IAisStreamIoTransport : IAsyncDisposable
{
    Task ConnectAsync(Uri endpoint, CancellationToken ct);
    Task SendTextAsync(string text, CancellationToken ct);
    IAsyncEnumerable<string> ReceiveTextAsync(CancellationToken ct);
}
```

The production implementation wraps `ClientWebSocket`. Tests inject a
deterministic in-process fake that replays a JSON-lines fixture and
captures sent frames for assertion.

Driver tests cover:

1. Connect → send subscribe within 3 s of `ConnectAsync` returning.
2. Replay a fixture of ~15 messages (2-3 vessels, including one
   `ShipStaticData` per vessel) → assert the decoded
   `AisPositionReport` / `AisStaticVoyageData` sequence.
3. `TryUpdateArea` sends a fresh subscribe frame on the same
   connection (per aisstream.io's documented "swap and replace, not
   merge" semantics).
4. Reconnect after transport fault re-sends subscribe.
5. Malformed JSON line is dropped without taking the connection
   down.
6. API key never appears in any log line emitted by the driver
   (assert against an `ITestLoggerProvider`).

---

## 7. Q5 — AIS → DynamicFeature mapping

The driver decodes aisstream.io JSON into our typed records. The
source layer projects records onto `DynamicFeature` at publish time.
Sentinel handling lives in the driver — the source never sees
sentinels.

### 7.1 Identity and kind

| Field | Mapping |
|---|---|
| `DynamicFeature.Id` | `"ais:" + Mmsi.ToString(CultureInfo.InvariantCulture)` |
| `DynamicFeature.Kind` | `"vessel.ais." + class` where `class` is one of `cargo`, `tanker`, `passenger`, `highspeedcraft`, `pleasure`, `fishing`, `tug`, `sar`, `lawenforcement`, `military`, `sailing`, `pilot`, `other`, `unknown` |
| `DynamicFeature.GeometryType` | `Point` |
| `DynamicFeature.Coordinates` | `[(Latitude, Longitude)]` |
| `DynamicFeature.LastUpdated` | aisstream.io `Metadata.time_utc` (UTC) |

The `class` bucket is derived from AIS shiptype code per ITU-R
M.1371-5 Table 53 (cited in `AisShipTypeClass` XML doc comments).
Until a `ShipStaticData` arrives for a given MMSI the bucket is
`unknown`.

### 7.2 Motion

```csharp
Motion = new DynamicMotion
{
    CourseOverGroundDeg = positionReport.CourseOverGroundDeg,  // null if 360 sentinel
    HeadingDeg          = positionReport.HeadingDeg,           // null if 511 sentinel
    SpeedOverGroundKn   = positionReport.SpeedOverGroundKn,    // null if 102.3 sentinel
};
```

Sentinels are collapsed to `null` at the **driver** layer, so the
source layer's `DynamicMotion` projection is a straight copy. The
nullability of the existing `DynamicMotion` fields handles the
already-absent case correctly.

### 7.3 Vessel geometry

When a `ShipStaticData` arrives for an MMSI, the driver caches it
internally and the source merges it into every subsequent
`DynamicFeature` for that MMSI:

```csharp
VesselGeometry = staticData is { Dimensions: { } d } d.LengthMetres > 0 && d.BeamMetres > 0
    ? new DynamicVesselGeometry
      {
          LengthMetres     = d.LengthMetres,      // A + B
          BeamMetres       = d.BeamMetres,        // C + D
          BowOffsetMetres  = d.BowOffsetMetres,   // A
          PortOffsetMetres = d.PortOffsetMetres,  // C
      }
    : null;
```

A vessel that has not yet emitted a Type-5 / Type-24 part-A is
rendered with the pictogram fallback (no hull, classic AIS triangle).

### 7.4 Aging

`DynamicFeatureTracker.Sweep` runs on a `PeriodicTimer`-driven loop
inside `AisDynamicFeatureSource`. Default retention is **6 minutes**
— covers ITU-R M.1371-5's longest at-anchor reporting interval
(3 minutes) with comfortable margin. Configurable via constructor
for tests and aggressive deployments. Aisstream.io has no explicit
"vessel left coverage" signal; aging is the only retirement
mechanism for this driver. (A future aggregator-with-coverage
driver could raise `AisTargetLost` events to short-circuit aging.)

---

## 8. Q6 — Renderer

**Decision: refactor first, then add `AisVesselRenderer`.**

Today, `OwnShipRenderer` contains four bits of vessel-rendering
logic that are not own-ship-specific:

1. Hull-outline polygon construction (5-vertex, parameterised by
   `DynamicVesselGeometry` and heading).
2. Arrowhead at the COG-vector tip.
3. CCRP cross at the GPS antenna position.
4. COG / heading vector itself (6-min predictor, capped at 10 nm).

Extract those into an internal helper class
`VesselSymbology` in `EncDotNet.S100.Renderers.Mapsui.DynamicSources`,
parameterised by:

- `Coordinate` (lat/lon).
- `DynamicVesselGeometry?` (null → no hull / no CCRP cross).
- `DynamicMotion?` (null → no COG vector).
- `Mapsui.Styles.Color stroke / fill / hullFill`.
- `double minVesselPixels` (per S-52 §7.4.5; default 22 px / 6 mm).

`OwnShipRenderer` becomes a ~30-line wrapper that picks the
own-ship palette and forwards. Existing `OwnShipRendererTests` stay
green without modification — that's the regression contract.

`AisVesselRenderer` is a parallel ~50-line wrapper that:

- Picks a colour by `Kind` suffix lookup (`vessel.ais.cargo` →
  cargo palette, `vessel.ais.tanker` → tanker palette, etc.).
  Default palette for `unknown` and any unmapped kind.
- Draws the classic AIS pictogram (small filled triangle pointing
  along COG / heading) when no `DynamicVesselGeometry` is present
  or when zoomed too far out. The own-ship pictogram (filled disc)
  remains own-ship-only.

We deliberately do **not** wrap `AisVesselRenderer` in
`KindMatchingRenderer`. Per-kind dispatch is a one-line palette
lookup inside the renderer; spinning up N small renderers and a
matcher would be heavier and would mean each renderer has to
duplicate the hull / arrowhead / CCRP shared logic anyway.

### 8.1 Palette

S-52 (Annex A) does not specify per-shiptype colours; vendor
implementations differ. We pick a small, accessible palette inspired
by common ECDIS conventions, keyed on `AisShipTypeClass`. Colours
are listed in `VesselSymbology.AisPalette` with their hex codes and
WCAG contrast ratios against the chart background documented
inline. This is intentionally a *renderer* concern, not a *catalogue*
concern — there is no S-100 portrayal catalogue for AIS overlays.

---

## 9. Q7 — Layer plane

**Decision: same `S98DisplayPlane.DynamicArrows` plane as own-ship.**

Per-source visibility already works through PR-D2.1's layer-stack
panel — each `IDynamicFeatureSource` gets its own row regardless of
plane. Splitting AIS into its own plane would be an arbitrary
distinction that buys nothing for the user.

If a future requirement wants AIS targets *under* own-ship in z-order
(say, for an ECDIS-style "always paint own-ship on top"), the
`DynamicSourceOverlayHost` already preserves registration order;
register the AIS source first, the own-ship source last.

---

## 10. Q8 — Live timing and viewport binding

**Decision: live wall-clock; no replay knob in this PR.**

The driver pushes events as the upstream socket delivers them. The
source's aging sweep runs on a 1 Hz `PeriodicTimer`. There is no
"playback speed" concept — that idea was a recorded-log artefact and
is properly the responsibility of a future
`LocalRecordedJsonSource`.

### 10.1 Viewport binding

The viewer creates one `AisDynamicFeatureSource` whose subscription's
`Area` is bound to the map viewport's bbox. Pan / zoom calls
`AisDynamicFeatureSource.UpdateArea(BoundingBox?)`, which forwards
to `IAisSubscription.TryUpdateArea` (the aisstream.io driver
returns `true` here — the protocol explicitly supports in-place
updates). If `TryUpdateArea` returns `false` the source disposes the
subscription and opens a fresh one.

Viewport updates are debounced to 300 ms. Below that we'd hammer the
upstream service every frame while the user pans; above that the
visible-but-not-yet-subscribed gap becomes uncomfortable. The
debounce interval is a constructor parameter for tests.

When the bbox changes, we **do not** clear out-of-bbox MMSIs from
the tracker eagerly — the aging sweep handles them in due course.
This avoids a flicker if the user pans back within the retention
window.

---

## 11. Interface specifications

Authoritative shapes for the public API. Implementations follow the
same xunit-based test conventions used elsewhere in the repo.

### 11.1 Subscription request

```csharp
public sealed record AisSubscriptionRequest
{
    /// <summary>Spatial filter (EPSG:4326). null = no filter.</summary>
    public BoundingBox? Area { get; init; }

    /// <summary>Optional MMSI allow-list. null = all.</summary>
    public IReadOnlyCollection<uint>? Mmsis { get; init; }

    /// <summary>Optional ship-type class allow-list. null = all.</summary>
    public IReadOnlyCollection<AisShipTypeClass>? ShipTypeClasses { get; init; }

    /// <summary>Which message families to receive. Defaults to both.</summary>
    public AisMessageKinds Include { get; init; }
        = AisMessageKinds.PositionReports | AisMessageKinds.StaticVoyageData;
}

[Flags]
public enum AisMessageKinds
{
    None = 0,
    PositionReports = 1,
    StaticVoyageData = 2,
}
```

### 11.2 Subscription handle

```csharp
public interface IAisSubscription : IAsyncDisposable
{
    AisSubscriptionRequest ActiveRequest { get; }

    event EventHandler<AisPositionReport>? PositionReportReceived;
    event EventHandler<AisStaticVoyageData>? StaticVoyageDataReceived;
    event EventHandler<AisTargetLost>? TargetLost;

    /// <summary>
    /// Updates the spatial filter without tearing the subscription
    /// down. Returns false when the driver cannot perform an
    /// in-place update; callers must dispose and resubscribe.
    /// </summary>
    bool TryUpdateArea(BoundingBox? area);
}
```

### 11.3 Source

```csharp
public interface IAisMessageSource
{
    AisSourceMetadata Metadata { get; }
    IAisSubscription Subscribe(AisSubscriptionRequest request);
}

public sealed record AisSourceMetadata
{
    public required string DisplayName { get; init; }
    public string? Description { get; init; }
    /// <summary>True if the driver supports concurrent subscriptions
    /// without renegotiating upstream.</summary>
    public bool SupportsMultipleSubscriptions { get; init; }
}
```

### 11.4 Payload records

```csharp
public abstract record AisMessage
{
    public required uint Mmsi { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record AisPositionReport : AisMessage
{
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public double? CourseOverGroundDeg { get; init; }
    public double? HeadingDeg { get; init; }
    public double? SpeedOverGroundKn { get; init; }
    public AisNavigationStatus? NavigationStatus { get; init; }
    public double? RateOfTurnDegPerMin { get; init; }
}

public sealed record AisStaticVoyageData : AisMessage
{
    public uint? ImoNumber { get; init; }
    public string? CallSign { get; init; }
    public string? VesselName { get; init; }
    public AisShipType ShipType { get; init; }
    public AisShipTypeClass ShipTypeClass { get; init; }
    public AisDimensions? Dimensions { get; init; }
    public double? DraughtMetres { get; init; }
    public string? Destination { get; init; }
    public DateTimeOffset? Eta { get; init; }
}

public sealed record AisTargetLost
{
    public required uint Mmsi { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed record AisDimensions
{
    public required double LengthMetres { get; init; }
    public required double BeamMetres { get; init; }
    public required double BowOffsetMetres { get; init; }
    public required double PortOffsetMetres { get; init; }
}
```

### 11.5 Enumerations

```csharp
/// <summary>Raw AIS shiptype code (0–99) per ITU-R M.1371-5 Table 53.</summary>
public enum AisShipType { /* 0..99 */ }

/// <summary>Display-bucketing of AIS shiptype codes.</summary>
public enum AisShipTypeClass
{
    Unknown, Cargo, Tanker, Passenger, HighSpeedCraft,
    Pleasure, Fishing, Tug, SearchAndRescue, LawEnforcement,
    Military, Sailing, PilotVessel, Other,
}

/// <summary>AIS navigation-status codes per ITU-R M.1371-5.</summary>
public enum AisNavigationStatus
{
    UnderWayUsingEngine = 0, AtAnchor = 1, NotUnderCommand = 2,
    RestrictedManoeuvrability = 3, ConstrainedByDraught = 4,
    Moored = 5, Aground = 6, EngagedInFishing = 7,
    UnderWaySailing = 8, /* 9..13 reserved */
    AisSart = 14, NotDefined = 15,
}
```

---

## 12. aisstream.io protocol notes

Authoritative reference: <https://aisstream.io/documentation>.
Re-stated here so the design doc is self-contained against future
service drift.

| Property | Value |
|---|---|
| Endpoint | `wss://stream.aisstream.io/v0/stream` |
| Auth | API key, sent in subscribe message |
| Subscribe format | JSON: `{ "APIKey", "BoundingBoxes": [[[lat, lon], [lat, lon]]], "FiltersShipMMSI"?, "FilterMessageTypes"? }` |
| Subscribe deadline | 3 s after connect |
| Subscribe semantics | Swap-and-replace (not merge) |
| Update mechanism | Resend full subscribe frame on same socket |
| Cross-origin | **Blocked** — server-side use only (matches our model) |
| Message types in scope | `PositionReport`, `ShipStaticData` |
| Decoded format | JSON `{ MessageType, Metadata, Message: { <key>: {…} } }` |
| SLA | **BETA — none stated** |
| Throughput ceiling | ~300 msg/s at global scope |

### 12.1 JSON contract subset we hand-roll

Only the fields we actually consume. Every other property in the
upstream message is ignored (`JsonSerializerOptions.UnknownTypeHandling`
defaults are fine — `System.Text.Json` ignores unknown JSON fields
by default).

```jsonc
// PositionReport
{
  "MessageType": "PositionReport",
  "MetaData": {
    "MMSI": 123456789,
    "ShipName": "EXAMPLE",
    "latitude": 37.81234,
    "longitude": -122.43210,
    "time_utc": "2026-05-29 14:23:01.234567 +0000 UTC"
  },
  "Message": {
    "PositionReport": {
      "Cog": 045.0,           // 360 sentinel → null
      "TrueHeading": 511,     // 511 sentinel → null
      "Sog": 12.3,            // 102.3 sentinel → null
      "RateOfTurn": -127,     // -128 sentinel → null
      "NavigationalStatus": 0
    }
  }
}

// ShipStaticData
{
  "MessageType": "ShipStaticData",
  "MetaData": { "MMSI": 123456789, "time_utc": "…" },
  "Message": {
    "ShipStaticData": {
      "ImoNumber": 9876543,
      "CallSign": "EXMPL",
      "Name": "EXAMPLE",
      "Type": 70,
      "Dimension": { "A": 100, "B": 30, "C": 5, "D": 12 },
      "MaximumStaticDraught": 8.5,
      "Destination": "USOAK",
      "Eta": { "Month": 6, "Day": 1, "Hour": 14, "Minute": 30 }
    }
  }
}
```

### 12.2 Reconnect strategy

On any transport-level disconnect (`WebSocketException`, server-side
close, network error), the driver:

1. Releases the existing `ClientWebSocket`.
2. Waits with truncated exponential backoff (250 ms, 500 ms, 1 s,
   2 s, 4 s, 8 s, capped at 30 s).
3. Reconnects.
4. Re-sends the subscribe frame for the most recent active request
   within the 3 s deadline.

Active subscriptions remain valid across reconnects from the
caller's perspective — the `IAisSubscription` instance does not
change, only its underlying socket.

---

## 13. Out of scope (explicit deferrals)

| Item | Why deferred | Future PR shape |
|---|---|---|
| Local antenna / NMEA-0183 / serial / TCP | Different driver entirely; aisstream.io covers the demo case | `EncDotNet.S100.DynamicSources.Ais.Drivers.LocalAntenna` |
| Recorded-JSON replay (offline mode for sample) | Sample's "no API key" UX is a clear-fail in this PR | `EncDotNet.S100.DynamicSources.Ais.Drivers.LocalRecordedJson` |
| AIS msg families beyond Position / StaticVoyage (SAR, Aids-to-Nav, Base-Station, Safety, BinaryBroadcast…) | Each needs its own DynamicFeature shape | Per-family follow-ups |
| S-52 sleeping / lost / dangerous-target variants | Needs more design + integration with NavigationStatus + a stale-target threshold | Future PR-D4 |
| CPA / TCPA computations | Needs route-vs-AIS interaction logic; heavyweight | Future feature work |
| MCP-server exposure | Intentionally — keeps secrets out of MCP surface | Future PR if requested |
| `DynamicFeature` shape changes | None needed; pre-staged sidecars already AIS-shaped | — |

---

## 14. Test surface

| Test project | Coverage |
|---|---|
| `EncDotNet.S100.Pipelines.Tests` (new files) | `AisDynamicFeatureSource` (subscription lifecycle, Type-5 cache merging, sentinel pass-through, aging sweep, IsEnabled toggle parity with OwnShipSource), `AisVesselRenderer` (palette dispatch, hull / pictogram switching, CCRP cross presence) |
| `EncDotNet.S100.Pipelines.Tests` | `VesselSymbology` helper (the underlying logic — both renderers re-call after refactor; existing OwnShipRendererTests stay green as the regression contract) |
| New `EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo.Tests` | Driver behaviour against the `IAisStreamIoTransport` fake (subscribe within 3 s, swap-and-replace updates, reconnect re-subscribes, malformed JSON drop, **API-key redaction**) |
| `EncDotNet.S100.Viewer.Tests` | Layer-stack integration (AIS source surfaces under DynamicArrows when overlay flag is on); settings round-trip for the new flag and masked API-key field |

CI **must not** make any live aisstream.io connections under any
test path. The transport seam is the load-bearing test boundary.

---

## 15. Security & privacy

- The aisstream.io API key is a secret. It is stored in
  `ViewerSettings` alongside other persisted settings. The settings
  control uses a masked input. The key is **never** included in:
  - Log output (the driver actively redacts it from any structured
    log call — there is a regression test for this).
  - Diagnostic export bundles.
  - Crash dumps that the user might share.
  - Any file the agent / CLI writes outside the user profile dir.
- The sample app reads the key from `ENCDOTNET_AIS_STREAM_KEY`
  environment variable only. Never from the repo.
- Test fixtures contain only invented MMSIs (real-world MMSI 000000000
  and 999999999 are reserved per ITU-R M.1371; we use values in the
  reserved range to make this unambiguous).
- AIS data itself is not personal data under most jurisdictions, but
  the driver README cross-references aisstream.io's terms of service
  (the consumer is responsible for compliance).

---

## 16. Open items (not blocking)

1. **Multiple bbox per subscription** — aisstream.io supports an
   array of bboxes. Our abstraction takes a single `BoundingBox`. If
   the viewer ever needs split-screen viewports, expand the
   abstraction's `Area` field to `IReadOnlyList<BoundingBox>?` and
   adapt drivers. Out of scope for v1.
2. **MMSI / ship-type filters in viewer UI** — the abstraction
   supports them but the v1 viewer does not surface controls for
   them. Add later if requested.
3. **Renderer per-NavigationStatus styling** — at-anchor / aground /
   not-under-command etc. are hinted at by S-52 but not wired in
   v1 (would land with PR-D4 sleeping-target work).
