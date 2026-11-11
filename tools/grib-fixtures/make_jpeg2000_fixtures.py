"""Builds the JPEG 2000 codestream fixtures in tests/datasets/GRIB2/jpeg2000
with OpenJPEG, an encoder independent of the managed decoder, so the
decoder's tests can check it against another implementation.

Needs Python 3.10+ and, in a scratch venv outside the repository:

    python3 -m venv venv && venv/bin/pip install numpy pillow imagecodecs glymur packaging
    venv/bin/python -I tools/grib-fixtures/make_jpeg2000_fixtures.py OUT_DIR

Three front ends to OpenJPEG are used, for the options each exposes:

- Pillow (its wheel bundles OpenJPEG 2.5.4): sizes, decomposition levels,
  tiles and tile and image offsets, progression orders, code-block and
  precinct sizes, quality layers, the 9/7 transform, JP2 files.
- imagecodecs (bundles OpenJPEG 2.5.4): bit depths other than 8 and 16, and
  signed samples.
- glymur, over the system's libopenjp2 (2.5.0 here): the code-block style
  flags (OpenJPEG's mode switch), SOP and EPH markers, PLT and TLM
  markers, and component subsampling.

For each case it writes NAME.j2k (or .jp2) and NAME.raw, the samples the
decoder must produce, row by row, little-endian, 1, 2 or 4 bytes each by
the precision (signed when the case is). For lossless cases that's the
image that was encoded, checked to round-trip through OpenJPEG. For lossy
cases it's what OpenJPEG decodes, and the tests allow a tolerance. It also
writes manifest.json, which the tests read, and prints a summary.

The images are smooth fields with a little noise, from a fixed seed, so
the output is the same on every run with the same OpenJPEG.
"""

import io
import json
import os
import sys
import tempfile

import glymur
import imagecodecs
import numpy as np
from PIL import Image


def field(width, height, bits, seed=1, signed=False):
    """A smooth pattern with noise, scaled to the full range of `bits`."""
    y, x = np.mgrid[0:height, 0:width].astype(np.float64)
    rng = np.random.default_rng(seed)
    v = (0.5 + 0.3 * np.sin(x / 5.0 + seed) * np.cos(y / 7.0)
         + 0.1 * np.sin((x + 2 * y) / 3.0) + rng.normal(0, 0.03, (height, width)))
    v = np.clip(v, 0, 1)
    if bits == 1:
        v = (v > 0.5).astype(np.int64)
    else:
        v = np.round(v * (2 ** bits - 1)).astype(np.int64)
    if signed:
        v -= 2 ** (bits - 1)
    dtype = (np.int8 if bits <= 8 else np.int16 if bits <= 16 else np.int32) if signed else \
        (np.uint8 if bits <= 8 else np.uint16 if bits <= 16 else np.uint32)
    return v.astype(dtype)


def pillow(arr, jp2=False, **options):
    out = io.BytesIO()
    Image.fromarray(arr).save(out, format="JPEG2000", no_jp2=not jp2, **options)
    data = out.getvalue()
    decoded = np.array(Image.open(io.BytesIO(data)))
    return data, decoded


def glymur_encode(arr, **options):
    with tempfile.TemporaryDirectory() as tmp:
        path = os.path.join(tmp, "x.j2k")
        glymur.Jp2k(path, data=arr, **options)
        with open(path, "rb") as f:
            data = f.read()
        decoded = glymur.Jp2k(path)[:]
    return data, decoded


def imagecodecs_encode(arr, bits, **options):
    data = imagecodecs.jpeg2k_encode(arr, level=0, codecformat="J2K", bitspersample=bits, reversible=True, **options)
    return data, imagecodecs.jpeg2k_decode(data)


