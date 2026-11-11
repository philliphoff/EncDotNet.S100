# Tropical cyclone test fixtures

NHC GIS shapefile zips for the tropical cyclone advisory reader (issue
#1294). They are National Hurricane Center products, a US Government work in
the public domain, copied byte for byte from NHC's archive.

| File | Bytes | What | Source |
|---|---|---|---|
| `al092022/al092022_5day_020.zip` | 33,149 | Hurricane Ian, advisory 20, issued 1100 PM EDT Tue Sep 27 2022 (03:00 UTC on the 28th). Forecast points (`_5day_pts`, 8 points from tau 0 to 96 h, synoptic time 00:00 UTC on the 28th), track line (`_5day_lin`), cone (`_5day_pgn`, 1,376 vertices) and coastal watches and warnings (`_ww_wwlin`, not read). | <https://www.nhc.noaa.gov/gis/forecast/archive/al092022_5day_020.zip> |
| `al092022/al092022_fcst_020.zip` | 73,125 | The same advisory's wind radii: 34, 50 and 64 kt at tau 0 to 24 h, 34 and 50 kt at 36 h, 34 kt at 48 and 60 h, none after (`_forecastradii`), and the initial radii (`_initialradii`). | <https://www.nhc.noaa.gov/gis/forecast/archive/al092022_fcst_020.zip> |
| `al092022/al092022_best_track.zip` | 253,198 | Ian's best track as archived after the season, 22 September 18:00 UTC to 1 October 06:00 UTC: points (`_pts`), line (`_lin`), observed radii (`_radii`) and wind swath (`_windswath`, not read). The reader keeps the points up to advisory 20's synoptic time. | <https://www.nhc.noaa.gov/gis/best_track/al092022_best_track.zip> |

Downloaded on 2026-11-04. SHA-256:

```text
c32ca23f231e7512329b5c086a4b54b2b7a8fee8710e9616af6189ce6f2ba8e1  al092022_5day_020.zip
23b2a9ddc2781120a5f84fa797a3d3df9a7baa7f781624ff862e1e716bb287f0  al092022_fcst_020.zip
46c592e1f83b337934837d8ed277ffe1d517449179b0fc559d3822a2db9a20e0  al092022_best_track.zip
```

Cases the archive doesn't hold small (a final advisory with no cone, radii of
all zeros, the southern hemisphere, a cone across the antimeridian) are
written by the tests themselves with `SyntheticNhc` in
`tests/_shared/TropicalCycloneFixtures.cs`.
