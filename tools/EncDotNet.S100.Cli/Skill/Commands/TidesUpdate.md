#### Agent guidance

Downloads NOAA CO-OPS's tide and current stations into the offline catalogue
the other `tides` commands and `s100 mcp serve` read. `--from noaa` (the
default) reads NOAA's metadata API directly: about 3,400 paced requests,
roughly 10 minutes. `--from mirror` downloads the same catalogue as one file
of about 1.2 MB from the EncDotNet.S100 mirror, which refreshes it from NOAA
every week; prefer it unless you need data straight from NOAA. Run it once,
then again when `--check` exits with code 8 (there's newer data) or the
catalogue is over 12 months old. `--check` downloads nothing: against NOAA it
makes a few requests, against a mirror one.
