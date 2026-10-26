# GRIB2 test fixtures

NOAA model output, public domain (US Government work). Downloaded on
26 October 2026 from NOAA Open Data Dissemination on AWS,
`https://noaa-gfs-bdp-pds.s3.amazonaws.com/`. Each message was read with an
HTTP byte range taken from the file's `.idx` index. The run is 2026-10-25
00Z.

They were built with `tools/grib-fixtures/make_grib_fixtures.py` (ecCodes
2.50.0, the `eccodes` and `eccodeslib` Python wheels). That script also
prints the reference values `NoaaFixtureTests` hardcodes. The cropped files
were cut to 40°N to 32°N and 284°E to 292°E (76°W to 68°W), 33 × 33 points
at 0.25°, off the US East Coast. Their rows still run north to south
(scanning mode 0), and ecCodes repacked them.

| File | Bytes | Source object | Subset |
|---|---|---|---|
| `gfs-1p00-ugrd10m-f003.grib2` | 78,547 | `gfs.20261025/00/atmos/gfs.t00z.pgrb2.1p00.f003` | The `UGRD:10 m above ground` message, byte for byte as NCEP serves it: global 1°, template 5.3, spatial differencing order 2. |
| `gfs-useast-wind10m.grib2` | 6,721 | `gfs.20261025/00/atmos/gfs.t00z.pgrb2.0p25.f000`, `f003`, `f006` | `UGRD` and `VGRD` at 10 m above ground, cropped. Repacked as 5.3 with order 2, the packing NCEP uses. Six messages: U, V at f000, f003, f006. |
| `gfs-useast-templates.grib2` | 5,325 | `gfs.20261025/00/atmos/gfs.t00z.pgrb2.0p25.f003` | Cropped, one message per data template: `GUST:surface` as 5.0, `PRMSL:mean sea level` as 5.2, `TMAX:2 m above ground` (0–3 hour max, product template 4.8) as 5.41 PNG, and `UGRD:10 m above ground` as 5.3 order 1. |
| `gfswave-useast.grib2` | 13,729 | `gfs.20261025/00/wave/gridded/gfswave.t00z.global.0p25.f000`, `f003`, `f006` (`.grib2`) | `HTSGW`, `PERPW` and `DIRPW` at the surface, cropped, with the land bitmap (64 of 1,089 points missing). Repacked from the served JPEG 2000 as 5.3 order 1, 5.2 and 5.0. Nine messages. |
| `gfswave-wcoast-htsgw-f003-jpeg2000.grib2` | 10,288 | `gfs.20261025/00/wave/gridded/gfswave.t00z.wcoast.0p16.f003.grib2` | The `HTSGW:surface` message, byte for byte as NCEP serves it: template 5.40 (JPEG 2000). Used to test that it's reported as unsupported. |

Total: 114,610 bytes.

The NOMADS GRIB filter (`nomads.ncep.noaa.gov`) was blocked from the
environment that built these, so they come from AWS rather than the filter.
To rebuild them, see the script's header. AWS keeps only recent runs, so
pass a current run as `RUN`. The values will then differ, and the tests'
reference values must be updated from the script's output.
