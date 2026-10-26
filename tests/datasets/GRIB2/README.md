# GRIB2 test fixtures

NOAA model output, public domain (US Government work). Downloaded on
26 October 2026 from NOAA Open Data Dissemination on AWS,
`https://noaa-gfs-bdp-pds.s3.amazonaws.com/`. Each message was read with an
HTTP byte range taken from the file's `.idx` index. The run is 2026-10-25
00Z.

They were built with `tools/grib-fixtures/make_grib_fixtures.py` (ecCodes
2.50.0, the `eccodes` and `eccodeslib` Python wheels). That script also
prints the reference values `NoaaFixtureTests` and `Jpeg2000GribTests`
hardcode. The cropped files were cut to 40°N to 32°N and 284°E to 292°E
(76°W to 68°W), 33 × 33 points at 0.25°, off the US East Coast. Their rows
still run north to south (scanning mode 0), and ecCodes repacked them.

| File | Bytes | Source object | Subset |
|---|---|---|---|
| `gfs-1p00-ugrd10m-f003.grib2` | 78,547 | `gfs.20261025/00/atmos/gfs.t00z.pgrb2.1p00.f003` | The `UGRD:10 m above ground` message, byte for byte as NCEP serves it: global 1°, template 5.3, spatial differencing order 2. |
| `gfs-useast-wind10m.grib2` | 6,721 | `gfs.20261025/00/atmos/gfs.t00z.pgrb2.0p25.f000`, `f003`, `f006` | `UGRD` and `VGRD` at 10 m above ground, cropped. Repacked as 5.3 with order 2, the packing NCEP uses. Six messages: U, V at f000, f003, f006. |
| `gfs-useast-templates.grib2` | 5,325 | `gfs.20261025/00/atmos/gfs.t00z.pgrb2.0p25.f003` | Cropped, one message per data template: `GUST:surface` as 5.0, `PRMSL:mean sea level` as 5.2, `TMAX:2 m above ground` (0–3 hour max, product template 4.8) as 5.41 PNG, and `UGRD:10 m above ground` as 5.3 order 1. |
| `gfs-useast-jpeg2000.grib2` | 3,181 | `gfs.20261025/00/atmos/gfs.t00z.pgrb2.0p25.f003`, `gfs.20261025/00/wave/gridded/gfswave.t00z.global.0p25.f003.grib2` | Cropped and repacked by ecCodes as 5.40 (ecCodes encodes with OpenJPEG 2.5.2): `UGRD:10 m above ground` lossless and `PRMSL:mean sea level` lossy (target ratio 10, which ecCodes does as the 5/3 transform cut short), each a 33 × 33 image; GFS-Wave `HTSGW:surface` with the land bitmap, a one-row image of 1,025 samples with no decomposition levels; and `GUST:surface` set to a constant 5.5, so 0 bits and an empty Section 7. Four messages. |
| `gfswave-useast.grib2` | 13,729 | `gfs.20261025/00/wave/gridded/gfswave.t00z.global.0p25.f000`, `f003`, `f006` (`.grib2`) | `HTSGW`, `PERPW` and `DIRPW` at the surface, cropped, with the land bitmap (64 of 1,089 points missing). Repacked from the served JPEG 2000 as 5.3 order 1, 5.2 and 5.0. Nine messages. |
| `gfswave-wcoast-f003-jpeg2000.grib2` | 42,850 | `gfs.20261025/00/wave/gridded/gfswave.t00z.wcoast.0p16.f003.grib2` | The `HTSGW:surface`, `PERPW:surface` and `DIRPW:surface` messages (`.idx` lines 5, 6 and 7), byte for byte as NCEP serves them: West Coast 0.16°, 241 × 151 points with the land bitmap (25,350 missing), template 5.40 lossless, 10, 11 and 16 bits. |
| `gfswave-atlocn-htsgw-f003-jpeg2000.grib2` | 30,600 | `gfs.20261025/00/wave/gridded/gfswave.t00z.atlocn.0p16.f003.grib2` | The `HTSGW:surface` message (`.idx` line 5), byte for byte as NCEP serves it: Atlantic 0.16°, 301 × 331 points with the land bitmap (70,212 missing), template 5.40 lossless, 9 bits. |

