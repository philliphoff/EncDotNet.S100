# EncDotNet.S100.Tides.Stations

`EncDotNet.S100.Tides.Stations` is the station catalogue for
[`EncDotNet.S100.Tides`](../EncDotNet.S100.Tides/README.md). It reads NOAA
CO-OPS's tide stations with their harmonic constituents, tidal datums and
subordinate-station offsets, keeps them in a JSON file that works offline,
finds stations near a point or in a box, and creates a station's predictor
on a chosen S-100 vertical datum with the provenance to label it. Reference
it when you need a predicted water level where NOAA has a tide station.

The package is pure managed code, and is trimming and NativeAOT compatible.
Times are UTC and heights are metres. Predictions are astronomical only and
aren't for navigation.

## Install

```bash
dotnet add package EncDotNet.S100.Tides.Stations
```

## Download NOAA's stations

`NoaaTideStationClient` reads the
[CO-OPS metadata API](https://api.tidesandcurrents.noaa.gov/mdapi/prod/).
Pass it your `HttpClient`, which you own along with its timeout and
`User-Agent`:

```csharp
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Noaa;

using var http = new HttpClient();
var client = new NoaaTideStationClient(http);

var catalogue = await client.DownloadCatalogueAsync(
    new Progress<NoaaDownloadProgress>(p => Console.WriteLine($"{p.CompletedStations}/{p.TotalStations}")));
await TideStationCatalogueFile.SaveAsync("noaa-tide-stations.json.gz", catalogue);
```

A download reads the station list, then each reference station's
constituents and datums: about 2,500 requests. The API refuses a client that
asks too fast (HTTP 403 for some minutes), so the client asks at most two at
a time with a pause after each, and retries a refusal after 5, 10, 20 and 40
seconds. A full download took 7 minutes 44 seconds in November 2026. Change
the pace with `NoaaTideStationClientOptions`.

The November 2026 catalogue has 3,502 stations: 1,260 reference
(harmonic) stations and 2,242 subordinate stations. 3,496 of them can be
predicted on MLLW. Of the other six, five are reference stations NOAA gives
no constituents or no MLLW for, and one is a subordinate station of one of
them.

## Load it offline

The saved file is the offline cache. Loading it needs no network, and takes
about 70 ms for NOAA's whole catalogue:

```csharp
var catalogue = await TideStationCatalogueFile.LoadAsync("noaa-tide-stations.json.gz");
Console.WriteLine($"{catalogue.Provider}, {catalogue.Retrieved:d}, epoch {catalogue.DatumEpoch}");
```

`SaveAsync` gzips when the path ends in `.gz` and replaces the file in one
step, so a reader never sees half a file. NOAA's catalogue is 2.7 MB as JSON
and 362 KB gzipped (259 KB with xz).

## Check for newer data

Newer NOAA data, not age, makes a catalogue stale. `CheckForUpdatesAsync`
finds out cheaply: it reads the station list (one request, about 140 KB
compressed) and the datums of five long-running stations, not every
station's constants:

```csharp
var check = await client.CheckForUpdatesAsync(catalogue);
if (check.HasNewerData)
{
    // check.NewDatumEpoch, AddedStationIds, RemovedStationIds,
    // ChangedStationIds (moved or new offsets), RevisedDatumStationIds
}
```

It reports a new tidal datum epoch (NOAA is replacing 1983–2001 with
2002–2020 in one national release, which moves most chart-datum
predictions by a few centimetres), stations added, removed or moved,
changed subordinate offsets, and datums NOAA has accepted again. Each
station keeps its datum epoch and the date its datums were accepted
(`StationDatums.Epoch` and `Accepted`). With no network to check,
`catalogue.IsOlderThan(TideStationCatalogue.OfflineStaleAfter, now)` falls
back to 12 months.

## Find stations

```csharp
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

var nearest = catalogue.FindNearest(new GeoPosition(47.27, -122.55), maxCount: 5, maxDistanceMeters: 20_000);
var inView = catalogue.FindWithin(new BoundingBox(47.0, -123.0, 48.2, -122.2));
```

Both return `TideStationMatch` values, nearest first, with the geodesic
distance on WGS-84. `FindWithin` ranks from the box's centre unless you pass
a point, and a box whose west edge is east of its east edge crosses the
antimeridian. `Find` looks a station up by its identifier.

## Predict a station

`TryCreatePrediction` gives a station's predictor on an S-100 vertical
datum, with where its predictions come from:

```csharp
var prediction = catalogue.TryCreatePrediction("9446486", VerticalDatum.MeanLowerLowWater);
if (prediction is not null)
{
    var height = prediction.Predictor.HeightAt(DateTime.UtcNow);
    var source = prediction.Source;
    // "Tacoma Narrows Bridge", Subordinate, MLLW,
    // "from SEATTLE (Madison St.), Elliott Bay 9447130, with offsets"
    Console.WriteLine($"{source.StationName}: {height:F2} m above {source.DatumName}, {source.ReferenceDescription}");
}
```

- A **harmonic** station is predicted from its constituents, about mean sea
  level plus the height of MSL above the datum. It can be given on MSL and
  on any datum NOAA lists for it: MLLW, MLW, MHW, MHHW, and LAT and HAT
  where given. `GetAvailableDatums` lists them.
- A **subordinate** station is predicted by NOAA's method (see
  [`SubordinateTidePredictor`](../EncDotNet.S100.Tides/README.md#predict-a-subordinate-station))
  from its reference station, and only on MLLW, the datum NOAA's offsets
  apply on.

It returns `null` when the station isn't in the catalogue or can't be
predicted on that datum. `TideDatumNames` maps NOAA's datum names to S-100
`VerticalDatum` codes (MLLW is 12, LAT is 23).

## Tidal current stations

The catalogue also holds NOAA's tidal current prediction stations. A current
is measured at a depth, so a station is NOAA's identifier and depth bin,
`PUG1701_33`, as NOAA writes it. `DownloadCatalogueAsync` reads them unless
`NoaaTideStationClientOptions.IncludeCurrentStations` is off: the current
station list (one request) and each harmonic current station's constituents
(about 900 more requests). Weak and variable stations (NOAA type `W`) have
no predictions and are left out.

```csharp
var near = catalogue.FindNearestCurrentStations(new GeoPosition(48.41, -122.64), maxCount: 3);
var prediction = catalogue.TryCreateCurrentPrediction("PUG1701_33");
if (prediction is not null)
{
    var current = prediction.Predictor.CurrentAt(DateTime.UtcNow);
    var events = prediction.Predictor.FindEvents(DateTime.UtcNow, DateTime.UtcNow.AddDays(1));
    // "Deception Pass (Narrows), 5.49 m deep: 2.1 kn toward 108°"
    Console.WriteLine($"{prediction.Source.StationName}, {prediction.Source.DepthMeters} m deep: {current.SpeedKnots:F1} kn toward {current.DirectionDegrees:F0}°");
}
```

- A **harmonic** current station has its major- and minor-axis
  constituents for its bin, the mean currents, the major axis and NOAA's
  flood and ebb directions. Where NOAA gives no directions, the major axis
  stands in. A station whose M2 current ellipse is at least half as wide as
  long (`NoaaTideStationClient.RotaryEllipseRatio`) is marked rotary and
  predicted from both axes; NOAA's own predictions give only the major axis.
- A **subordinate** current station has its reference station and bin, its
  time differences for slack before flood, maximum flood, slack before ebb
  and maximum ebb, and its flood and ebb speed ratios, and is predicted by
  NOAA's method (see the
  [Tides README](../EncDotNet.S100.Tides/README.md#predict-tidal-currents)).

`FindCurrentStationsWithin` and `FindCurrentStation` work as for tide
stations, and `CurrentPredictionSource` says where a prediction comes from:
kind, depth, rotary or not, and for a subordinate station "from Golden Gate
Bridge, 0.88 nm NE of SFB1202_17, with offsets". `CheckForUpdatesAsync`
checks the current station list too when the catalogue has current
stations.

## Bring your own stations

The cache file format is also the way to add stations from another source.
Write a JSON file like this and load it with `TideStationCatalogueFile.LoadAsync`:

```json
{
  "format": "tide-stations",
  "version": 1,
  "provider": "Example harbour authority",
  "stations": [
    {
      "id": "example-harbour",
      "name": "Example Harbour",
      "latitude": 47.6026,
      "longitude": -122.3393,
      "utcOffsetHours": -8,
      "kind": "harmonic",
      "phaseReference": "local",
      "constituents": [
        { "name": "M2", "amplitude": 1.063, "phase": 138.9 },
        { "name": "K1", "amplitude": 0.834, "phase": 337.4 }
      ],
      "datums": {
        "epoch": "1983-2001",
        "accepted": "2003-04-17",
        "heights": { "MSL": 4.443, "MLLW": 2.419, "MHHW": 5.882 }
      }
    },
    {
      "id": "example-cove",
      "name": "Example Cove",
      "latitude": 47.2717,
      "longitude": -122.552,
      "kind": "subordinate",
      "reference": "example-harbour",
      "offsets": {
        "datum": "MLLW",
        "highWaterMinutes": 28,
        "lowWaterMinutes": 23,
        "highWaterHeight": 1.11,
        "lowWaterHeight": 1.02,
        "heightAdjustment": "ratio"
      }
    }
  ]
}
```

| Member | Required | Meaning |
|---|---|---|
| `format` | yes | Always `tide-stations`. |
| `version` | yes | `1`. |
| `provider` | no | Who the stations come from, shown with every prediction. |
| `retrieved` | no | When they were retrieved, as an ISO 8601 time with offset. |
| `stations[].id` | yes | A unique identifier. |
| `stations[].name` | no | The station's name; the identifier if left out. |
| `stations[].latitude`, `longitude` | yes | Decimal degrees on WGS-84. |
| `stations[].region` | no | A region, such as a US state. |
| `stations[].utcOffsetHours` | no | The offset from UTC of the station's standard time zone, positive east. |
| `stations[].kind` | yes | `harmonic` or `subordinate`. |
| `stations[].phaseReference` | no | `greenwich` (the default) or `local`, when phases are referred to `utcOffsetHours`. |
| `stations[].constituents[]` | harmonic | `name` (one of `HarmonicConstituents.Names`), `amplitude` in metres, `phase` in degrees. |
| `stations[].datums` | no | `heights`: each datum's height above a common reference, in metres, by name (`MSL`, `MLLW`, `MLW`, `MHW`, `MHHW`, `LAT`, `HAT`); a harmonic station needs `MSL` for any datum but MSL. Optional `epoch` and `accepted` (`yyyy-MM-dd`). |
| `stations[].reference` | subordinate | The reference station's `id`, a harmonic station in the same file. |
| `stations[].offsets` | subordinate | `highWaterMinutes` and `lowWaterMinutes` (positive when later than the reference), `highWaterHeight` and `lowWaterHeight`, `heightAdjustment` (`ratio` to multiply, `offset` to add metres), and `datum` (the datum they apply on; `MLLW` if left out). |

A file that breaks these rules fails to load with a `FormatException` that
names the station and the problem. Check the terms of any constituents you
use: some national offices license theirs rather than publishing them
openly.

## Terms

NOAA's station data is a work of the US Government and in the public
domain. Predictions are astronomical only: they leave out the weather, river
flow and other non-tidal effects on the water level, which can move it
0.5 m or more, and aren't for navigation.
