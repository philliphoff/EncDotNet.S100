# EncDotNet.S100.DynamicSources.Ais

`EncDotNet.S100.DynamicSources.Ais` turns AIS messages into vessel targets
that a map host draws alongside chart data. `AisDynamicFeatureSource`
implements `IDynamicFeatureSource` from
[`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md), and
`IAisMessageSource` is the interface AIS drivers implement. Reference it when
you add live AIS targets to a host. SoundCharts uses it.

## Install

This package isn't published to NuGet. Reference the project directly:

```xml
<ProjectReference Include="path/to/src/EncDotNet.S100.DynamicSources.Ais/EncDotNet.S100.DynamicSources.Ais.csproj" />
```

You also need a driver, such as
[`EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo`](../EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo/README.md).

## Example: track vessels in an area

```csharp
using EncDotNet.S100.DynamicSources.Ais;
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;
using EncDotNet.S100.Pipelines;

var driver = new AisStreamIoMessageSource(new AisStreamIoOptions { ApiKey = apiKey });

await using var ais = new AisDynamicFeatureSource(
    "ais",
    driver,
    new AisSubscriptionRequest { Area = new BoundingBox(47.0, -123.0, 48.5, -122.0) });

ais.Changed += (_, _) => Console.WriteLine($"{ais.CurrentFeatures.Count} targets");

// Call Sweep on your own schedule, typically once a second, to drop stale targets.
ais.Sweep(DateTimeOffset.UtcNow);
```

The `BoundingBox` arguments are south latitude, west longitude, north latitude
and east longitude.

## Main entry points

| Type | Purpose |
| --- | --- |
| `AisDynamicFeatureSource` | The `IDynamicFeatureSource`. It turns each position report into a `DynamicFeature`, merges each MMSI's static and voyage data, and removes targets not heard from within the retention window (6 minutes by default) when you call `Sweep`. `UpdateArea` changes the area filter. |
| `IAisMessageSource` / `IAisSubscription` | The subscription interface over a source of decoded AIS messages. Drivers implement it, for example for the aisstream.io WebSocket service, a local antenna's NMEA output, or a recorded-log replay. |
| `AisSubscriptionRequest` / `AisMessageKinds` | Filters by area, MMSI and ship-type class, and selects the message families. |
| `AisPositionReport` / `AisStaticVoyageData` / `AisTargetLost` | Decoded AIS payloads, with fields named so callers don't need AIS message-type numbers. |
| `AisShipType` / `AisShipTypeClass` (with `ToClass` and `ToKindToken`) | Raw ship-type codes and the display classes from ITU-R M.1371-5 Table 53, used in `DynamicFeature.Kind`. |
| `AisNavigationStatus` | Navigation status, per ITU-R M.1371-5 §3.3.7.2.1. |
| `AisDimensions` | Hull dimensions A, B, C and D, converted to length, beam, bow offset and port offset. |

## How the layers fit

The wire format stays in the driver assembly:

```text
[recorded log | local antenna | aggregator service]
   │   wire bytes
   ▼
[driver assembly, such as .Drivers.AisStreamIo]
   │   IAisMessageSource (this assembly)
   ▼
[AisDynamicFeatureSource]
   │   IDynamicFeatureSource
   ▼
[SoundCharts, or your host]
```

`AisDynamicFeatureSource` works only with typed records, so you can test it
without wire-format fixtures.

## Draw the targets

The source sets `RendererKey = "vessel.ais"`. To draw its features, register a
matching `IDynamicFeatureRenderer` in `EncDotNet.S100.Renderers.Mapsui`;
`AisVesselRenderer` there does this.

## See also

- [AIS dynamic feature source](../../docs/design/ais-source.md): the design
  note.
- [Dynamic feature sources](../../docs/design/dynamic-feature-source.md): the
  base interfaces.
- [AIS zoom-gated subscription](../../docs/design/ais-zoom-gated-subscription.md):
  how SoundCharts waits to subscribe until the visible area is smaller than a
  configurable span.
