# EncDotNet.S100.Datasets.NetCdf

`EncDotNet.S100.Datasets.NetCdf` reads NetCDF-CF wind and wave grids, such as
Copernicus Marine wave forecasts and NOAA ERDDAP `griddap` downloads, into the
shared weather grid model of
[`EncDotNet.S100.Datasets.Weather`](../EncDotNet.S100.Datasets.Weather/README.md).
It's managed code with no native dependencies: NetCDF-3 is read by a small
reader written from the Unidata classic format specification, and NetCDF-4 is
read through [PureHDF](https://github.com/Apollo3zehn/PureHDF).

> **Not for navigation.** Weather model data isn't official S-100 or ENC data.
> Hosts label it so wherever it appears.

The design is in [Wind and wave data](../../docs/design/weather-sources.md),
§3.2 (detection), §3.4 (the grid model) and §7.1 (this reader). To draw,
sample and pick a file, use `NetCdfDatasetProcessor` in
[`EncDotNet.S100.Datasets.Pipelines`](../EncDotNet.S100.Datasets.Pipelines/README.md#weather-grids),
which the default product registry picks up as `NetCDF`.

## Install

```bash
dotnet add package EncDotNet.S100.Datasets.NetCdf
```

## Read a file

```csharp
using EncDotNet.S100.Datasets.NetCdf;
using EncDotNet.S100.Datasets.Weather;

using var dataset = NetCdfWeatherDataset.Open("cmems_mod_glo_wav_anfc.nc");

Console.WriteLine($"{dataset.Format}: {dataset.GridSet.Origin.Model}, run {dataset.ReferenceTime:u}");
foreach (var parameter in dataset.GridSet.Parameters)
    Console.WriteLine($"{parameter.Id} ({parameter.SourceName}), {parameter.Steps.Count} steps");

// Steps are read from the file only when decoded, normalised to the
// south-west-origin grid, with missing values as NaN.
var height = dataset.GridSet.FindParameter(WeatherParameters.WaveHeight)!;
float[] values = dataset.GridSet.ReadStep(height, stepIndex: 0);
```

`NetCdfWeatherDataset` keeps the file open, because each time step is read
when it's first decoded; dispose it to close the file. `NetCdfFile` is the
format layer underneath, if you need the raw variables and attributes.

## Formats

| Format | Signature | How it's read |
|---|---|---|
| NetCDF-3 classic | `CDF\x01` | Managed reader: header, fixed-size and record variables, 32-bit offsets |
| NetCDF-3 64-bit offset | `CDF\x02` | The same, with 64-bit offsets |
| NetCDF-4 | HDF5 signature | PureHDF: dimension scales, `DIMENSION_LIST`, hyperslab reads |

- **Record variables.** A `numrecs` of `0xFFFFFFFF` (a file still being
  written) is worked out from the file length.
- **Compression.** NetCDF-4 deflate and shuffle are supported (PureHDF also
  reads Fletcher-32, N-bit and scale-offset). Any other HDF5 filter, such as
  Zstandard or Blosc, fails with a `NetCdfFormatException` that names it.
- **Not supported.** CDF-5 (`CDF\x05`), NetCDF-4 groups (only the root
  group is read), and compound, enum, opaque and variable-length types. Each
  fails with a message saying so.

## CF rules

- **Coordinates.** Latitude, longitude and time are found by `standard_name`
  (`latitude`, `longitude`, `time`), by `axis` (`Y`, `X`, `T`) or by units
  (`degrees_north`, `degrees_east`, "&lt;unit&gt; since …"). Latitude and
  longitude must be 1-D and evenly spaced; rows running south and 0 to 360
  longitudes are normalised by the weather model.
- **Time.** Units of seconds, minutes, hours or days since a reference time,
  in the `standard`, `gregorian` or `proleptic_gregorian` calendar
  (`CfTimeUnits`). Months, years and other calendars fail.
- **Variables.** A data variable on the latitude × longitude grid is matched
  to the weather parameter table by its `standard_name` and converted to the
  table's units (feet, knots, km/h and hPa are converted; anything else makes
  it an unknown parameter). Extra dimensions of length 1 (a single height or
  depth) are allowed; a 10 m height is the wind level. A variable with several
  levels is listed in `IgnoredVariables` and left out.
- **Values.** `_FillValue`, `missing_value`, `valid_min`, `valid_max` and
  `valid_range` are applied to the stored values (and the NetCDF default fill
  value when there's no `_FillValue`), then `scale_factor` and `add_offset`.
- **Model and run.** The model is the global `source`, `institution` or
  `title` attribute. The run time is a `forecast_reference_time` variable or
  global attribute; without one, `ReferenceTime` is null and the grid set's
  run time is the first valid time.

Failures say what to do or what the file holds:

| Exception | When |
|---|---|
| `NetCdfFormatException` | Not NetCDF, CDF-5, a damaged or truncated file, groups only, an unsupported filter |
| `CfConventionsException` | No latitude, longitude or time coordinate; unsupported time units or calendar; no variable the parameter table knows. The message lists the variables found, with their standard names |
| `UnsupportedWeatherGridException` | 2-D latitude and longitude (a curvilinear grid), a `grid_mapping` other than `latitude_longitude` (named in the message), uneven spacing, or a regional grid across the antimeridian |

## Dependencies

- `EncDotNet.S100.Datasets.Weather`: the grid model and parameter table.
- `PureHDF`: the HDF5 reader under NetCDF-4.

## See also

- [Wind and wave data — Design Note](../../docs/design/weather-sources.md)
- [NetCDF Classic Format Specification (Unidata)](https://docs.unidata.ucar.edu/netcdf-c/current/file_format_specifications.html)
- [CF Conventions](https://cfconventions.org/)
