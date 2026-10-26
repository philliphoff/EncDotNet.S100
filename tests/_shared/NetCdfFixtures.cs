using System.Buffers.Binary;
using System.Text;
using PureHDF;
using PureHDF.Filters;
using PureHDF.VOL.Native;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Generated NetCDF-CF fixtures for the NetCDF reader and processor (issue
/// #1071): a test-only NetCDF-3 writer (classic and 64-bit offset, record
/// variables) written from the Unidata classic format specification, and a
/// NetCDF-4 writer on PureHDF that lays dimensions out as HDF5 dimension
/// scales with <c>DIMENSION_LIST</c> object references. Real Copernicus data
/// needs an account and attribution, so the wave fixtures only copy its
/// variable names and attributes (<c>VHM0</c>, <c>VMDR</c>, <c>VTPK</c>).
/// </summary>
/// <remarks>
/// The NetCDF-4 writer doesn't write the <c>REFERENCE_LIST</c> back-references
/// on the dimension scales, <c>_Netcdf4Dimid</c> or <c>_NCProperties</c>: the
/// reader doesn't need them, and the NetCDF-C library only uses
/// <c>REFERENCE_LIST</c> to keep the scales attached when it rewrites a file.
/// </remarks>
internal static class NetCdfFixtures
{
    /// <summary>The model run of every fixture, 2026-10-25 00Z.</summary>
    public static readonly DateTime Run = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// A regular grid: <paramref name="Rows"/> latitudes from
    /// <paramref name="South"/> and <paramref name="Columns"/> longitudes from
    /// <paramref name="West"/>, <paramref name="Spacing"/> degrees apart, with
    /// <paramref name="Steps"/> time steps <paramref name="StepHours"/> apart
    /// from <see cref="Run"/>.
    /// </summary>
    public sealed record Grid(double South, double West, double Spacing, int Rows, int Columns, int Steps = 3, int StepHours = 3)
    {
        /// <summary>The latitudes, south to north.</summary>
        public double[] Latitudes => Enumerable.Range(0, Rows).Select(r => Math.Round(South + r * Spacing, 6)).ToArray();

        /// <summary>The longitudes, west to east.</summary>
        public double[] Longitudes => Enumerable.Range(0, Columns).Select(c => Math.Round(West + c * Spacing, 6)).ToArray();

        /// <summary>The valid times.</summary>
        public DateTime[] Times => Enumerable.Range(0, Steps).Select(t => Run.AddHours(t * StepHours)).ToArray();
    }

    /// <summary>The grid over IHO S-101 test cell 101AA00DS0020 (62.00°E to 62.17°E, 32.30°S to 32.13°S).</summary>
    public static Grid ChartGrid { get; } = new(-32.31, 61.99, 0.01, 20, 20);

    /// <summary>A coarser 5 × 5 wind grid over the same cell.</summary>
    public static Grid ChartWindGrid { get; } = new(-32.29, 62.005, 0.04, 5, 5);

    /// <summary>Significant wave height at a node: 0.5 m in the south-west, rising north-east and with time.</summary>
    public static double WaveHeight(int row, int column, int step) => 0.5 + 0.15 * (row + column) + 0.5 * step;

    /// <summary>The direction waves come from: 250° turning 10° a step.</summary>
    public static double WaveDirection(int row, int column, int step) => 250 + 10 * step;

    /// <summary>The peak period: 8 s rising 0.1 s a column.</summary>
    public static double WavePeriod(int row, int column, int step) => 8 + 0.1 * column;

    /// <summary>The wind at a node, in knots, and the direction it blows from: 15 kn veering 4° a node, plus 5 kn a step.</summary>
    public static (double Knots, double FromDirection) Wind(int row, int column, int step, int columns)
    {
        int i = row * columns + column;
        return (15 + i + 5 * step, 200 + 4.0 * i);
    }

    /// <summary>
    /// The (row, column) of the one wave node with no data in every wave
    /// fixture: the north-east corner.
    /// </summary>
    public static (int Row, int Column) MissingNode(Grid grid) => (grid.Rows - 1, grid.Columns - 1);

    // -----------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------

    /// <summary>
    /// Writes a Copernicus-style NetCDF-4 wave file: <c>VHM0</c> (packed
    /// <c>short</c>, scale 0.001, with a <c>_FillValue</c>), <c>VMDR</c> and
    /// <c>VTPK</c> (<c>float</c>), on (time, latitude, longitude), chunked one
    /// step a chunk with shuffle and deflate.
    /// </summary>
    /// <param name="path">Where to write the file.</param>
    /// <param name="grid">The grid; <see cref="ChartGrid"/> by default.</param>
    /// <param name="northToSouth">Whether latitudes run north to south, as many models write them.</param>
    /// <param name="dimensionList">Whether variables carry <c>DIMENSION_LIST</c>; without it the reader matches dimensions by length.</param>
    /// <param name="globalAttributes">Extra or replacement global attributes.</param>
    public static string WriteWaveNetCdf4(
        string path,
        Grid? grid = null,
        bool northToSouth = false,
        bool dimensionList = true,
        IReadOnlyDictionary<string, object>? globalAttributes = null)
    {
        grid ??= ChartGrid;
        var file = WaveFile(grid, northToSouth, packedHeight: true, globalAttributes);
        WriteNetCdf4(path, file, dimensionList);
        return path;
    }

    /// <summary>
    /// Writes a NetCDF-4 wind file of 10 m <c>eastward_wind</c> and
    /// <c>northward_wind</c> (<c>u10</c>, <c>v10</c>, m s-1) on (time, height,
    /// latitude, longitude), with a length-1 <c>height</c> of 10 m.
    /// </summary>
    /// <param name="path">Where to write the file.</param>
    /// <param name="grid">The grid; <see cref="ChartWindGrid"/> by default.</param>
    public static string WriteWindNetCdf4(string path, Grid? grid = null)
    {
        grid ??= ChartWindGrid;
        WriteNetCdf4(path, WindFile(grid), dimensionList: true);
        return path;
    }

    /// <summary>
    /// Writes an ERDDAP-style NetCDF-3 wave file: <c>time</c> as the record
    /// dimension (seconds since 1970), a length-1 <c>depth</c>, latitudes north
    /// to south and longitudes 0 to 360, and <c>Thgt</c>, <c>Tdir</c> and
    /// <c>Tper</c> as <c>float</c> record variables with a <c>_FillValue</c>.
    /// </summary>
    /// <param name="path">Where to write the file.</param>
    /// <param name="grid">The grid; <see cref="ChartGrid"/> by default.</param>
    /// <param name="offset64">Whether to write the 64-bit offset format (<c>CDF\x02</c>).</param>
    public static string WriteWaveNetCdf3(string path, Grid? grid = null, bool offset64 = false)
    {
        grid ??= ChartGrid;
        var lats = grid.Latitudes.Reverse().ToArray();
        var lons = grid.Longitudes.Select(l => l < 0 ? l + 360 : l).ToArray();
        int rows = grid.Rows, cols = grid.Columns, steps = grid.Steps;
        var missing = MissingNode(grid);

        double[] Field(Func<int, int, int, double> value)
        {
            var data = new double[steps * rows * cols];
            for (int t = 0; t < steps; t++)
            {
                for (int fileRow = 0; fileRow < rows; fileRow++)
                {
                    int row = rows - 1 - fileRow; // north to south
                    for (int c = 0; c < cols; c++)
                        data[(t * rows + fileRow) * cols + c] = (row, c) == missing ? -9999 : value(row, c, t);
                }
            }

            return data;
        }

        var file = new FixtureFile
        {
            Dimensions = [new("time", steps, Unlimited: true), new("depth", 1), new("latitude", rows), new("longitude", cols)],
            GlobalAttributes = new Dictionary<string, object>
            {
                ["Conventions"] = "COARDS, CF-1.6, ACDD-1.3",
                ["title"] = "Wave Watch 3 (synthetic test data)",
                ["institution"] = "NOAA (synthetic)",
                ["source"] = "WW3",
                ["cdm_data_type"] = "Grid",
            },
            Variables =
            [
                new("time", FixtureType.Double, ["time"], grid.Times.Select(t => (t - DateTime.UnixEpoch).TotalSeconds).ToArray(),
                    new() { ["units"] = "seconds since 1970-01-01T00:00:00Z", ["standard_name"] = "time", ["axis"] = "T", ["calendar"] = "Gregorian" }),
                new("depth", FixtureType.Float, ["depth"], [0.0],
                    new() { ["units"] = "m", ["standard_name"] = "depth", ["positive"] = "down", ["axis"] = "Z" }),
                new("latitude", FixtureType.Float, ["latitude"], lats,
                    new() { ["units"] = "degrees_north", ["standard_name"] = "latitude", ["axis"] = "Y" }),
                new("longitude", FixtureType.Float, ["longitude"], lons,
                    new() { ["units"] = "degrees_east", ["standard_name"] = "longitude", ["axis"] = "X" }),
                new("Thgt", FixtureType.Float, ["time", "depth", "latitude", "longitude"], Field(WaveHeight),
                    new() { ["units"] = "meters", ["standard_name"] = "sea_surface_wave_significant_height", ["_FillValue"] = new[] { -9999f } }),
                new("Tdir", FixtureType.Float, ["time", "depth", "latitude", "longitude"], Field(WaveDirection),
                    new() { ["units"] = "degrees_true", ["standard_name"] = "sea_surface_wave_from_direction", ["_FillValue"] = new[] { -9999f } }),
                new("Tper", FixtureType.Float, ["time", "depth", "latitude", "longitude"], Field(WavePeriod),
                    new() { ["units"] = "s", ["long_name"] = "peak period", ["_FillValue"] = new[] { -9999f } }),
            ],
        };

        WriteNetCdf3(path, file, offset64);
        return path;
    }

    // -----------------------------------------------------------------
    // File models
    // -----------------------------------------------------------------

    /// <summary>The value type of a fixture variable.</summary>
    public enum FixtureType
    {
        /// <summary>16-bit integer.</summary>
        Short,

        /// <summary>32-bit integer.</summary>
        Int,

        /// <summary>32-bit float.</summary>
        Float,

        /// <summary>64-bit float.</summary>
        Double,
    }

    /// <summary>A fixture dimension.</summary>
    public sealed record FixtureDimension(string Name, int Length, bool Unlimited = false);

    /// <summary>A fixture variable: its values row-major along <paramref name="Dimensions"/>.</summary>
    public sealed record FixtureVariable(
        string Name,
        FixtureType Type,
        string[] Dimensions,
        double[] Values,
        Dictionary<string, object> Attributes);

    /// <summary>A fixture file.</summary>
    public sealed class FixtureFile
    {
        /// <summary>The dimensions; at most one unlimited, which comes first.</summary>
        public required List<FixtureDimension> Dimensions { get; init; }

        /// <summary>Global attributes: strings, or arrays of float, double, short or int.</summary>
        public required Dictionary<string, object> GlobalAttributes { get; init; }

        /// <summary>The variables.</summary>
        public required List<FixtureVariable> Variables { get; init; }
    }

    /// <summary>The Copernicus-style wave file model.</summary>
    public static FixtureFile WaveFile(Grid grid, bool northToSouth, bool packedHeight, IReadOnlyDictionary<string, object>? globalAttributes = null)
    {
        var lats = northToSouth ? grid.Latitudes.Reverse().ToArray() : grid.Latitudes;
        int rows = grid.Rows, cols = grid.Columns, steps = grid.Steps;
        var missing = MissingNode(grid);
        const double fill = -32767;

        double[] Field(Func<int, int, int, double> value, double scale, double missingValue)
        {
            var data = new double[steps * rows * cols];
            for (int t = 0; t < steps; t++)
            {
                for (int fileRow = 0; fileRow < rows; fileRow++)
                {
                    int row = northToSouth ? rows - 1 - fileRow : fileRow;
                    for (int c = 0; c < cols; c++)
                    {
                        data[(t * rows + fileRow) * cols + c] = (row, c) == missing
                            ? missingValue
                            : scale == 1 ? value(row, c, t) : Math.Round(value(row, c, t) / scale);
                    }
                }
            }

            return data;
        }

        string[] dims = ["time", "latitude", "longitude"];
        var attributes = new Dictionary<string, object>
        {
            ["Conventions"] = "CF-1.6",
            ["title"] = "Global Ocean Waves Analysis and Forecast (synthetic test data)",
            ["institution"] = "Synthetic test centre",
            ["source"] = "MFWAM",
            ["forecast_reference_time"] = "2026-10-25T00:00:00Z",
        };
        foreach (var (key, value) in globalAttributes ?? new Dictionary<string, object>())
            attributes[key] = value;

        return new FixtureFile
        {
            Dimensions = [new("time", steps), new("latitude", rows), new("longitude", cols)],
            GlobalAttributes = attributes,
            Variables =
            [
                new("time", FixtureType.Double, ["time"], grid.Times.Select(t => (t - new DateTime(1950, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalHours).ToArray(),
                    new() { ["units"] = "hours since 1950-01-01", ["standard_name"] = "time", ["axis"] = "T", ["calendar"] = "standard" }),
                new("latitude", FixtureType.Float, ["latitude"], lats,
                    new() { ["units"] = "degrees_north", ["standard_name"] = "latitude", ["axis"] = "Y" }),
                new("longitude", FixtureType.Float, ["longitude"], grid.Longitudes,
                    new() { ["units"] = "degrees_east", ["standard_name"] = "longitude", ["axis"] = "X" }),
                packedHeight
                    ? new("VHM0", FixtureType.Short, dims, Field(WaveHeight, 0.001, fill),
                        new() { ["units"] = "m", ["standard_name"] = "sea_surface_wave_significant_height", ["long_name"] = "Spectral significant wave height (Hm0)", ["_FillValue"] = new short[] { -32767 }, ["scale_factor"] = new[] { 0.001 }, ["add_offset"] = new[] { 0.0 } })
                    : new("VHM0", FixtureType.Float, dims, Field(WaveHeight, 1, fill),
                        new() { ["units"] = "m", ["standard_name"] = "sea_surface_wave_significant_height", ["_FillValue"] = new[] { -32767f } }),
                new("VMDR", FixtureType.Float, dims, Field(WaveDirection, 1, fill),
                    new() { ["units"] = "degree", ["standard_name"] = "sea_surface_wave_from_direction", ["long_name"] = "Mean wave direction from (Mdir)", ["_FillValue"] = new[] { -32767f } }),
                new("VTPK", FixtureType.Float, dims, Field(WavePeriod, 1, fill),
                    new() { ["units"] = "s", ["standard_name"] = "sea_surface_wave_period_at_variance_spectral_density_maximum", ["long_name"] = "Wave period at spectral peak / peak period (Tp)", ["_FillValue"] = new[] { -32767f } }),
            ],
        };
    }

    /// <summary>The 10 m wind file model.</summary>
    public static FixtureFile WindFile(Grid grid)
    {
        int rows = grid.Rows, cols = grid.Columns, steps = grid.Steps;
        var u = new double[steps * rows * cols];
        var v = new double[steps * rows * cols];
        for (int t = 0; t < steps; t++)
        {
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    var (knots, from) = Wind(r, c, t, cols);
                    double speed = knots * 1852.0 / 3600.0;
                    double radians = from * Math.PI / 180;
                    int i = (t * rows + r) * cols + c;
                    u[i] = -speed * Math.Sin(radians);
                    v[i] = -speed * Math.Cos(radians);
                }
            }
        }

        string[] dims = ["time", "height", "latitude", "longitude"];
        return new FixtureFile
        {
            Dimensions = [new("time", steps), new("height", 1), new("latitude", rows), new("longitude", cols)],
            GlobalAttributes = new Dictionary<string, object>
            {
                ["Conventions"] = "CF-1.8",
                ["title"] = "10 m wind (synthetic test data)",
                ["source"] = "Synthetic wind model",
            },
            Variables =
            [
                new("time", FixtureType.Int, ["time"], grid.Times.Select(t => (t - Run).TotalHours).ToArray(),
                    new() { ["units"] = "hours since 2026-10-25 00:00:00", ["standard_name"] = "time" }),
                new("height", FixtureType.Float, ["height"], [10.0],
                    new() { ["units"] = "m", ["standard_name"] = "height", ["positive"] = "up", ["axis"] = "Z" }),
                new("latitude", FixtureType.Double, ["latitude"], grid.Latitudes,
                    new() { ["units"] = "degrees_north", ["standard_name"] = "latitude" }),
                new("longitude", FixtureType.Double, ["longitude"], grid.Longitudes,
                    new() { ["units"] = "degrees_east", ["standard_name"] = "longitude" }),
                new("u10", FixtureType.Float, dims, u, new() { ["units"] = "m s-1", ["standard_name"] = "eastward_wind" }),
                new("v10", FixtureType.Float, dims, v, new() { ["units"] = "m s-1", ["standard_name"] = "northward_wind" }),
            ],
        };
    }

    // -----------------------------------------------------------------
    // NetCDF-4 writer (PureHDF)
    // -----------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="model"/> as NetCDF-4: each dimension a dimension
    /// scale (its coordinate variable, or an empty pure dimension), each
    /// multi-dimensional variable chunked one slice of its first dimension a
    /// chunk, with shuffle and deflate.
    /// </summary>
    public static void WriteNetCdf4(string path, FixtureFile model, bool dimensionList = true, IReadOnlyList<H5Filter>? filters = null)
    {
        var file = new H5File();
        foreach (var (key, value) in model.GlobalAttributes)
            file.Attributes[key] = AttributeValue(value);

        var scales = new Dictionary<string, H5Dataset>(StringComparer.Ordinal);
        foreach (var dimension in model.Dimensions)
        {
            var coordinate = model.Variables.FirstOrDefault(v => v.Name == dimension.Name && v.Dimensions is [var only] && only == dimension.Name);
            H5Dataset dataset;
            if (coordinate is not null)
            {
                dataset = new H5Dataset(TypedArray(coordinate.Type, coordinate.Values, [dimension.Length]));
                foreach (var (key, value) in coordinate.Attributes)
                    dataset.Attributes[key] = AttributeValue(value);
                dataset.Attributes["NAME"] = dimension.Name;
            }
            else
            {
                dataset = new H5Dataset(new float[dimension.Length]);
                dataset.Attributes["NAME"] = $"This is a netCDF dimension but not a netCDF variable. {dimension.Length,10}";
            }

            dataset.Attributes["CLASS"] = "DIMENSION_SCALE";
            scales[dimension.Name] = dataset;
            file[dimension.Name] = dataset;
        }

        foreach (var variable in model.Variables)
        {
            if (scales.ContainsKey(variable.Name))
                continue;

            var lengths = variable.Dimensions.Select(d => model.Dimensions.Single(x => x.Name == d).Length).ToArray();
            var data = TypedArray(variable.Type, variable.Values, lengths);
            H5Dataset dataset;
            if (lengths.Length >= 2)
            {
                var chunks = lengths.Select((l, i) => i == 0 ? 1u : (uint)l).ToArray();
                dataset = new H5Dataset(
                    data,
                    chunks: chunks,
                    datasetCreation: new H5DatasetCreation(Filters: filters?.ToList() ?? [ShuffleFilter.Id, DeflateFilter.Id]));
            }
            else
            {
                dataset = new H5Dataset(data);
            }

            foreach (var (key, value) in variable.Attributes)
                dataset.Attributes[key] = AttributeValue(value);
            if (dimensionList && variable.Dimensions.Length > 0)
            {
                dataset.Attributes["DIMENSION_LIST"] = variable.Dimensions
                    .Select(d => new H5ObjectReference[] { scales[d] })
                    .ToArray();
            }

            file[variable.Name] = dataset;
        }

        file.Write(path);
    }

    private static object AttributeValue(object value) => value switch
    {
        // A one-element array is written as a scalar, as the NetCDF library does.
        float[] { Length: 1 } a => a[0],
        double[] { Length: 1 } a => a[0],
        short[] { Length: 1 } a => a[0],
        int[] { Length: 1 } a => a[0],
        _ => value,
    };

    private static Array TypedArray(FixtureType type, double[] values, int[] lengths)
    {
        var elementType = type switch
        {
            FixtureType.Short => typeof(short),
            FixtureType.Int => typeof(int),
            FixtureType.Float => typeof(float),
            _ => typeof(double),
        };

        var array = Array.CreateInstance(elementType, lengths.Length == 0 ? [1] : lengths);
        var flat = type switch
        {
            FixtureType.Short => (Array)values.Select(v => (short)v).ToArray(),
            FixtureType.Int => values.Select(v => (int)v).ToArray(),
            FixtureType.Float => values.Select(v => (float)v).ToArray(),
            _ => values.ToArray(),
        };

        Buffer.BlockCopy(flat, 0, array, 0, Buffer.ByteLength(flat));
        return array;
    }

    // -----------------------------------------------------------------
    // NetCDF-3 writer (Unidata classic format specification)
    // -----------------------------------------------------------------

    /// <summary>
    /// Writes <paramref name="model"/> as NetCDF-3 classic (<c>CDF\x01</c>) or
    /// 64-bit offset (<c>CDF\x02</c>): the header, then the fixed-size
    /// variables, then the records, each record holding one slab of every
    /// record variable (padded to 4 bytes unless there's only one).
    /// </summary>
    public static void WriteNetCdf3(string path, FixtureFile model, bool offset64 = false)
    {
        var record = model.Dimensions.FirstOrDefault(d => d.Unlimited);
        int numRecords = record?.Length ?? 0;
        bool IsRecord(FixtureVariable v) => record is not null && v.Dimensions.Length > 0 && v.Dimensions[0] == record.Name;
        int Length(string d) => model.Dimensions.Single(x => x.Name == d).Length;

        long SlabBytes(FixtureVariable v)
        {
            long elements = 1;
            foreach (var d in v.Dimensions.Skip(IsRecord(v) ? 1 : 0))
                elements *= Length(d);
            return elements * Size(v.Type);
        }

        var recordVariables = model.Variables.Where(IsRecord).ToList();
        long Pad(long n) => (n + 3) & ~3L;

        // Pass 1 sizes the header with placeholder offsets; pass 2 writes it.
        var begins = new Dictionary<string, long>();
        byte[] header = Header(model, begins, offset64, numRecords, IsRecord, SlabBytes);
        long offset = header.Length;
        foreach (var v in model.Variables.Where(v => !IsRecord(v)))
        {
            begins[v.Name] = offset;
            offset += Pad(SlabBytes(v));
        }

        foreach (var v in recordVariables)
        {
            begins[v.Name] = offset;
            offset += recordVariables.Count == 1 ? SlabBytes(v) : Pad(SlabBytes(v));
        }

        header = Header(model, begins, offset64, numRecords, IsRecord, SlabBytes);

        using var stream = File.Create(path);
        stream.Write(header);
        foreach (var v in model.Variables.Where(v => !IsRecord(v)))
        {
            var bytes = Encode(v.Type, v.Values);
            stream.Write(bytes);
            stream.Write(new byte[Pad(bytes.Length) - bytes.Length]);
        }

        for (int r = 0; r < numRecords; r++)
        {
            foreach (var v in recordVariables)
            {
                int perRecord = (int)(SlabBytes(v) / Size(v.Type));
                var bytes = Encode(v.Type, v.Values.AsSpan(r * perRecord, perRecord).ToArray());
                stream.Write(bytes);
                if (recordVariables.Count > 1)
                    stream.Write(new byte[Pad(bytes.Length) - bytes.Length]);
            }
        }

    }

    private static byte[] Header(
        FixtureFile model,
        Dictionary<string, long> begins,
        bool offset64,
        int numRecords,
        Func<FixtureVariable, bool> isRecord,
        Func<FixtureVariable, long> slabBytes)
    {
        var buffer = new MemoryStream();
        void Int(int value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, value);
            buffer.Write(b);
        }

        void Long(long value)
        {
            Span<byte> b = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(b, value);
            buffer.Write(b);
        }

        void Name(string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name);
            Int(bytes.Length);
            buffer.Write(bytes);
            buffer.Write(new byte[((bytes.Length + 3) & ~3) - bytes.Length]);
        }

        void Attributes(Dictionary<string, object> attributes)
        {
            if (attributes.Count == 0)
            {
                Int(0);
                Int(0);
                return;
            }

            Int(0x0C);
            Int(attributes.Count);
            foreach (var (key, value) in attributes)
            {
                Name(key);
                byte[] bytes;
                int count;
                switch (value)
                {
                    case string text:
                        Int(2);
                        bytes = Encoding.UTF8.GetBytes(text);
                        count = bytes.Length;
                        break;
                    case short[] shorts:
                        Int(3);
                        bytes = Encode(FixtureType.Short, shorts.Select(s => (double)s).ToArray());
                        count = shorts.Length;
                        break;
                    case int[] ints:
                        Int(4);
                        bytes = Encode(FixtureType.Int, ints.Select(s => (double)s).ToArray());
                        count = ints.Length;
                        break;
                    case float[] floats:
                        Int(5);
                        bytes = Encode(FixtureType.Float, floats.Select(s => (double)s).ToArray());
                        count = floats.Length;
                        break;
                    case double[] doubles:
                        Int(6);
                        bytes = Encode(FixtureType.Double, doubles);
                        count = doubles.Length;
                        break;
                    default:
                        throw new ArgumentException($"Unsupported attribute value {value.GetType().Name}.");
                }

                Int(count);
                buffer.Write(bytes);
                buffer.Write(new byte[((bytes.Length + 3) & ~3) - bytes.Length]);
            }
        }

        buffer.Write("CDF"u8);
        buffer.WriteByte(offset64 ? (byte)2 : (byte)1);
        Int(numRecords);

        Int(0x0A);
        Int(model.Dimensions.Count);
        foreach (var d in model.Dimensions)
        {
            Name(d.Name);
            Int(d.Unlimited ? 0 : d.Length);
        }

        Attributes(model.GlobalAttributes);

        Int(0x0B);
        Int(model.Variables.Count);
        foreach (var v in model.Variables)
        {
            Name(v.Name);
            Int(v.Dimensions.Length);
            foreach (var d in v.Dimensions)
                Int(model.Dimensions.FindIndex(x => x.Name == d));
            Attributes(v.Attributes);
            Int(v.Type switch { FixtureType.Short => 3, FixtureType.Int => 4, FixtureType.Float => 5, _ => 6 });
            Int((int)((slabBytes(v) + 3) & ~3L));
            long begin = begins.GetValueOrDefault(v.Name);
            if (offset64)
                Long(begin);
            else
                Int((int)begin);
        }

        return buffer.ToArray();
    }

    private static int Size(FixtureType type) => type switch
    {
        FixtureType.Short => 2,
        FixtureType.Int or FixtureType.Float => 4,
        _ => 8,
    };

    private static byte[] Encode(FixtureType type, double[] values)
    {
        int size = Size(type);
        var bytes = new byte[values.Length * size];
        for (int i = 0; i < values.Length; i++)
        {
            var span = bytes.AsSpan(i * size, size);
            switch (type)
            {
                case FixtureType.Short: BinaryPrimitives.WriteInt16BigEndian(span, (short)values[i]); break;
                case FixtureType.Int: BinaryPrimitives.WriteInt32BigEndian(span, (int)values[i]); break;
                case FixtureType.Float: BinaryPrimitives.WriteSingleBigEndian(span, (float)values[i]); break;
                default: BinaryPrimitives.WriteDoubleBigEndian(span, values[i]); break;
            }
        }

        return bytes;
    }
}
