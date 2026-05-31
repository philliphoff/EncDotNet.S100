# EncDotNet.S100.DynamicSources.Ais

Concrete `IDynamicFeatureSource` for AIS targets, plus the
driver-agnostic `IAisMessageSource` abstraction.

## What ships here

| Type | Purpose |
| --- | --- |
| `IAisMessageSource` / `IAisSubscription` | Subscription-based contract over a producer of decoded AIS messages. Drivers (aisstream.io WebSocket, local antenna NMEA, recorded-log replay) implement this interface. |
| `AisSubscriptionRequest` / `AisMessageKinds` | Spatial / MMSI / ship-type-class filters and message-family selector. |
| `AisPositionReport` / `AisStaticVoyageData` / `AisTargetLost` | Decoded AIS payloads — denormalised so callers don't see message-type numbers. |
| `AisShipType` / `AisShipTypeClass` (+ `ToClass`, `ToKindToken`) | Raw shiptype codes and the ITU-R M.1371-5 Table 53 display buckets used in `DynamicFeature.Kind`. |
| `AisNavigationStatus` | Navigation status enum per ITU-R M.1371-5 §3.3.7.2.1. |
| `AisDimensions` | Hull dimensions (A/B/C/D) folded into length/beam/bow-offset/port-offset. |
| `AisDynamicFeatureSource` | The actual `IDynamicFeatureSource` — projects each position report to a `DynamicFeature`, merges per-MMSI static / voyage data, and ages stale targets. |

## Architecture

Three-layer split with the wire format isolated to driver assemblies:

```text
[recorded log | local antenna | aggregator service]
   │   wire bytes
   ▼
[driver assembly (e.g. .Drivers.AisStreamIo)]
   │   IAisMessageSource (this assembly)
   ▼
[AisDynamicFeatureSource]
   │   IDynamicFeatureSource
   ▼
[viewer / sample]
```

`AisDynamicFeatureSource` consumes only typed records and is
unit-testable without any wire-format fixtures.

## Renderer key

The source advertises `RendererKey = "vessel.ais"`. Register a matching
`IDynamicFeatureRenderer` in `EncDotNet.S100.Renderers.Mapsui` (the
`AisVesselRenderer`, ships separately).

## See also

- `docs/design/ais-source.md` — full design notes.
- `docs/design/dynamic-feature-source.md` — base abstractions.
- `docs/design/ais-zoom-gated-subscription.md` — viewer-side gate
  that defers the first subscription until the visible viewport
  has shrunk below a configurable span.
- `EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo` — production driver.