def split_tile_parts(data):
    """Rewrites a codestream written with SOP markers so that each tile is
    in two tile-parts, split at the SOP marker nearest the middle of its
    packets, and the tile-parts of different tiles are interleaved: all the
    first parts in tile order, then all the second parts. T.800 allows both;
    none of the front ends writes them."""
    def u16(p):
        return int.from_bytes(data[p:p + 2], "big")

    first_sot = data.find(b"\xff\x90")
    header, position, tiles = data[:first_sot], first_sot, []
    while u16(position) == 0xFF90:
        index = u16(position + 4)
        length = int.from_bytes(data[position + 6:position + 10], "big")
        sod = data.find(b"\xff\x93", position)
        assert data[position + 12:sod] == b"", "tile-part headers aren't expected"
        body = data[sod + 2:position + length]
        sops = [i for i in range(len(body)) if body[i:i + 2] == b"\xff\x91"]
        assert len(sops) >= 2, "the tile needs two packets to split"
        middle = min(sops[1:], key=lambda i: abs(i - len(body) // 2))
        tiles.append((index, [body[:middle], body[middle:]]))
        position += length
    assert data[position:] == b"\xff\xd9"

    out = bytearray(header)
    for part in range(2):
        for index, bodies in tiles:
            body = bodies[part]
            out += b"\xff\x90" + (10).to_bytes(2, "big") + index.to_bytes(2, "big")
            out += (12 + 2 + len(body)).to_bytes(4, "big") + bytes([part, 2]) + b"\xff\x93" + body
    return bytes(out + b"\xff\xd9")


CASES = []


def case(name, encoder, arr, bits, lossless=True, tolerance=0, signed=False, ext="j2k", **options):
    CASES.append(dict(name=name, encoder=encoder, arr=arr, bits=bits, lossless=lossless,
                      tolerance=tolerance, signed=signed, ext=ext, options=options))


def define_cases():
    # Bit depths, 37 × 19, lossless.
    for bits in (1, 4, 8, 12, 16, 20, 24):
        case(f"depth-{bits:02d}", "imagecodecs", field(37, 19, bits), bits)
    case("depth-12-signed", "imagecodecs", field(37, 19, 12, signed=True), 12, signed=True)

    # Odd sizes, 8 bits, lossless; as many levels as the size allows.
    case("size-1x1", "pillow", field(1, 1, 8), 8, num_resolutions=1)
    case("size-3x5", "pillow", field(3, 5, 8), 8, num_resolutions=2)
    case("size-37x19", "pillow", field(37, 19, 8), 8, num_resolutions=4)
    case("size-1x40", "pillow", field(1, 40, 8), 8, num_resolutions=1)
    case("size-200x1", "imagecodecs", field(200, 1, 12), 12)

    # Decomposition levels 0 to 5, 41 × 33, lossless.
    for levels in range(6):
        case(f"levels-{levels}", "pillow", field(41, 33, 8, seed=2), 8, num_resolutions=levels + 1)

    # Tiles, with tile and image offsets.
    case("tiles-16x16", "pillow", field(50, 40, 8, seed=3), 8, tile_size=(16, 16), num_resolutions=3)
    case("tiles-offset", "pillow", field(50, 40, 8, seed=3), 8, tile_size=(16, 12), tile_offset=(3, 5), offset=(7, 9), num_resolutions=3)
    # Pillow only writes an image offset with an explicit tile size.
    case("image-offset-odd", "pillow", field(29, 23, 8, seed=4), 8, offset=(5, 3), tile_size=(64, 64), tile_offset=(1, 2), num_resolutions=4)

    # Progression orders, with several layers and small precincts, so the
    # order of packets matters.
    for order in ("LRCP", "RLCP", "RPCL", "PCRL", "CPRL"):
        case(f"progression-{order.lower()}", "pillow", field(48, 40, 8, seed=5), 8, progression=order,
             precinct_size=(16, 16), codeblock_size=(8, 8), num_resolutions=3,
             quality_mode="rates", quality_layers=[20, 6, 0])
    case("progression-pcrl-tiles-offset", "pillow", field(45, 39, 8, seed=5), 8, progression="PCRL",
         precinct_size=(8, 8), codeblock_size=(4, 4), num_resolutions=3, tile_size=(20, 16), tile_offset=(2, 1), offset=(5, 3))
    case("progression-rpcl-tiles-offset", "pillow", field(45, 39, 8, seed=5), 8, progression="RPCL",
         precinct_size=(16, 8), codeblock_size=(4, 4), num_resolutions=3, tile_size=(20, 16), tile_offset=(2, 1), offset=(5, 3))

    # Code-block sizes.
    case("codeblock-4x4", "pillow", field(37, 19, 8, seed=6), 8, codeblock_size=(4, 4), num_resolutions=3)
    case("codeblock-64x16", "pillow", field(70, 40, 8, seed=6), 8, codeblock_size=(16, 64), num_resolutions=3)

    # Code-block styles (OpenJPEG's mode switch), one at a time and together.
    styles = {"bypass": 1, "reset": 2, "termall": 4, "vcausal": 8, "pterm": 16, "segsym": 32, "all": 63,
              "bypass-termall": 5}
    for name, mode in styles.items():
        case(f"style-{name}", "glymur", field(40, 37, 16, seed=7).astype(np.uint16), 16, modesw=mode, numres=3, cbsize=(16, 16))
    case("style-all-lossy", "glymur", field(40, 37, 8, seed=7), 8, lossless=False, tolerance=1,
         modesw=63, numres=3, cbsize=(16, 16), irreversible=True, cratios=[12, 4])

    # SOP and EPH markers, PLT and TLM, subsampling.
    case("markers-sop-eph", "glymur", field(37, 19, 8, seed=8), 8, sop=True, eph=True, numres=3, cratios=[10, 1])
    case("markers-plt-tlm", "glymur", field(37, 19, 8, seed=8), 8, plt=True, tlm=True, numres=3, tilesize=(16, 16))
    case("subsampling-2x3", "glymur", field(37, 19, 8, seed=8), 8, subsam=(3, 2), numres=2)
    case("tile-parts-interleaved", "glymur", field(37, 30, 8, seed=8), 8, split=True, sop=True, numres=3,
         tilesize=(16, 24), cratios=[10, 3, 1])

    # Quality layers: lossless 5/3 with several, and lossy 9/7.
    case("layers-lossless", "pillow", field(48, 40, 12, seed=9).astype(np.uint16), 16, quality_mode="rates", quality_layers=[40, 10, 3, 0])
    case("lossy-97-8bit", "pillow", field(48, 40, 8, seed=9), 8, lossless=False, tolerance=1, irreversible=True,
         quality_mode="rates", quality_layers=[8])
    case("lossy-97-layers", "pillow", field(48, 40, 8, seed=9), 8, lossless=False, tolerance=1, irreversible=True,
         quality_mode="rates", quality_layers=[30, 10, 4])
    # OpenJPEG scales the 9/7 high-pass samples by 1.625732422 / 2 where
    # T.800 has 1 / K: 3.3e-5 apart, which shows as an error of up to 2 at
    # 16 bits. The managed decoder follows T.800, so allow 4.
    case("lossy-97-16bit", "pillow", field(48, 40, 16, seed=9).astype(np.uint16), 16, lossless=False, tolerance=4,
         irreversible=True, quality_mode="rates", quality_layers=[6])
    case("lossy-97-tiles-offset", "pillow", field(45, 39, 8, seed=10), 8, lossless=False, tolerance=1, irreversible=True,
         tile_size=(20, 16), tile_offset=(2, 1), offset=(5, 3), num_resolutions=3, quality_mode="rates", quality_layers=[5])
    case("lossy-53-truncated", "pillow", field(48, 40, 8, seed=9), 8, lossless=False, tolerance=1,
         quality_mode="rates", quality_layers=[10])

    # A JP2 file: the decoder takes its codestream box.
    case("container-jp2", "pillow", field(37, 19, 8, seed=11), 8, ext="jp2", jp2=True, num_resolutions=3)


def encode(c):
    arr, options = c["arr"], dict(c["options"])
    split = options.pop("split", False)
    if c["encoder"] == "pillow":
        data, decoded = pillow(arr, **options)
    elif c["encoder"] == "glymur":
        data, decoded = glymur_encode(arr, **options)
    else:
        data, decoded = imagecodecs_encode(arr, c["bits"], **options)
    if split:
        data = split_tile_parts(data)
        decoded = imagecodecs.jpeg2k_decode(data)
    return data, decoded


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    define_cases()
    manifest = []
    total = 0
    for c in CASES:
        data, decoded = encode(c)
        decoded = np.asarray(decoded)
        if c["lossless"]:
            assert decoded.shape == c["arr"].shape and np.array_equal(decoded, c["arr"]), c["name"]
            expected = c["arr"]
        else:
            expected = decoded
        width_bytes = 1 if c["bits"] <= 8 else 2 if c["bits"] <= 16 else 4
        kind = "i" if c["signed"] else "u"
        raw = np.ascontiguousarray(expected).astype(f"<{kind}{width_bytes}").tobytes()
        name = f"{c['name']}.{c['ext']}"
        with open(os.path.join(out_dir, name), "wb") as f:
            f.write(data)
        with open(os.path.join(out_dir, c["name"] + ".raw"), "wb") as f:
            f.write(raw)
        total += len(data) + len(raw)
        error = int(np.abs(expected.astype(np.int64) - c["arr"].astype(np.int64)).max()) if expected.shape == c["arr"].shape else None
        manifest.append(dict(
            name=c["name"], file=name, encoder=c["encoder"], width=int(expected.shape[1]), height=int(expected.shape[0]),
            precision=c["bits"], signed=c["signed"], lossless=c["lossless"], tolerance=c["tolerance"],
            options={k: (list(v) if isinstance(v, tuple) else v) for k, v in c["options"].items()},
            maxErrorVsSource=error))
        print(f"{c['name']:32} {c['encoder']:11} {expected.shape[1]:4} x {expected.shape[0]:<3} {c['bits']:2} bits "
              f"{len(data):6} bytes {'lossless' if c['lossless'] else f'lossy, max error vs source {error}'}")
    with open(os.path.join(out_dir, "manifest.json"), "w") as f:
        json.dump(dict(generator="tools/grib-fixtures/make_jpeg2000_fixtures.py",
                       openjpeg={"pillow": "2.5.4 (Pillow " + Image.__version__ + ")",
                                 "imagecodecs": imagecodecs.jpeg2k_version() + " (imagecodecs " + imagecodecs.__version__ + ")",
                                 "glymur": str(glymur.version.openjpeg_version) + " (glymur " + glymur.__version__ + ")"},
                       cases=manifest), f, indent=1)
        f.write("\n")
    print(f"{len(CASES)} cases, {total} bytes")


main()
