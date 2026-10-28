# EncDotNet.S100.DynamicSources.Nmea

`EncDotNet.S100.DynamicSources.Nmea` shows the ship's own wind, water and air
temperature and pressure from its NMEA 0183 instruments on a chart, next to
forecasts and buoy observations. `NmeaWeatherSource` implements
`IDynamicFeatureSource` from
[`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) and reads sentences
with [`EncDotNet.S100.Nmea`](../EncDotNet.S100.Nmea/README.md);
`OwnShipWeatherPortrayal` draws the feature with the code-defined portrayal
toolkit in [`EncDotNet.S100.Portrayals`](../EncDotNet.S100.Portrayals/README.md)
and the weather styles' wind barbs. The library has no UI or Mapsui
dependency. SoundCharts uses it.

Not for navigation.

## Install

This package isn't published to NuGet. Reference the project directly:

```xml
<ProjectReference Include="path/to/src/EncDotNet.S100.DynamicSources.Nmea/EncDotNet.S100.DynamicSources.Nmea.csproj" />
```

## Example: own-ship wind from a multiplexer

```csharp
using EncDotNet.S100.DynamicSources.Nmea;
using EncDotNet.S100.Nmea;

await using var weather = new NmeaWeatherSource(new NmeaUdpLineSource(10110));
weather.Changed += (_, _) => Console.WriteLine(weather.Current?.TrueWind);

// Optional: where own ship is drawn, and the motion to use when the stream has none.
weather.UpdateOwnShip(new OwnShipReference(position, heading, cog, sog, DateTimeOffset.UtcNow));

weather.Start(); // reads in the background, retrying after a failure
```

A host that already reads an NMEA stream passes no input and calls
`ProcessLine` for each line. `ReadToEndAsync` reads an input once, for a
replay.

The feature is a point at own ship, id `ownship-weather`, `Kind`
`weather.observation.ownship`, with an `OwnShipWeather` under the `weather`
attribute (`NmeaWeatherSource.GetWeather`).

## True wind

Best first; `OwnShipWeather.TrueWindSource` says which:

1. `MWD`: the instruments' own true direction and speed (a magnetic-only
   direction is turned true with the variation from `HDG` or `RMC`).
2. Derived from relative wind (`MWV` R), the heading and the speed and course
   over the ground: the wind a buoy or forecast reports, so the barbs compare
   like with like.
3. `MWV` T, turned into a direction with the heading.
4. Derived from relative wind, the heading and the speed through the water
   (`VHW`).

The heading is `HDT`, else `HDG` with a variation, else `VHW`'s true heading,
else the host's. Speed and course over the ground are `RMC` or `VTG`, else
the host's; below 0.2 kn the heading stands in for the course. Stream values
older than 10 seconds aren't combined with the wind. When none of the four can
be had, the feature carries the relative wind only
(`OwnShipWeather.IsRelativeOnly`): it's drawn grey from heading plus the
relative angle when the heading is known, and labelled `Rel. wind`.

## Drawing

`OwnShipWeatherPortrayal` draws a small ring 8 mm right of and above own ship,
so it clears the own-ship symbol, with the WMO barb (pennants 50 kn, full
barbs 10 kn, half barbs 5 kn) growing from it into the wind, rotated in the
geographic frame, and labels for the wind (`200° 14 kn`) and water
temperature (`Water 12.4 °C`). The barb is picked with
`WindBarbSymbols.For` at own ship's latitude: southern-hemisphere barbs
(`WXBARBS*`) south of the equator, northern ones (`WXBARBN*`) on and north of
it, as for gridded and NDBC barbs. Register a
`PortrayedDynamicFeatureRenderer` over `OwnShipWeatherPortrayal.Instance`
under the renderer key `weather.observation.ownship` (see
[`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/README.md)).
The source's metadata places it on S-98 `OtherChartOverlays` at
within-plane priority 110, above NDBC observations.

## Main entry points

| Type | Purpose |
| --- | --- |
| `NmeaWeatherSource` | The `IDynamicFeatureSource` and `IDynamicFeatureDescriber`. Keeps the latest value of each sentence, drops values after 1 minute without an update, publishes at most once a second, and counts accepted and rejected lines. |
| `NmeaWeatherSourceOptions` | Id, display name, motion age, drop age, publish interval, retry delay, parse options. |
| `OwnShipWeather` / `OwnShipReference` | The weather at own ship; the host's own-ship fix. |
| `OwnShipWeatherPortrayal` | The glyph as drawing instructions. Implements `IDynamicFeaturePortrayal`. |
| `NmeaWeatherFormatter` | The pick-report rows (true wind and its basis, relative wind, heading, temperatures, pressure, update time) and chart labels. |

## See also

- [Wind and wave data](../../docs/design/weather-sources.md), §6.2: the
  design note.
- [EncDotNet.S100.DynamicSources.Ndbc](../EncDotNet.S100.DynamicSources.Ndbc/README.md):
  buoy observations with the same barbs.
