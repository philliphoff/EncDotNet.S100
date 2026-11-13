# Find when the tide is above a height

This guide answers "When is the tide above 2 m at Seattle tomorrow afternoon?"
from the predicted tide, first with the `s100` command line and then by asking
an AI agent over MCP. Use it when you need a time window from a height, such
as when there's enough water over a bar or under a bridge, and no S-104 water
level covers the place.

The heights are astronomical predictions from NOAA's harmonic constituents.
They leave out wind, pressure and river flow, which can move the water 0.5 m
or more. They aren't a forecast and aren't for navigation.

## Prerequisites

- The `s100` command-line tool. See [Install](../cli.md#install).
- About 10 minutes of network access the first time, to download NOAA's tide
  stations. After that, everything works offline.

## Steps

1. Download the station catalogue:

   ```bash
   s100 tides update
   ```

   It's kept in your local application data, where the other `tides`
   commands and `s100 mcp serve` find it. See
   [`tides update`](../cli.md#tides-update).

2. Find the station. Seattle's is NOAA 9447130, a harmonic station on the
   waterfront:

   ```bash
   s100 tides stations --near 47.6,-122.34 --limit 3
   ```

   A **harmonic** station is predicted from its own constituents. A
   **subordinate** station, such as Tacoma Narrows Bridge, is predicted from a
   reference station with time and height differences.

3. Predict the afternoon at a 10-minute step. Without an offset, times are in
   your computer's time zone; this guide's are Pacific time, and "tomorrow" is
   18 November 2026:

   ```bash
   s100 tides predict Seattle --from 2026-11-18T12:00 --to 2026-11-18T18:00 --step 10m
   ```

   Heights are in metres above mean lower low water (MLLW), the chart datum
   of US charts. Part of the output:

   ```text
   SEATTLE (Madison St.), Elliott Bay 9447130, harmonic, predicted on MLLW
   ╭─────────────────────────┬──────────────────────────╮
   │ Time                    │ Predicted (m above MLLW) │
   ├─────────────────────────┼──────────────────────────┤
   │ 2026-11-18 12:00 -08:00 │                     3.42 │
   │ ...                     │                      ... │
   │ 2026-11-18 16:00 -08:00 │                     2.13 │
   │ 2026-11-18 16:10 -08:00 │                     2.06 │
   │ 2026-11-18 16:20 -08:00 │                     1.99 │
   │ 2026-11-18 16:30 -08:00 │                     1.92 │
   │ ...                     │                      ... │
   │ 2026-11-18 18:00 -08:00 │                     1.48 │
   ╰─────────────────────────┴──────────────────────────╯
   ```

4. Read where the heights cross 2 m. The tide is above 2 m from noon until
   between 16:10 and 16:20, after high water at 11:55. The highs and lows
   show the shape of the day:

   ```bash
   s100 tides predict Seattle --highs-lows --from 2026-11-18 --to 2026-11-19
   ```

   ```text
   │ 2026-11-18 04:35 -08:00 │ Low      │                     0.80 │
   │ 2026-11-18 11:55 -08:00 │ High     │                     3.42 │
   │ 2026-11-18 18:55 -08:00 │ Low      │                     1.41 │
   ```

## Result

Tomorrow afternoon, the predicted tide at Seattle is above 2 m above MLLW
until about 16:18, then falls to 1.41 m at 18:55. Allow a margin for the
weather: a strong southerly or low pressure can raise the water, and a high
can lower it.

## Ask an agent

The same answer is available to an AI agent through the MCP tools
`list_tide_stations` and `predict_tide`, in SoundCharts or `s100 mcp serve`.
See [Run the headless server](../mcp-server.md#run-the-headless-server) to
connect one, then ask:

> When is the tide above 2 m at Seattle tomorrow afternoon?

The agent calls `predict_tide` with the station and a window, for example:

```json
{
  "station": "Seattle",
  "from": "2026-11-18T12:00:00-08:00",
  "to": "2026-11-18T18:00:00-08:00",
  "stepMinutes": 10
}
```

The result names the station and datum, and carries a `caveat` the agent
should quote with its answer. Times are in UTC:

```json
{
  "station": { "id": "9447130", "name": "SEATTLE (Madison St.), Elliott Bay", "type": "tide", "kind": "harmonic" },
  "kind": "prediction",
  "datum": "MLLW",
  "verticalDatum": 12,
  "unit": "m",
  "stepSeconds": 600,
  "series": [
    { "time": "2026-11-18T20:00:00+00:00", "height": 3.421 },
    { "time": "2026-11-19T00:10:00+00:00", "height": 2.058 },
    { "time": "2026-11-19T00:20:00+00:00", "height": 1.986 }
  ],
  "caveat": "Astronomical tide only, not a forecast."
}
```

The series is shortened here. [Tide tools](../mcp-server.md#tide-tools) lists
every field.

## Other places and datums

- **Near a point.** `--near 47.27,-122.55` (or `latitude` and `longitude` in
  `predict_tide`) uses the nearest station within 10 km that can be predicted
  on the datum.
- **Another datum.** `--datum LAT` gives heights above lowest astronomical
  tide, at harmonic stations that have it. Subordinate stations are MLLW only.
- **An S-104 file.** `--format s104 -o seattle.h5` writes the series as an
  S-104 station dataset to open in SoundCharts. See
  [Write an S-104 dataset](../cli.md#write-an-s-104-dataset).
