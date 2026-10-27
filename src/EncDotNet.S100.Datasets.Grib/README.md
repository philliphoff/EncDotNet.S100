# EncDotNet.S100.Datasets.Grib

`EncDotNet.S100.Datasets.Grib` is a managed GRIB2 decoder for wind and wave
grids. It covers the parts of GRIB2 that NOAA's GFS and GFS-Wave and ECMWF's
open data use, and maps each field onto the
[`EncDotNet.S100.Datasets.Weather`](../EncDotNet.S100.Datasets.Weather/README.md)
grid model. It has no native dependencies. It was written from the WMO
Manual on Codes (FM 92 GRIB edition 2), the PNG specification and, for its
JPEG 2000 decoder, ITU-T T.800 | ISO/IEC 15444-1.

> **Not for navigation.** Weather model data isn't official S-100 or ENC data.
> Hosts label it so wherever it appears.

The design is in [Wind and wave data](../../docs/design/weather-sources.md),
§7.2. That section also lists which templates each source serves.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.Grib
```

## Read a file

```csharp
using EncDotNet.S100.Datasets.Grib;

using var grib = GribFile.Open("gfs.t00z.pgrb2.0p25.f003");

foreach (var field in grib.Fields)
    Console.WriteLine(field);   // #587 0,2,2 type 103 = 10 ref 2026-10-25T00:00Z valid 2026-10-25T03:00Z 3.0 4.0 5.3

var u = grib.Fields.First(f => f.FindWeatherParameter()?.Id == "wind.u");
float[] values = grib.ReadValues(u);   // row-major, NaN where missing
```

Opening a file indexes it. The decoder reads Section 0 of each message and
sections 1, 3, 4 and 5 of each field, and skips the bitmaps and data.
`ReadValues` then reads and unpacks one field. Reads are safe from several
threads at once. The file stays open until the `GribFile` is disposed.
`GribFile.Open` takes a path, a seekable `Stream` or bytes in memory.

## Build a weather grid set

Each field gives what the weather model needs:

| Model input | From the field |
|---|---|
| `WeatherNativeGrid` | `field.ToWeatherNativeGrid()`: the first point, and increments signed by the scanning mode |
| Canonical parameter | `field.FindWeatherParameter()`: the GRIB2 code through `WeatherParameters.FindByGribCode`, and a level check where the row has a level (10 m for wind) |
| `WeatherLevel` | `field.ToWeatherLevel()`: surface, mean sea level, height above ground, or `Other` with a description |
| Run time, valid time | `field.ReferenceTime`, `field.ValidTime` (UTC; for template 4.8, the end of the interval) |
| Centre | `field.Identification.CentreName`, for example `"NCEP"` |
| `WeatherStepDecoder` | `grib.CreateStepDecoder(field)`, which decodes only when the step is needed |

```csharp
var steps = grib.Fields
    .Where(f => f.FindWeatherParameter()?.Id == WeatherParameters.WindU)
    .Select(f => new WeatherTimeStep(f.ValidTime!.Value, grib.CreateStepDecoder(f)))
    .ToList();
var first = grib.Fields[0];
var u = new WeatherParameter(WeatherParameters.Find(WeatherParameters.WindU)!, first.ToWeatherLevel(), steps);
var set = new WeatherGridSet(
    new WeatherOrigin("GFS", first.ReferenceTime, first.Identification.CentreName),
    first.ToWeatherNativeGrid(),
    [u]);
