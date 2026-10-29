# EncDotNet.S100.DynamicSources.Ndbc

`EncDotNet.S100.DynamicSources.Ndbc` shows NOAA National Data Buoy Center
(NDBC) observations on a chart: the latest wind, waves, pressure and
temperatures from the moored buoys and C-MAN stations in view.
`NdbcObservationSource` implements `IDynamicFeatureSource` from
[`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md), and
`NdbcStationPortrayal` draws each station as a station-model glyph with the
code-defined portrayal toolkit in
[`EncDotNet.S100.Portrayals`](../EncDotNet.S100.Portrayals/README.md). The
library has no UI or Mapsui dependency. SoundCharts uses it.

Not for navigation.

## Install

This package isn't published to NuGet. Reference the project directly:

```xml
<ProjectReference Include="path/to/src/EncDotNet.S100.DynamicSources.Ndbc/EncDotNet.S100.DynamicSources.Ndbc.csproj" />
```

## Example: observations for the visible area

```csharp
using EncDotNet.S100.DynamicSources.Ndbc;
using EncDotNet.S100.Pipelines;

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("MyApp/1.0 (+https://example.org)");

await using var ndbc = new NdbcObservationSource(http);
ndbc.Changed += (_, _) => Console.WriteLine($"{ndbc.CurrentFeatures.Count} stations");

// South latitude, west longitude, north latitude, east longitude.
ndbc.UpdateArea(new BoundingBox(40.5, -71.5, 43.0, -69.5));
ndbc.Start(); // polls every 10 minutes; or call PollAsync on your own schedule
```

Each feature is a point at the station with `Kind`
`weather.observation.ndbc` (or `weather.observation.ndbc.stale`), and its
`NdbcObservation` under the `observation` attribute
(`NdbcObservationSource.GetObservation`).

## Main entry points

| Type | Purpose |
| --- | --- |
| `NdbcObservationSource` | The `IDynamicFeatureSource`. Fetches the station table once a day and the head of each `realtime2/{station}.txt` file for the meteorological stations in the area plus a margin, every 10 minutes per station, with exponential backoff on errors. Nothing is fetched until `UpdateArea` sets an area, while the area is wider than 25°, or while `IsActive` is false. Marks observations older than 3 hours stale and drops them after 12. Also an `IDynamicFeatureDescriber`, so a pick report lists every value with its unit and the observation time. |
| `NdbcObservationSourceOptions` | Base address, paths, poll interval, ageing, backoff, margin, span gate, per-poll and concurrency limits, and the HTTP `Range` size. |
| `NdbcRealtimeParser` / `NdbcObservation` | The `realtime2` standard meteorological format: wind direction, speed and gust; wave height, dominant and average period and mean direction; pressure, air, water and dew-point temperature, visibility, pressure tendency and tide. `MM` is missing (`null`). `NdbcObservation.Combine` merges a station's newest rows, since buoys report waves less often than wind (half-hourly or hourly, against every 10 minutes). |
| `NdbcStationTable` / `NdbcStation` | The `activestations.xml` station table: ids, positions, types and whether a station reports meteorological data. |
| `NdbcStationPortrayal` | The station-model glyph as drawing instructions: a station marker, the weather styles' WMO wind barb rotated into the wind (the mirrored southern-hemisphere barb for a station south of the equator, through `WindBarbSymbols`), and the wave height as a label. Stale stations are grey. Implements `IDynamicFeaturePortrayal`. |
| `NdbcObservationFormatter` | The pick-report rows for an observation. |

## Draw the stations

The source's `RendererKey` is `weather.observation.ndbc`. With Mapsui,
register a `PortrayedDynamicFeatureRenderer` over `NdbcStationPortrayal.Instance`
under that key (see
[`EncDotNet.S100.Renderers.Mapsui`](../EncDotNet.S100.Renderers.Mapsui/README.md)).
Headless, pass `NdbcStationPortrayal.Instance.Portray(feature)` and a
`DynamicFeatureGeometryProvider` to the Skia renderer. The source's metadata
places it on the S-98 `OtherChartOverlays` plane at within-plane priority
100.

## Network use

The files come from `https://www.ndbc.noaa.gov/` (US government data, public
domain). Each station request asks for the first 16 KiB of its file with an
HTTP `Range` header, about a day of rows. NDBC answers with `206 Partial
Content`, so the last line is usually cut off; the parser skips it, and
stops at rows older than 12 hours either way. Set a `User-Agent` that
identifies your application on the `HttpClient` you pass in. The client
stays yours to dispose.

## See also

- [Wind and wave data](../../docs/design/weather-sources.md), §6.2: the
  design note.
- [AIS dynamic feature source](../../docs/design/ais-source.md): the pattern
  this source follows.
- [Dynamic source pick](../../docs/design/dynamic-source-pick.md).
