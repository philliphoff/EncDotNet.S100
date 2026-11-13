#### Agent guidance

Lists tidal current stations round `--near latitude,longitude` (latitude
first), nearest first. Use it to find a station id for
`s100 tides currents predict`. NOAA lists each depth bin as its own station,
such as `PUG1701_33`, so one place can appear several times with different
depths. `--format json` is the `list_current_stations` MCP result: each
station's `kind` (`harmonic`, or `subordinate` with `referenceStationId` and
`offsets`), `distanceMeters`, `depthMeters`, flood and ebb directions (degrees
true, where the current sets) and `isRotary`, and a `catalogue` object with
its date, `ageDays` and `stale`.

It reads the offline catalogue and needs no network. With no catalogue it
exits with code 2 and says to run `s100 tides update` first. An older
catalogue with no current stations lists none: run `s100 tides update` again.
