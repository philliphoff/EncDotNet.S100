# EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo

`EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo` is an
`IAisMessageSource` driver for [aisstream.io](https://aisstream.io), a free
WebSocket AIS streaming service. Reference it with
[`EncDotNet.S100.DynamicSources.Ais`](../EncDotNet.S100.DynamicSources.Ais/README.md)
to show live AIS targets from aisstream.io. You need an aisstream.io API key.

## Install

This package isn't published to NuGet. Reference the project directly:

```xml
<ProjectReference Include="path/to/src/EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo/EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo.csproj" />
```

It uses only the .NET base class library
(`System.Net.WebSockets.ClientWebSocket` and `System.Text.Json`), with no
third-party AIS or WebSocket libraries.

## Example: create the driver

```csharp
using EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo;

var driver = new AisStreamIoMessageSource(new AisStreamIoOptions { ApiKey = apiKey });
```

Pass the driver to `AisDynamicFeatureSource`; see the
[`EncDotNet.S100.DynamicSources.Ais` example](../EncDotNet.S100.DynamicSources.Ais/README.md#example-track-vessels-in-an-area).

## Main entry points

| Type | Purpose |
| --- | --- |
| `AisStreamIoMessageSource` | The `IAisMessageSource` backed by aisstream.io. |
| `AisStreamIoOptions` | The API key, endpoint URI, subscribe deadline and reconnect backoff. |
| `IAisStreamIoTransport` | A test seam over `ClientWebSocket` that exchanges JSON text frames. |
| `ClientWebSocketTransport` | The production transport. It wraps `System.Net.WebSockets.ClientWebSocket` and reassembles fragmented frames. |

Two internal types do the rest:

- `AisStreamIoSubscription` is one subscription. It reconnects automatically,
  with truncated exponential backoff from 250 ms to 30 s, and replaces the area
  filter in `TryUpdateArea`.
- `AisStreamIoJson` reads `PositionReport` and `ShipStaticData` messages with a
  hand-written JSON contract. It turns AIS "not available" values (511, 360,
  102.3 and -128) into missing values, and redacts the API key for logging.

## Service behaviour

- aisstream.io is a beta service with no published service level. The driver
  reconnects when the connection drops.
- The service needs the subscribe frame within 3 seconds of connecting
  (`SubscribeDeadline`).
- To change the area filter, the driver sends the subscribe frame again on the
  same socket. `TryUpdateArea` does this and always returns `true`.

## API key handling

The driver never logs the API key. Every outgoing frame goes through
`AisStreamIoJson.RedactApiKey` before it's logged, and a regression test checks
this.

## See also

- [AIS dynamic feature source](../../docs/design/ais-source.md): the design
  note; section 12 covers the aisstream.io protocol.
