"""Builds the GRIB2 test fixtures in tests/datasets/GRIB2 from NOAA's GFS and
GFS-Wave runs on AWS (noaa-gfs-bdp-pds, public domain), and prints the
reference values the tests hardcode.

Needs Python 3.10+ and ecCodes' Python bindings with the binary library:

    python3 -m venv venv && venv/bin/pip install eccodes eccodeslib numpy
    venv/bin/python -I tools/grib-fixtures/make_grib_fixtures.py OUT_DIR [RUN]

RUN is the model run as YYYYMMDDHH (default 2026102500). AWS keeps the
recent runs; pick another if that one has gone. Each wanted message is read
with an HTTP byte range from the run's .idx index, so only a few MB are
downloaded. Run it outside the repository and copy the files across.

What it writes (see tests/datasets/GRIB2/README.md):

- gfs-1p00-ugrd10m-f003.grib2: one GFS 1° message exactly as NCEP serves it
  (template 5.3, spatial differencing order 2).
- gfs-useast-wind10m.grib2: GFS 0.25° 10 m U and V at f000, f003 and f006,
  cropped to the US East Coast and repacked by ecCodes with template 5.3
  and spatial differencing order 2, the packing NCEP uses.
- gfs-useast-templates.grib2: GFS 0.25° gust (5.0), mean sea level pressure
  (5.2), 2 m maximum temperature (product template 4.8, data 5.41 PNG) and
  10 m U (5.3, order 1) at f003, cropped and repacked by ecCodes.
- gfswave-useast.grib2: GFS-Wave 0.25° significant height, peak period and
  primary direction at f000, f003 and f006, cropped and repacked by ecCodes
  (5.3, 5.2 and 5.0) with the land bitmap.
- gfswave-wcoast-f003-jpeg2000.grib2: GFS-Wave West Coast 0.16° significant
  height, peak period and primary direction, exactly as NCEP serves them
  (template 5.40, JPEG 2000 written by JasPer, with the land bitmap).
- gfswave-atlocn-htsgw-f003-jpeg2000.grib2: GFS-Wave Atlantic 0.16°
  significant height, exactly as NCEP serves it (template 5.40).
- gfs-useast-jpeg2000.grib2: cropped and repacked by ecCodes as 5.40, which
  encodes with OpenJPEG: GFS 10 m U lossless and PRMSL lossy (target ratio
  10), both 33 × 33 images; GFS-Wave significant height with the land
  bitmap (a one-row image); and a constant gust field (0 bits, an empty
  Section 7).
"""

import os
import sys
import urllib.request

import eccodes
import numpy as np

BUCKET = "https://noaa-gfs-bdp-pds.s3.amazonaws.com"

# The crop: 40°N to 32°N, 76°W to 68°W (284°E to 292°E), 0.25°: 33 × 33 points.
NORTH, SOUTH, WEST, EAST = 40.0, 32.0, 284.0, 292.0
MISSING = 9999.0


def read_index(url):
    lines = urllib.request.urlopen(url + ".idx").read().decode().splitlines()
    out = []
    for i, line in enumerate(lines):
        start = int(line.split(":")[1])
        end = int(lines[i + 1].split(":")[1]) - 1 if i + 1 < len(lines) else None
        out.append((line, start, end))
    return out


def fetch_message(url, match):
    """Returns the bytes of the first message whose .idx line contains match."""
    for line, start, end in read_index(url):
        if match in line:
            rng = f"bytes={start}-{'' if end is None else end}"
            req = urllib.request.Request(url, headers={"Range": rng})
            return urllib.request.urlopen(req).read()
    raise KeyError(f"{match} not in {url}.idx")


