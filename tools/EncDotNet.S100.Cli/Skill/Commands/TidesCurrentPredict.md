#### Agent guidance

Predicts the astronomical tidal current at a current station named by id or
name, or at the nearest station to `--near latitude,longitude`. A NOAA station
id without its depth bin (`PUG1701`) or a name shared by several bins takes
the shallowest bin. Prefer `--format json`: it's the `predict_current` MCP
result, with times in UTC, speeds in knots, directions in degrees true (where
the current sets), `phase` (`flood` or `ebb`), the station with its depth and
flood and ebb directions, `kind: "prediction"` and a `caveat`. `--events`
returns the slacks and maximum and minimum floods and ebbs instead of a
series; use it to answer "when is slack water".

`--from` and `--to` without an offset are in this machine's time zone (UTC
with `--utc`); give an offset when the place's zone differs.

Quote the caveat with any value: wind and river flow change real currents,
and the strongest stream in a pass can be away from the station. It isn't
S-111 and isn't for navigation. `--format s111 -o file.h5` writes the series
as an S-111 station dataset with producer `ZZ01`, which marks it as not an
official product.
