# EncDotNet.S100.Samples.Ais

A small console sample demonstrating PR-D3's AIS dynamic feature
source. Connects to [aisstream.io](https://aisstream.io)'s free
WebSocket service via `EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo`
and prints decoded position / static-voyage messages projected
through `AisDynamicFeatureSource`.

## Prerequisites

* A free API key from <https://aisstream.io>.

## Run

```bash
export ENCDOTNET_AIS_STREAM_KEY=<your-key>
dotnet run --project samples/EncDotNet.S100.Samples.Ais
```

To constrain the subscription to a bounding box (helps stay inside
aisstream.io's reasonable-use envelope):

```bash
# San Francisco Bay area
dotnet run --project samples/EncDotNet.S100.Samples.Ais -- 37.4,-123.0,38.2,-122.0
```

## Output

* `+` first sighting of a vessel.
* (two leading spaces) subsequent updates of a known vessel.
* `-` aged-out target (no message in 6 minutes).

A typical line looks like:

```
+ ais:367123450  vessel.ais.cargo       37.7785,-122.3850 SOG= 12.4kn COG=270°
```

## Architecture

The sample composes the three layers documented in
[`docs/design/ais-source.md`](../../docs/design/ais-source.md):

```
AisStreamIoMessageSource (driver)
   ↓ IAisMessageSource
AisDynamicFeatureSource (per-MMSI cache, aging, projection)
   ↓ IDynamicFeatureSource
this sample's Console.WriteLine sink
```

The same `AisDynamicFeatureSource` is reused by the viewer's AIS
overlay (gated on `ViewerSettings.AisOverlay.Enabled` and the same
environment variable).