def crop(message, packing, order=None, keys=None, constant=None):
    """Crops a regular lat/lon message (scanning mode 0) and repacks it.
    keys are set after the packing type (for example JPEG 2000's type of
    compression); constant replaces every value that isn't missing."""
    src = eccodes.codes_new_from_message(message)
    ni = eccodes.codes_get(src, "Ni")
    nj = eccodes.codes_get(src, "Nj")
    lat0 = eccodes.codes_get(src, "latitudeOfFirstGridPointInDegrees")
    lon0 = eccodes.codes_get(src, "longitudeOfFirstGridPointInDegrees")
    dlat = eccodes.codes_get(src, "jDirectionIncrementInDegrees")
    dlon = eccodes.codes_get(src, "iDirectionIncrementInDegrees")
    assert eccodes.codes_get(src, "scanningMode") == 0
    bitmap = eccodes.codes_get(src, "bitmapPresent") == 1
    eccodes.codes_set(src, "missingValue", MISSING)
    values = eccodes.codes_get_values(src).reshape(nj, ni)
    j0, j1 = round((lat0 - NORTH) / dlat), round((lat0 - SOUTH) / dlat)
    i0, i1 = round((WEST - lon0) / dlon), round((EAST - lon0) / dlon)
    sub = values[j0:j1 + 1, i0:i1 + 1].copy()

    out = eccodes.codes_clone(src)
    eccodes.codes_release(src)
    eccodes.codes_set(out, "packingType", "grid_simple")
    eccodes.codes_set(out, "bitmapPresent", 1 if bitmap else 0)
    eccodes.codes_set(out, "missingValue", MISSING)
    eccodes.codes_set(out, "Ni", sub.shape[1])
    eccodes.codes_set(out, "Nj", sub.shape[0])
    eccodes.codes_set(out, "latitudeOfFirstGridPointInDegrees", NORTH)
    eccodes.codes_set(out, "longitudeOfFirstGridPointInDegrees", WEST)
    eccodes.codes_set(out, "latitudeOfLastGridPointInDegrees", SOUTH)
    eccodes.codes_set(out, "longitudeOfLastGridPointInDegrees", EAST)
    eccodes.codes_set_values(out, sub.flatten())
    eccodes.codes_set(out, "packingType", packing)
    if order is not None:
        eccodes.codes_set(out, "orderOfSpatialDifferencing", order)
    for key, value in (keys or {}).items():
        eccodes.codes_set(out, key, value)
    if constant is not None:
        sub = np.where(sub == MISSING, MISSING, constant)
    eccodes.codes_set_values(out, sub.flatten())
    data = eccodes.codes_get_message(out)
    eccodes.codes_release(out)
    return data