Total: 180,953 bytes.

Every NCEP JPEG 2000 codestream here (and every one in the five GFS-Wave
grids checked, see the design note's §7.2) was written by JasPer 2.0.25:
one tile, the packed values as a single row, 5 levels of the 5/3 reversible
transform, 64 × 64 code-blocks with no style flags, LRCP order, one layer.
The global 0.25° messages are too large to commit (300 to 830 KB each); they
were only decoded locally, compared with ecCodes and timed.

The NOMADS GRIB filter (`nomads.ncep.noaa.gov`) was blocked from the
environment that built these, so they come from AWS rather than the filter.
To rebuild them, see the script's header. AWS keeps only recent runs, so
pass a current run as `RUN`. The values will then differ, and the tests'
reference values must be updated from the script's output.

## JPEG 2000 codestreams

`jpeg2000/` holds JPEG 2000 codestreams written by OpenJPEG, an encoder
independent of the managed decoder, for `OpenJpegFixtureTests`. They were
built with `tools/grib-fixtures/make_jpeg2000_fixtures.py`, which drives
OpenJPEG through three Python front ends for the options each exposes:
Pillow 13.0.0 (OpenJPEG 2.5.4), imagecodecs 2026.10.10 (OpenJPEG 2.5.4) and
glymur 0.14.8 (over the system's OpenJPEG 2.5.0). The images are synthetic:
smooth fields with a little noise from a fixed seed.

Each case is `NAME.j2k` (or `.jp2`) and `NAME.raw`, the samples the decoder
must produce, row by row, little-endian, 1, 2 or 4 bytes each by the
precision. `manifest.json` lists every case with its size, precision,
encoder options and tolerance. Lossless cases expect the encoded image
exactly. Lossy cases expect what OpenJPEG decodes, within 1, or 4 for the
16-bit 9/7 case: OpenJPEG scales the 9/7 high-pass samples by
1.625732422 / 2 where T.800 has 1 / K, 3.3 × 10⁻⁵ apart.

| Cases | What they cover |
|---|---|
| `depth-01` to `depth-24`, `depth-12-signed` | Bit depths 1, 4, 8, 12, 16, 20 and 24 (the widest OpenJPEG encodes losslessly), and signed samples. 37 × 19. |
| `size-1x1`, `size-3x5`, `size-37x19`, `size-1x40`, `size-200x1` | Odd and degenerate sizes. |
| `levels-0` to `levels-5` | 0 to 5 decomposition levels, 41 × 33. |
| `tiles-16x16`, `tiles-offset`, `image-offset-odd` | Several tiles, tile offsets and odd image offsets. |
| `progression-*` | LRCP, RLCP, RPCL, PCRL and CPRL with 16 × 16 precincts and three layers; PCRL and RPCL also with tiles, offsets and 8 × 8 or 16 × 8 precincts. |
| `codeblock-4x4`, `codeblock-64x16` | Code-block sizes. |
| `style-*` | Each code-block style flag on its own (bypass, reset, terminate all, vertically causal, predictable termination, segmentation symbols), bypass with terminate all, all six, and all six with the 9/7 transform. |
| `markers-sop-eph`, `markers-plt-tlm`, `subsampling-2x3`, `tile-parts-interleaved` | SOP and EPH markers, PLT and TLM markers, component subsampling (XRsiz 2, YRsiz 3), and tiles split into two tile-parts each with the tile-parts of different tiles interleaved (the script splits them at SOP markers, as none of the front ends writes tile-parts). |
| `layers-lossless`, `lossy-*` | Quality layers with the 5/3 transform, the 9/7 transform at 8 and 16 bits, with layers and with tiles and offsets, and a 5/3 codestream cut short by rate control. |
| `container-jp2` | A JP2 file. |

Total: 51 cases, 162,707 bytes of codestreams and samples, plus the
17,396-byte manifest.

Not covered by an encoder: derived scalar quantisation (OpenJPEG writes
expounded) and samples of 25 to 31 bits (OpenJPEG doesn't encode them
losslessly).
