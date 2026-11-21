#### Agent guidance

Downloads NOAA CO-OPS's tide and current stations (about 2,100 requests,
roughly 10 minutes) into the offline catalogue the other `tides` commands and
`s100 mcp serve` read. Run it once, then again when `--check` exits with
code 8 (NOAA has newer data) or the catalogue is over 12 months old. `--check`
makes a few requests and downloads nothing.