def report(path):
    """Prints, per message, the keys and values the tests check."""
    print(f"== {os.path.basename(path)} ({os.path.getsize(path)} bytes)")
    with open(path, "rb") as f:
        index = 0
        while True:
            h = eccodes.codes_grib_new_from_file(f)
            if h is None:
                break
            keys = ["discipline", "parameterCategory", "parameterNumber", "shortName",
                    "productDefinitionTemplateNumber", "dataRepresentationTemplateNumber",
                    "bitmapPresent", "Ni", "Nj", "dataDate", "dataTime", "forecastTime",
                    "typeOfFirstFixedSurface", "scaledValueOfFirstFixedSurface",
                    "latitudeOfFirstGridPointInDegrees", "longitudeOfFirstGridPointInDegrees",
                    "jDirectionIncrementInDegrees", "iDirectionIncrementInDegrees", "scanningMode",
                    "numberOfMissing", "offset", "totalLength"]
            if eccodes.codes_is_defined(h, "orderOfSpatialDifferencing"):
                keys.append("orderOfSpatialDifferencing")
            if eccodes.codes_get(h, "dataRepresentationTemplateNumber") == 40:
                keys += ["bitsPerValue", "typeOfCompressionUsed", "targetCompressionRatio"]
            if eccodes.codes_get(h, "productDefinitionTemplateNumber") == 8:
                keys += ["lengthOfTimeRange", "typeOfStatisticalProcessing", "yearOfEndOfOverallTimeInterval",
                         "monthOfEndOfOverallTimeInterval", "dayOfEndOfOverallTimeInterval",
                         "hourOfEndOfOverallTimeInterval"]
            print(f"  [{index}] " + " ".join(f"{k}={eccodes.codes_get(h, k, int if k.startswith('typeOf') else None)}" for k in keys))
            eccodes.codes_set(h, "missingValue", MISSING)
            v = eccodes.codes_get_values(h)
            valid = v[v != MISSING]
            n = len(v)
            picks = [0, 1, n // 2, n - 1, 17 * 33 + 5]
            print("     values " + " ".join(f"[{i}]={v[i]!r}" for i in picks))
            print(f"     valid={len(valid)} min={valid.min()!r} max={valid.max()!r} mean={valid.mean()!r}")
            # A checksum over every point, weighted by position so that the
            # order counts too (the JPEG 2000 tests use it).
            weights = (np.arange(n) % 13) + 1
            present = v != MISSING
            print(f"     weighted={float(np.sum(v[present] * weights[present]))!r}")
            eccodes.codes_release(h)
            index += 1


def main():
    out_dir = sys.argv[1]
    run = sys.argv[2] if len(sys.argv) > 2 else "2026102500"
    day, hour = run[:8], run[8:]
    os.makedirs(out_dir, exist_ok=True)
    atmos = f"{BUCKET}/gfs.{day}/{hour}/atmos/gfs.t{hour}z.pgrb2"
    wave = f"{BUCKET}/gfs.{day}/{hour}/wave/gridded/gfswave.t{hour}z"

    files = {}
    files["gfs-1p00-ugrd10m-f003.grib2"] = [fetch_message(f"{atmos}.1p00.f003", ":UGRD:10 m above ground:")]
    files["gfs-useast-wind10m.grib2"] = [
        crop(fetch_message(f"{atmos}.0p25.f{fh:03d}", f":{var}:10 m above ground:"),
             "grid_complex_spatial_differencing", order=2)
        for fh in (0, 3, 6) for var in ("UGRD", "VGRD")
    ]
    files["gfs-useast-templates.grib2"] = [
        crop(fetch_message(f"{atmos}.0p25.f003", ":GUST:surface:"), "grid_simple"),
        crop(fetch_message(f"{atmos}.0p25.f003", ":PRMSL:mean sea level:"), "grid_complex"),
        crop(fetch_message(f"{atmos}.0p25.f003", ":TMAX:2 m above ground:"), "grid_png"),
        crop(fetch_message(f"{atmos}.0p25.f003", ":UGRD:10 m above ground:"),
             "grid_complex_spatial_differencing", order=1),
    ]
    files["gfswave-useast.grib2"] = [
        crop(fetch_message(f"{wave}.global.0p25.f{fh:03d}.grib2", f":{var}:surface:"), packing)
        for fh in (0, 3, 6)
        for var, packing in (("HTSGW", "grid_complex_spatial_differencing"),
                             ("PERPW", "grid_complex"),
                             ("DIRPW", "grid_simple"))
    ]
    files["gfswave-wcoast-f003-jpeg2000.grib2"] = [
        fetch_message(f"{wave}.wcoast.0p16.f003.grib2", f":{var}:surface:") for var in ("HTSGW", "PERPW", "DIRPW")
    ]
    files["gfswave-atlocn-htsgw-f003-jpeg2000.grib2"] = [
        fetch_message(f"{wave}.atlocn.0p16.f003.grib2", ":HTSGW:surface:")
    ]
    files["gfs-useast-jpeg2000.grib2"] = [
        crop(fetch_message(f"{atmos}.0p25.f003", ":UGRD:10 m above ground:"), "grid_jpeg"),
        crop(fetch_message(f"{atmos}.0p25.f003", ":PRMSL:mean sea level:"), "grid_jpeg",
             keys={"typeOfCompressionUsed": 1, "targetCompressionRatio": 10}),
        crop(fetch_message(f"{wave}.global.0p25.f003.grib2", ":HTSGW:surface:"), "grid_jpeg"),
        crop(fetch_message(f"{atmos}.0p25.f003", ":GUST:surface:"), "grid_jpeg", constant=5.5),
    ]

    for name, messages in files.items():
        path = os.path.join(out_dir, name)
        with open(path, "wb") as f:
            for m in messages:
                f.write(m)
        report(path)
    print("ecCodes", eccodes.codes_get_api_version())


main()
