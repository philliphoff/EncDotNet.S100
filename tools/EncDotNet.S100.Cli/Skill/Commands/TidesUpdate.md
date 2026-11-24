#### Agent guidance

Downloads NOAA CO-OPS's tide and current stations into the offline catalogue
the other `tides` commands and `s100 mcp serve` read. By default it downloads
one file of about 1.2 MB from the EncDotNet.S100 mirror, which refreshes it
from NOAA every week, in a few seconds; when the mirror can't be used it says
so and downloads from NOAA instead. `--from noaa` reads NOAA's metadata API
directly: about 3,400 paced requests, roughly 10 minutes; use it only when
you need data newer than the mirror's. A `--from` value never falls back.
Run it once, then again when `--check` exits with code 8 (there's newer data)
or the catalogue is over 12 months old. `--check` downloads nothing: against
the mirror it makes one request, against NOAA a few.
s100 ships a copy of the catalogue from the mirror at build time, used when
the default cache is missing or older, so the other `tides` commands work on
first use; run `tides update` only to get anything newer.