```

The GRIB2 dataset processor (issue #1074) does this for a whole file.

## What's supported

| Part | Support |
|---|---|
| Framing | Sections 0 to 8. Several messages per file, with any bytes between them skipped (padding, WMO headers). Repeated sections 2 to 7, 3 to 7 or 4 to 7 in a message, giving one field per Section 7. |
| Section 1 | Centre, sub-centre, table versions, reference time and its significance, production status, data type. |
| Section 2 | Skipped. |
| Section 3 | Template 3.0, the regular latitude/longitude grid. All scanning modes except staggered grids (flag table 3.4, bits 5 to 8). Basic angle and subdivisions. Sign-and-magnitude coordinates. Increments worked out from the corners when the template omits them. |
| Section 4 | Template 4.0 (analysis or forecast at a time) and 4.8 (statistics over a time interval): parameter, generating process, forecast time in any code table 4.4 unit, first and second fixed surfaces, and for 4.8 the end of the interval and each time range specification. |
| Section 5 and 7 | 5.0 simple packing. 5.2 complex packing. 5.3 complex packing with spatial differencing of order 1 or 2. Missing value management 0, 1 or 2 for 5.2 and 5.3. 5.40 JPEG 2000, lossless or lossy, through the decoder [below](#jpeg-2000). 5.41 PNG packing, with grey images of 1 to 16 bits and RGB or RGBA images of 8 bits per channel. |
| Section 6 | A bitmap (indicator 0), the previous bitmap in the message (254), or none (255). |

### How values are decoded

Every template stores integers `X` that decode to `Y = (R + X × 2^E) × 10^−D`,
computed in `double` and returned as `float`. Missing points are `NaN`,
whether the bitmap marks them or the complex packing embeds them.

- **0 bits.** In 5.0, 5.40 and 5.41, a field packed in 0 bits is constant,
  and every value is `R` itself, unscaled. ecCodes writes and reads constant
  fields that way: it stores the value as `R` and leaves `D` as it was.
  Section 7 may be empty.
- **5.0.** Each `X` takes `BitsPerValue` bits.
- **5.2.** Section 7 holds the NG group references (`BitsPerValue` bits
  each), the group widths (added to the width reference) and the group
  lengths (`reference + stored × increment`; the last group's length is
  given separately). Each part is padded to an octet. The values follow,
  each the group reference plus `width` bits.
  - Missing value management 1: a value of all ones for its group's width is
    missing.
  - Missing value management 2: all ones less one is a second missing value.
  - A group of width 0 is all missing when its reference is all ones for
    `BitsPerValue` bits.
- **5.3.** As 5.2, after the extra descriptors that start Section 7: the
  first value (and, for order 2, the second), then the minimum difference,
  each in sign and magnitude. The integers are then undifferenced over the
  points that aren't missing:
  - order 1: `X + min + previous`;
  - order 2: `X + min + 2 × previous − the one before`.
- **5.41.** Section 7 is a PNG image. Its pixels, read row by row, are the
  `X`. The pixels are read at the image's own depth. NCEP's g2lib writes
  that depth in the template, but ecCodes writes the field's bit count (for
  example 11) and stores a 16-bit image. Chunk CRCs aren't checked.
  Interlaced and palette images fail with a message.
- **5.40.** Section 7 is a JPEG 2000 codestream with one greyscale
  component. Its samples, row by row, are the `X`. NCEP writes the packed
  values as one row (width = the packed count, height 1) when the field has
  a bitmap; ecCodes writes an `Ni × Nj` image when it hasn't. The template's
  type of compression (`CompressionType`: 0 lossless, 1 lossy) and target
  ratio (`TargetCompressionRatio`) are reported but not needed: the
  codestream says which transform it uses. ecCodes' "lossy" is the 5/3
  transform cut short by rate control, not 9/7.

### JPEG 2000

GRIB2's JPEG 2000 is decoded by a managed decoder in this package
(`Internal/Jpeg2000`, not public), written from ITU-T T.800 | ISO/IEC
15444-1 for what GRIB2 needs. No OpenJPEG, JasPer or JJ2000 code was used.

Every GFS-Wave message NOAA serves on AWS (checked in the West Coast,
Atlantic, East Pacific, South Global and global 0.25° grids of the
2026-10-25 00Z run) is a raw codestream written by JasPer 2.0.25: one tile,
a one-row image, 5 decomposition levels of the 5/3 reversible transform,
64 × 64 code-blocks, no code-block style flags, LRCP order, one layer, no
precincts, SOP or EPH, with QCD, QCC and COM markers, and 7 to 16 bits.
The decoder covers much more than that:

| Feature | Support |
|---|---|
| Container | A raw codestream (SOC to EOC), or a JP2 file's contiguous codestream box. |
| Image | One component, 1 to 31 bits, unsigned or signed, with any subsampling. Image and tile offsets, any number of tiles, tile-parts in any order. |
| Markers | SIZ, COD and COC, QCD and QCC (main and tile-part headers, with T.800's precedence), SOT, SOD, EOC. TLM, PLM, PLT, CRG and COM are skipped. SOP and EPH are read. |
| Tier-2 | The five progression orders (LRCP, RLCP, RPCL, PCRL, CPRL), any number of quality layers, default and custom precinct sizes, tag trees, Lblock and codeword segments. |
| Tier-1 | The MQ decoder and the three coding passes with T.800's contexts. Every code-block style: selective arithmetic coding bypass, context reset, termination on each pass, vertically causal contexts, predictable termination, segmentation symbols. Code-blocks of up to 30 coded magnitude bit-planes. |
| Transform | The 5/3 reversible and 9/7 irreversible wavelets with 0 to 32 levels, by lifting with symmetric extension, for any size and offset. No quantisation, and derived and expounded scalar quantisation. Midpoint reconstruction of truncated code-blocks. DC level shift. |

It decodes the image at full resolution with every layer. These fail with
`UnsupportedGribException` naming the feature:

| Feature | Message |
|---|---|
| Several components | "The JPEG 2000 codestream has 3 components, which isn't supported; only single-component (greyscale) images are, as GRIB2 uses." |
| Regions of interest | "The JPEG 2000 codestream has a region of interest (RGN marker), which isn't supported." |
| Progression order changes | "The JPEG 2000 codestream has progression order changes (POC marker), which aren't supported." |
| Packed packet headers | "The JPEG 2000 codestream has packed packet headers (PPM marker), which aren't supported." (or PPT) |
| Part 2 | "The JPEG 2000 codestream uses JPEG 2000 Part 2 extensions (Rsiz 0x8000), which aren't supported; only Part 1 is.", and for an arbitrary wavelet kernel, "… uses wavelet transformation 2 (a Part 2 arbitrary kernel) …" |
| HTJ2K (Part 15) | "The JPEG 2000 codestream is HTJ2K (ITU-T T.814 \| ISO/IEC 15444-15, …), which isn't supported; only JPEG 2000 Part 1 is." for Rsiz bit 14 or a CAP marker, and "… uses HT code-blocks …" for the code-block style. |
| Samples over 31 bits | "JPEG 2000 samples of 32 bits aren't supported; at most 31 are." |

A malformed codestream (truncated, a bad marker segment, missing tiles)
throws `GribFormatException` with a message starting "The JPEG 2000 …".

On this project's 4-core build machine (Release, .NET 10, one thread), the
global 0.25° GFS-Wave significant height (1440 × 721 points, 565,732
packed) decodes in about 85 ms, the slowest global wave field (wind
direction, 16 bits) in about 210 ms, and GFS 10 m U repacked by ecCodes as
a 1440 × 721 lossless image in about 230 ms; OpenJPEG 2.5.4 takes 70, 160
and 140 ms for the same codestreams.

`ReadValues` returns the values row by row (`row × Ni + column`). Rows and
columns run the way `ToWeatherNativeGrid` says:

- Rows run south (negative latitude increment) unless scanning mode bit 2
  is set.
- Columns run west (negative longitude increment) when bit 1 is set.
- Column-major order (bit 3) and boustrophedon order (bit 4) are undone by
  the decoder.

### What isn't supported, and the errors

| Case | Behaviour |
|---|---|
| GRIB edition 1, or any edition but 2 | `GribFile.Open` throws `UnsupportedGribException`: "The message is GRIB edition 1, which isn't supported; only GRIB edition 2 is. Convert the file to GRIB2 …" |
| Data templates 5.42 (CCSDS/AEC), 5.4 (IEEE float), 5.61, 5.200 and others | The field is listed. Its `UnsupportedReason` is, for example, "Data representation template 5.42 (CCSDS/AEC) isn't supported; only templates 5.0, 5.2, 5.3, 5.40 and 5.41 are." `ReadValues` throws `UnsupportedGribException` with that message. |
| JPEG 2000 features GRIB2 doesn't use | `ReadValues` throws `UnsupportedGribException` naming the feature (see [JPEG 2000](#jpeg-2000)). |
| Grid templates other than 3.0 (for example 3.30 Lambert conformal, 3.20 polar stereographic, 3.40 Gaussian), quasi-regular or staggered grids | The field is listed with the reason. `ToWeatherNativeGrid` throws the model's `UnsupportedWeatherGridException`, and `ReadValues` throws `UnsupportedGribException`. |
| Product templates other than 4.0 and 4.8 (for example 4.1 and 4.11 ensembles) | The field is listed with its parameter and the reason, and `ValidTime` is `null`. |
| Predefined bitmaps (Section 6 indicator 1 to 253), missing value management above 2, spatial differencing of order 3 or more | The field is listed with the reason. |
| Malformed files: truncated messages, bad section lengths, no "7777", a bitmap that disagrees with the packed count | `GribFormatException`, naming what's wrong and the byte offset. |

ECMWF open data uses CCSDS, which isn't supported yet. The design note's
§7.2 and open question §10.6 cover what that means and the options.

## Main types

| Type | Description |
|---|---|
| `GribFile` | Opens and indexes a file. `Fields`, `ReadValues` and `CreateStepDecoder`. |
| `GribField` | One indexed field: `Parameter`, `Identification`, `Grid`, `Product`, `DataRepresentation`, `HasBitmap`, the message offset and length, `ReferenceTime`, `ValidTime` and `UnsupportedReason`. It also has the weather mapping methods. |
| `GribIdentification` | Section 1. |
| `GribGrid`, `GribLatLonGrid`, `GribScanningMode` | Section 3, and template 3.0's geometry and scanning flags. |
| `GribProduct`, `GribFixedSurface`, `GribTimeRange`, `GribTimeUnit`, `GribStatisticalProcess` | Section 4 and templates 4.0 and 4.8. |
| `GribDataRepresentation`, `GribMissingValueManagement` | Section 5. |
| `GribCentres` | Short names for the originating centres the readers target. |
| `GribFormatException`, `UnsupportedGribException` | Malformed files, and valid files using something unsupported. |

## Tests and fixtures

The tests decode NOAA GFS and GFS-Wave subsets in `tests/datasets/GRIB2`.
The expected values were dumped with ecCodes 2.50.0. The subsets include
one GFS message and four GFS-Wave JPEG 2000 messages exactly as NCEP serves
them. A test-only encoder writes messages for every supported data template
but 5.40, so each has a byte-exact round-trip test.
`tools/grib-fixtures/make_grib_fixtures.py` rebuilds the fixtures.

The JPEG 2000 decoder is also tested on its own, against 51 codestreams
written by OpenJPEG (2.5.4 and 2.5.0) in `tests/datasets/GRIB2/jpeg2000`,
which `tools/grib-fixtures/make_jpeg2000_fixtures.py` rebuilds: bit depths
of 1 to 24, sizes down to 1 × 1, 0 to 5 levels, tiles and offsets,
interleaved tile-parts, every progression order, the code-block styles,
SOP, EPH, PLT and TLM markers, subsampling, quality layers, the 9/7
transform and a JP2 file. Lossless cases must match exactly. Lossy ones
must be within 1 of what OpenJPEG decodes, or 4 at 16 bits, because
OpenJPEG scales the 9/7 high-pass samples by 1.625732422 / 2 where T.800
has 1 / K (3.3 × 10⁻⁵ apart). There are also tests of the MQ decoder
(including ITU-T T.88's test sequence), the tag tree and the 1D transforms
against reference encoders written in the tests.

## Dependencies

- `EncDotNet.S100.Datasets.Weather`, for `GribParameterCode`,
  `WeatherNativeGrid`, `WeatherLevel`, the parameter table and
  `WeatherStepDecoder`. Through it, `EncDotNet.S100.Core` and
  `EncDotNet.S100.Portrayals`.
- `System.IO.Compression` (in the framework) for the PNG image data.
- Nothing for JPEG 2000: the decoder is part of this package.

## See also

- [Wind and wave data — Design Note](../../docs/design/weather-sources.md)
- [EncDotNet.S100.Datasets.Weather](../EncDotNet.S100.Datasets.Weather/README.md)
