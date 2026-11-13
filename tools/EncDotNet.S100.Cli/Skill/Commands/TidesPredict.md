#### Agent guidance

Predicts the astronomical tide at a station named by id or name, or at the
nearest station to `--near latitude,longitude`. Prefer `--format json`: it's
the `predict_tide` MCP result, with times in UTC, heights in metres above the
datum (MLLW unless `--datum`), and the station, `kind: "prediction"` and a
`caveat`. `--highs-lows` returns the highs and lows instead of a series.

`--from` and `--to` without an offset are in this machine's time zone (UTC
with `--utc`); give an offset (`2026-11-13T12:00-08:00`) when the place's
zone differs. To answer "when is the tide above X", ask for a series at a
short step (`--step 10m`) over the window and read where it crosses X.

Quote the caveat with any value: a prediction leaves out weather and river
flow, which can move the water 0.5 m or more. It isn't S-104 and isn't for
navigation. `--format s104 -o file.h5` writes the series as an S-104 station
dataset with producer `ZZ01`, which marks it as not an official product.
