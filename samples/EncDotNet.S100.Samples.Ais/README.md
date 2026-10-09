# EncDotNet.S100.Samples.Ais

This console sample streams live AIS vessel positions from
[aisstream.io](https://aisstream.io) and prints each vessel as it's added,
updated or removed. It shows the AIS dynamic feature source,
`AisDynamicFeatureSource`, without a UI.

## Prerequisites

- The .NET 10 SDK.
- A free API key from [aisstream.io](https://aisstream.io).

## Run the sample

1. Set the `ENCDOTNET_AIS_STREAM_KEY` environment variable to your API key:

   ```bash
   export ENCDOTNET_AIS_STREAM_KEY=<your-key>
   ```

2. From the repository root, run the sample:

   ```bash
   dotnet run --project samples/EncDotNet.S100.Samples.Ais
   ```

   The sample subscribes to the whole world and prints:

   ```text
   Connected to aisstream.io, listening on world. Press Ctrl+C to exit.
   ```

3. Press Ctrl+C to stop. The sample prints how many vessels it saw.

To keep the subscription small, and within aisstream.io's fair-use limits,
pass a bounding box as `minLat,minLon,maxLat,maxLon` in decimal degrees. For
example, for San Francisco Bay:

```bash
dotnet run --project samples/EncDotNet.S100.Samples.Ais -- 37.4,-123.0,38.2,-122.0
```

If the environment variable isn't set, the sample prints how to set it and
exits with code `1`.

## Output

Each line starts with a marker:

| Marker | Meaning |
|---|---|
| `+` | The first message from a vessel. |
| Two spaces | An update for a vessel already seen. |
| `-` | A vessel removed because it sent no message for 6 minutes. |

A line shows the vessel ID, the feature kind, the position, the speed over
ground and the course over ground:

```text
+ ais:367123450  vessel.ais.cargo       37.7785,-122.3850 SOG= 12.4kn COG=270°
```

## How it works

The sample connects three layers, described in the
[AIS source design note](../../docs/design/ais-source.md):

```text
AisStreamIoMessageSource       driver: the aisstream.io WebSocket connection
  ↓ IAisMessageSource
AisDynamicFeatureSource        keeps one feature per MMSI, ages out old targets
  ↓ IDynamicFeatureSource
Program.cs                     prints each Changed event to the console
```

The driver is in `EncDotNet.S100.DynamicSources.Ais.Drivers.AisStreamIo`.
[`Program.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/samples/EncDotNet.S100.Samples.Ais/Program.cs)
subscribes to the source's `Changed` event and reads `CurrentFeatures` for the
added and updated vessels.

SoundCharts uses the same `AisDynamicFeatureSource` for its AIS overlay. To
turn it on, open **Settings** > **Vessels**, select **Enable AIS overlay**, and
enter your **API key** or set the same environment variable. The change takes
effect when you restart SoundCharts.
