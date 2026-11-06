# EncDotNet.S100.Datasets.TropicalCyclones

`EncDotNet.S100.Datasets.TropicalCyclones` reads tropical cyclone advisories
from the US National Hurricane Center's GIS data
(<https://www.nhc.noaa.gov/gis/>) into one model: the storm, the advisory,
every forecast point with its intensity and wind radii, the cone of
uncertainty, and the storm's track so far.

It depends on [`EncDotNet.S100.Core`](../EncDotNet.S100.Core/README.md) and
NetTopologySuite. The pipeline processor that loads it,
`TropicalCycloneDatasetProcessor`, is in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md),
under the format name `TC`. The portrayal, `TropicalCycloneStyle`, is in
[`EncDotNet.S100.Datasets.Weather`](../EncDotNet.S100.Datasets.Weather/README.md#tropical-cyclones).

> **Not for navigation.** NHC advisories aren't S-100 products or official
> navigational warnings. Hosts label them so wherever they appear.

The design is in the vector weather design note
(`docs/design/vector-weather.md`, §8.1 and §3.1).

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.TropicalCyclones
```

## Read an advisory

NHC publishes each advisory as three shapefile zips. Download them into one
folder:

| Zip | What it holds | Needed |
|---|---|---|
| `al092022_5day_020.zip` | Forecast points, track line and cone | Yes |
| `al092022_fcst_020.zip` | 34, 50 and 64 kt wind radii for each forecast time | No |
| `al092022_best_track.zip` | The storm's past track and observed radii | No |

Then read the forecast zip. The reader finds the other two beside it:

```csharp
using EncDotNet.S100.Datasets.TropicalCyclones;

var advisory = NhcAdvisoryReader.Read("al092022_5day_020.zip");

Console.WriteLine($"{advisory.Storm.Name} ({advisory.Storm.AtcfId}), advisory {advisory.Advisory}");
Console.WriteLine($"Issued {advisory.IssueTime:u}");

foreach (var point in advisory.Forecast)
{
    Console.WriteLine($"+{point.Tau} h {point.ValidTime:u}: {point.Position}, {point.MaxWindKnots} kt, {point.Classification}");
    foreach (var radius in point.Radii)
        Console.WriteLine($"  {radius.ThresholdKnots} kt: NE {radius.NortheastNauticalMiles} nm, SE {radius.SoutheastNauticalMiles} nm, ...");
}
```

You can also pass a folder holding one storm's zips. The reader takes its
latest advisory, so `20A` comes after `20`. To read zips that aren't on disk,
pass their streams to `NhcAdvisoryReader.Read(forecast, windRadii, bestTrack)`.
The last two can be `null`.

## What the model holds

`TropicalCycloneAdvisory` holds:

- **Storm.** The ATCF id (`AL092022`), the name, and the basin, number and
  year the id gives.
- **Advisory.** The number as NHC writes it (`"20"`, or `"20A"` for an
  intermediate advisory), and the issue time. The issue time keeps the offset
  of NHC's local time, such as EDT. Hosts show it in the viewer's own zone.
- **Forecast.** One `TropicalCycloneForecastPoint` per forecast time, in tau
  order. Each has the valid time in UTC, the position, the maximum wind and
  gusts, and the class (`TropicalCycloneClass`, with NHC's code and letter).
  The point at tau 0 also has the pressure and the storm's motion. Values
  NHC doesn't forecast are `null`.
- **Wind radii.** Each forecast point's 34, 50 and 64 kt radii, as nautical
  miles in each quadrant (NE, SE, SW, NW) and NHC's polygon. A radius whose
  quadrants are all 0 is left out, so a tropical depression has none.
  `QuadrantSurface(centre, pointsPerQuadrant)` builds the area from the four
  quadrants, a quarter circle each, for a radius that has no polygon of NHC's.
- **Cone.** `TropicalCycloneCone` holds NHC's polygon unchanged, and the same
  area split at 72 hours into `DaysOneToThree` and `DaysFourToFive`. It is
  `null` when the advisory has no cone, as some final advisories don't.
- **Past track.** The best-track points up to the advisory's synoptic time,
  with the intensity, class and observed radii at each. A best track
  downloaded later holds the storm's whole life, so points after the advisory
  are left out.

All positions are WGS-84 degrees. NHC computes on a sphere, and the
difference is far smaller than the data's precision.

### How the cone is split

NHC's GIS files give only the whole five-day cone. The split that the chart
draws (days 1 to 3 plain, days 4 and 5 stippled) is made by cutting the cone
with a line through the 72-hour position, square to the track there. This
matches NHC's own graphic for any track that doesn't double back within the
cone. When the forecast ends at or before 72 hours, the whole cone is days 1
to 3.

## Field names

The reader uses these fields, checked against NHC's archived files for 2022
and 2023:

| Layer | Fields |
|---|---|
| `_5day_pts` | `ADVDATE`, `ADVISNUM`, `FCSTPRD`, `TAU`, `VALIDTIME`, `LAT`, `LON`, `MAXWIND`, `GUST`, `MSLP`, `STORMTYPE`, `DVLBL`, `TCDVLP`, `SSNUM`, `TCDIR`, `TCSPD`, `STORMNAME` |
| `_5day_lin`, `_5day_pgn` | `STORMNAME` |
| `_forecastradii`, `_initialradii` | `TAU`, `RADII`, `NE`, `SE`, `SW`, `NW`, `STORMID`, `VALIDTIME` |
| Best track `_pts` | `YEAR`, `MONTH`, `DAY`, `HHMM`, `DTG`, `INTENSITY`, `MSLP`, `STORMTYPE`, `SS`, `STORMNAME`, `LAT`, `LON` |
| Best track `_radii` | `SYNOPTIME`, `RADII`, `NE`, `SE`, `SW`, `NW` |

NHC writes 9999 for a value it leaves out, and the reader returns `null` for
it. The watches and warnings layer (`_ww_wwlin`) and the best track's wind
swath aren't read yet.

The shapefiles are read by a small managed SHP and DBF reader in this
package. It reads points, lines and polygons, including their Z and M forms.
It isn't public API.
