# Find slack water in a pass

This guide answers "When is slack water at Deception Pass tomorrow
afternoon?" from the predicted tidal current, first with the `s100` command
line and then by asking an AI agent over MCP. Use it to time a passage through
a narrow pass, where the stream runs too hard to stem for much of the tide,
and no S-111 surface current data covers the place.

The currents are astronomical predictions from NOAA's harmonic constituents.
Wind and river flow change real currents, and the strongest stream in a pass
can be away from the station. They aren't a forecast and aren't for
navigation.

## Prerequisites

- The `s100` command-line tool. See [Install](../cli.md#install).
- About 10 minutes of network access the first time, to download NOAA's tide
  and current stations. After that, everything works offline.

## Steps

1. Download the station catalogue, if you haven't already:

   ```bash
   s100 tides update
   ```

   It holds NOAA's tide and current stations. See
   [`tides update`](../cli.md#tides-update).

2. Find the current station. NOAA lists each depth at a station as its own
   station, such as `PUG1701_33`, the bin 5.5 m below the surface in the
   Deception Pass narrows:

   ```bash
   s100 tides currents stations --near 48.406,-122.643 --limit 3
   ```

   ```text
   ╭────────────┬───────────┬──────────┬───────────┬───────┬───────────┬──────────╮
   │ Id         │ Name      │ Kind     │ Reference │ Depth │ Flood/ebb │ Distance │
   ├────────────┼───────────┼──────────┼───────────┼───────┼───────────┼──────────┤
   │ PUG1701_33 │ Deception │ harmonic │ —         │ 5.5 m │ 108°/277° │   0.0 km │
   │            │ Pass      │          │           │       │           │          │
   │            │ (Narrows) │          │           │       │           │          │
   ╰────────────┴───────────┴──────────┴───────────┴───────┴───────────┴──────────╯
   ```

   **Flood/ebb** gives the directions the current sets toward: here the flood
   runs east into Skagit Bay and the ebb west to the strait.

3. List the slacks and maxima for the day. Without an offset, times are in
   your computer's time zone; this guide's are Pacific time, and "tomorrow"
   is 18 November 2026. A name or NOAA's id without its bin (`PUG1701`) takes
   the shallowest bin:

   ```bash
   s100 tides currents predict "Deception Pass" --events --from 2026-11-18 --to 2026-11-19
   ```

   ```text
   Deception Pass (Narrows) PUG1701_33, harmonic, 5.5 m below the surface, flood
   108°, ebb 277°
   ╭─────────────────────────┬─────────────────────┬────────────────┬───────────╮
   │ Time                    │ Event               │ Predicted (kn) │ Direction │
   ├─────────────────────────┼─────────────────────┼────────────────┼───────────┤
   │ 2026-11-18 00:02 -08:00 │ Max ebb             │            4.0 │      277° │
   │ 2026-11-18 03:40 -08:00 │ Slack, flood begins │            0.0 │         — │
   │ 2026-11-18 06:17 -08:00 │ Max flood           │            4.3 │      108° │
   │ 2026-11-18 09:44 -08:00 │ Slack, ebb begins   │            0.0 │         — │
   │ 2026-11-18 12:42 -08:00 │ Max ebb             │            5.5 │      277° │
   │ 2026-11-18 17:00 -08:00 │ Slack, flood begins │            0.0 │         — │
   │ 2026-11-18 19:21 -08:00 │ Max flood           │            3.0 │      108° │
   │ 2026-11-18 22:37 -08:00 │ Slack, ebb begins   │            0.0 │         — │
   ╰─────────────────────────┴─────────────────────┴────────────────┴───────────╯
   ```

4. Look at the speeds round the afternoon slack at a 10-minute step, to see
   how long the stream stays weak:

   ```bash
   s100 tides currents predict PUG1701_33 --from 2026-11-18T16:30 --to 2026-11-18T17:30 --step 10m
   ```

   ```text
   │ 2026-11-18 16:30 -08:00 │            1.2 │      277° │ Ebb       │
   │ 2026-11-18 16:40 -08:00 │            0.8 │      277° │ Ebb       │
   │ 2026-11-18 16:50 -08:00 │            0.4 │      277° │ Ebb       │
   │ 2026-11-18 17:00 -08:00 │            0.0 │      108° │ Flood     │
   │ 2026-11-18 17:10 -08:00 │            0.4 │      108° │ Flood     │
   │ 2026-11-18 17:20 -08:00 │            0.8 │      108° │ Flood     │
   │ 2026-11-18 17:30 -08:00 │            1.2 │      108° │ Flood     │
   ```

## Result

Tomorrow afternoon, the predicted slack at Deception Pass is at 17:00, when
the ebb, which peaks at 5.5 kn at 12:42, turns to flood. The stream is under
1 kn from about 16:40 to 17:20. Allow a margin: wind and river flow move the
slack, and the stream in the pass can turn earlier or later than at the
station.

## Ask an agent

The same answer is available to an AI agent through the MCP tools
`list_current_stations` and `predict_current`, in SoundCharts or
`s100 mcp serve`. See
[Run the headless server](../mcp-server.md#run-the-headless-server) to
connect one, then ask:

> When is slack water at Deception Pass tomorrow afternoon?

The agent calls `predict_current` with the station, a window and the events
mode, for example:

```json
{
  "station": "Deception Pass",
  "from": "2026-11-18T12:00:00-08:00",
  "to": "2026-11-18T18:00:00-08:00",
  "mode": "events"
}
```

The result names the station, its depth and its flood and ebb directions,
and carries a `caveat` the agent should quote with its answer. Times are in
UTC:

```json
{
  "station": { "id": "PUG1701_33", "name": "Deception Pass (Narrows)", "type": "current", "kind": "harmonic", "depthMeters": 5.49, "floodDirectionDegrees": 108, "ebbDirectionDegrees": 277, "isRotary": false },
  "kind": "prediction",
  "unit": "kn",
  "events": [
    { "time": "2026-11-18T20:42:00+00:00", "kind": "max_ebb", "speedKnots": 5.47, "directionDegrees": 277 },
    { "time": "2026-11-19T01:00:00+00:00", "kind": "slack_before_flood", "speedKnots": 0 }
  ],
  "caveat": "Astronomical tidal current only, not a forecast."
}
```

[Tidal current tools](../mcp-server.md#tidal-current-tools) lists every
field.

## Other places and outputs

- **Near a point.** `--near 48.4,-122.64` (or `latitude` and `longitude` in
  `predict_current`) uses the nearest current station within 10 km, the
  shallowest bin where several share a place.
- **Subordinate stations.** A **subordinate** station, such as the one 0.8
  mile east of the Golden Gate Bridge (`PCT0281_1`), is predicted from a
  reference station with NOAA's time differences and speed ratios.
- **An S-111 file.** `--format s111 -o deception-pass.h5` writes the series
  as an S-111 station dataset to open in SoundCharts. See
  [Write an S-111 dataset](../cli.md#write-an-s-111-dataset).
- **Sampling.** With `s100 mcp serve --tide-predictions`, `sample_coverage`
  for `S-111` gives the predicted current where no S-111 covers the point.
  See [Predicted values in sampling](../mcp-server.md#predicted-values-in-sampling).
