#### Agent guidance

Lists tide stations round `--near latitude,longitude` (latitude first), nearest
first. Use it to find a station id for `s100 tides predict`. `--format json`
is the `list_tide_stations` MCP result: each station's `kind` (`harmonic`, or
`subordinate` with `referenceStationId` and `offsets`), `distanceMeters`,
`datums` (heights above MLLW) and `predictableDatums`, and a `catalogue`
object with its date, `ageDays` and `stale`.

It reads the offline catalogue and needs no network. With no catalogue it
exits with code 2 and says to run `s100 tides update` first.
