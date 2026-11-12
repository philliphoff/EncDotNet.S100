#### Agent guidance

Downloads NOAA CO-OPS's tide stations (about 2,500 requests, roughly 8
minutes) into the offline catalogue the other `tides` commands, `s100 mcp
serve` and SoundCharts read. Run it once, then again when `--check` exits with
code 8 (NOAA has newer data) or the catalogue is over 12 months old. `--check`
makes a few requests and downloads nothing.
