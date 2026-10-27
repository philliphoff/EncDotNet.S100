using System.Buffers.Binary;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// GRIB2 fixtures for the GRIB2 processor (issue #1074): the committed NOAA
/// subsets in <c>tests/datasets/GRIB2</c> (copied to <c>TestData/GRIB2</c>)
/// with the ecCodes reference values the tests check, and a small test-only
/// GRIB2 writer for grids no NOAA file has (over the S-101 test cell, a global
/// 0 to 360 grid, ECMWF's parameter codes).
/// </summary>
/// <remarks>
/// The writer is written from the WMO FM 92 GRIB2 templates and covers only
/// what these fixtures need: Section 1, grid template 3.0, product templates
/// 4.0 and 4.8, simple packing (5.0) and a bitmap for missing values. The
/// decoder's own byte-exact encoder (in the decoder's test project) covers the
/// other data templates.
/// </remarks>
internal static class GribFixtures
{
    /// <summary>The model run of every fixture, 2026-10-25 00Z.</summary>
    public static readonly DateTime Run = new(2026, 10, 25, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// GFS-Wave as a NOMADS GRIB filter returns it for f000, f003 and f006 (the
    /// hours appended): WIND, WDIR, UGRD, VGRD, HTSGW, PERPW, DIRPW, WVHGT, three
    /// SWELL partitions and WVDIR, off the US East Coast. 36 messages.
    /// </summary>
    public const string WaveFilter = "gfswave-useast-filter.grib2";

    /// <summary>GFS 10 m UGRD and VGRD at f000, f003 and f006 over the same crop. 6 messages.</summary>
    public const string Wind10m = "gfs-useast-wind10m.grib2";

    /// <summary>GFS-Wave HTSGW, PERPW and DIRPW at f000, f003 and f006 over the same crop. 9 messages.</summary>
    public const string Waves = "gfswave-useast.grib2";

    /// <summary>GFS GUST, PRMSL, TMAX and UGRD at f003, one per data template. 4 messages.</summary>
    public const string Templates = "gfs-useast-templates.grib2";

    /// <summary>GFS-Wave West Coast 0.16° HTSGW, PERPW and DIRPW at f003, exactly as NCEP serves them, JPEG 2000 (template 5.40). 3 messages.</summary>
    public const string Jpeg2000 = "gfswave-wcoast-f003-jpeg2000.grib2";

    /// <summary>The path of a committed fixture in the test output.</summary>
    /// <param name="name">The fixture's file name.</param>
    public static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "GRIB2", name);

    /// <summary>Copies a committed fixture to <paramref name="path"/> and returns the path.</summary>
    /// <param name="name">The fixture's file name.</param>
    /// <param name="path">Where to copy it, for example with another extension or none.</param>
    public static string CopyFixture(string name, string path)
    {
        File.Copy(Fixture(name), path, overwrite: true);
        return path;
    }

    // -----------------------------------------------------------------
    // The US East Coast crop and its ecCodes reference values
    // -----------------------------------------------------------------

    /// <summary>The crop's northern row (the file's first), 40°N.</summary>
    public const double CropNorth = 40;

    /// <summary>The crop's southern row, 32°N.</summary>
    public const double CropSouth = 32;

    /// <summary>The crop's western column, 76°W (284°E in the file).</summary>
    public const double CropWest = -76;

    /// <summary>The crop's eastern column, 68°W (292°E in the file).</summary>
    public const double CropEast = -68;

    /// <summary>The crop's spacing, 0.25°.</summary>
    public const double CropSpacing = 0.25;

    /// <summary>
    /// The latitude and longitude of a point of the crop by its index in the
    /// file (rows north to south from 40°N, 33 columns east from 76°W), as
    /// <c>make_grib_fixtures.py</c> numbers its reference values.
    /// </summary>
    /// <param name="fileIndex">The point's index in the file's order.</param>
    public static (double Latitude, double Longitude) CropPoint(int fileIndex) =>
        (CropNorth - (fileIndex / 33) * CropSpacing, CropWest + (fileIndex % 33) * CropSpacing);

    /// <summary>
    /// ecCodes 2.50.0 values of <see cref="WaveFilter"/> at file index 566
    /// (35.75°N, 74.75°W) for f000, f003 and f006.
    /// </summary>
    public static class FilterAt566
    {
        /// <summary>HTSGW, m.</summary>
        public static readonly double[] Height = [2.5231250002980232, 2.5387500002980232, 2.6224999986588955];

        /// <summary>PERPW, s.</summary>
        public static readonly double[] Period = [10.168906211853027, 10.018750190734863, 9.500312328338623];

        /// <summary>DIRPW, degrees from.</summary>
        public static readonly double[] Direction = [48.25937461853027, 49.84250068664551, 52.09250068664551];

        /// <summary>WIND, m/s.</summary>
        public static readonly double[] WindSpeed = [9.875, 10.378749996423721, 10.287499994039536];

        /// <summary>WDIR, degrees from.</summary>
        public static readonly double[] WindDirection = [59.996562480926514, 65.64859372377396, 60.539843797683716];

        /// <summary>UGRD (surface, the wave model's 10 m forcing wind), m/s.</summary>
        public static readonly double[] U = [-8.554374694824219, -9.477499961853027, -8.9350004196167];

        /// <summary>WVHGT, m.</summary>
        public static readonly double[] WindSeaHeight = [2.5168750286102295, 2.5425000190734863, 2.6075000166893005];

        /// <summary>WVDIR, degrees from.</summary>
        public static readonly double[] WindSeaDirection = [55.02031326293945, 58.368595123291016, 61.588279724121094];
    }

    /// <summary>
    /// ecCodes 2.50.0 values of <see cref="Wind10m"/> at file index 566
    /// (35.75°N, 74.75°W) for f000, f003 and f006.
    /// </summary>
    public static class Wind10mAt566
    {
        /// <summary>UGRD at 10 m, m/s.</summary>
        public static readonly double[] U = [-8.516435623168945, -9.53079605102539, -8.91003704071045];

        /// <summary>VGRD at 10 m, m/s.</summary>
        public static readonly double[] V = [-4.959780693054199, -4.310278415679932, -5.056154727935791];
    }

    /// <summary>
    /// ecCodes values of <see cref="WaveFilter"/> at f003 and file index 9
    /// (40°N, 73.75°W), where the first two swell partitions have data.
    /// </summary>
    public static class FilterSwellAt9
    {
        /// <summary>SWELL, 1 in sequence, m.</summary>
        public const double First = 0.6537499986588955;

        /// <summary>SWELL, 2 in sequence, m.</summary>
        public const double Second = 0.3395312502980232;
    }

    // -----------------------------------------------------------------
    // Generated fixtures
    // -----------------------------------------------------------------

    /// <summary>
    /// A regular latitude/longitude grid as GRIB2 template 3.0 stores it: the
    /// first point, the point count each way and the spacing, rows running
    /// south when <paramref name="NorthToSouth"/> (scanning mode 0, as NCEP
    /// and ECMWF write them).
    /// </summary>
    public sealed record Grid(double FirstLatitude, double FirstLongitude, double Spacing, int Rows, int Columns, bool NorthToSouth = true)
    {
        /// <summary>The latitude of the file's row <paramref name="row"/>.</summary>
        public double Latitude(int row) => FirstLatitude + (NorthToSouth ? -row : row) * Spacing;

        /// <summary>The longitude of column <paramref name="column"/>, as the file gives it.</summary>
        public double Longitude(int column) => FirstLongitude + column * Spacing;
    }

    /// <summary>One message for <see cref="Encode"/>.</summary>
    public sealed record Message
    {
        /// <summary>The discipline: 0 meteorological, 10 oceanographic.</summary>
        public byte Discipline { get; init; }

        /// <summary>The parameter category.</summary>
        public byte Category { get; init; }

        /// <summary>The parameter number.</summary>
        public byte Number { get; init; }

        /// <summary>The originating centre: 7 NCEP, 98 ECMWF.</summary>
        public ushort Centre { get; init; } = 7;

        /// <summary>The generating process identifier: 96 GFS, 11 GFS-Wave at NCEP.</summary>
        public byte Process { get; init; } = 96;

        /// <summary>The reference time.</summary>
        public DateTime ReferenceTime { get; init; } = Run;

        /// <summary>The forecast time in hours (for template 4.8, the start of the interval).</summary>
        public int ForecastHours { get; init; }

        /// <summary>The first fixed surface type: 1 surface, 101 mean sea level, 103 height above ground, 241 NCEP sequence.</summary>
        public byte SurfaceType { get; init; } = 1;

        /// <summary>The first fixed surface's value (metres for 103, the partition for 241).</summary>
        public int SurfaceValue { get; init; }

        /// <summary>
        /// For product template 4.8: the length of the interval in hours, ending
        /// at the forecast time plus the length (a maximum, statistical process 2).
        /// <see langword="null"/> writes template 4.0.
        /// </summary>
        public int? IntervalHours { get; init; }

        /// <summary>The grid.</summary>
        public required Grid Grid { get; init; }

        /// <summary>The values in the file's order (row by row); NaN is missing, written with a bitmap.</summary>
        public required double[] Values { get; init; }

        /// <summary>The decimal scale factor D: values are kept to 10^−D.</summary>
        public int DecimalScale { get; init; } = 3;

        /// <summary>
        /// Whether to write data template 5.42 (CCSDS, as ECMWF Open Data
        /// packs everything) with placeholder data instead of simple packing:
        /// a field the decoder lists as unsupported.
        /// </summary>
        public bool Ccsds { get; init; }
    }

    /// <summary>Encodes one GRIB2 message.</summary>
    /// <param name="m">The message.</param>
    public static byte[] Encode(Message m)
    {
        if (m.Values.Length != m.Grid.Rows * m.Grid.Columns)
            throw new ArgumentException("The values don't match the grid.", nameof(m));

        var body = new List<byte>();
        body.AddRange(Section(1, w => w
            .U16(m.Centre).U16(0).U8(2).U8(1).U8(1)
            .U16(m.ReferenceTime.Year).U8(m.ReferenceTime.Month).U8(m.ReferenceTime.Day)
            .U8(m.ReferenceTime.Hour).U8(m.ReferenceTime.Minute).U8(m.ReferenceTime.Second)
            .U8(0).U8(1)));

        var g = m.Grid;
        double lastLatitude = g.Latitude(g.Rows - 1), lastLongitude = g.Longitude(g.Columns - 1);
        body.AddRange(Section(3, w => w
            .U8(0).U32((uint)(g.Rows * g.Columns)).U8(0).U8(0).U16(0)
            .U8(6).U8(0).U32(0).U8(0).U32(0).U8(0).U32(0)
            .U32((uint)g.Columns).U32((uint)g.Rows).U32(0).U32(0xFFFF_FFFF)
            .S32(Micro(g.FirstLatitude)).S32(Micro(Wrap360(g.FirstLongitude))).U8(0x30)
            .S32(Micro(lastLatitude)).S32(Micro(Wrap360(lastLongitude)))
            .U32((uint)Micro(g.Spacing)).U32((uint)Micro(g.Spacing))
            .U8(g.NorthToSouth ? 0x00 : 0x40)));

        body.AddRange(Section(4, w =>
        {
            w.U16(0).U16(m.IntervalHours is null ? 0 : 8);
            w.U8(m.Category).U8(m.Number).U8(2).U8(0).U8(m.Process).U16(0).U8(0);
            w.U8(1).S32(m.ForecastHours);
            w.U8(m.SurfaceType).S8(0).S32(m.SurfaceValue);
            w.U8(255).U8(255).U32(0xFFFF_FFFF);
            if (m.IntervalHours is { } length)
            {
                var end = m.ReferenceTime.AddHours(m.ForecastHours + length);
                w.U16(end.Year).U8(end.Month).U8(end.Day).U8(end.Hour).U8(end.Minute).U8(end.Second);
                w.U8(1).U32(0);
                w.U8(2).U8(2).U8(1).S32(length).U8(1).S32(0);
            }
        }));

        // Simple packing: Y = (R + X) × 10^−D, with E = 0.
        var present = m.Values.Where(v => !double.IsNaN(v)).ToArray();
        double scale = Math.Pow(10, m.DecimalScale);
        double min = present.Length == 0 ? 0 : present.Min();
        float reference = (float)Math.Floor(min * scale);
        var packed = present.Select(v => (ulong)Math.Round(v * scale - reference)).ToArray();
        ulong max = packed.Length == 0 ? 0 : packed.Max();
        int bits = max == 0 ? 0 : 64 - System.Numerics.BitOperations.LeadingZeroCount(max);
        if (bits == 0)
        {
            // A constant field is R = the value, unscaled, as ecCodes writes and reads it.
            reference = (float)min;
        }

        if (m.Ccsds)
        {
            // Template 5.42: R, E, D, bits, original type, CCSDS flags, block size, reference sample interval.
            body.AddRange(Section(5, w => w
                .U32((uint)present.Length).U16(42)
                .F32(reference).S16(0).S16(m.DecimalScale).U8(16).U8(0).U8(14).U8(32).U16(128)));
            body.AddRange(Section(6, w => w.U8(255)));
            body.AddRange(Section(7, w => w.Bytes(new byte[16])));
            return Finish(m, body);
        }

        body.AddRange(Section(5, w => w
            .U32((uint)present.Length).U16(0)
            .F32(reference).S16(0).S16(m.DecimalScale).U8(bits).U8(0)));

        bool hasBitmap = present.Length != m.Values.Length;
        body.AddRange(Section(6, w =>
        {
            w.U8(hasBitmap ? 0 : 255);
            if (hasBitmap)
                w.Bytes(PackBits(m.Values.Select(v => double.IsNaN(v) ? 0UL : 1UL), 1));
        }));
        body.AddRange(Section(7, w => w.Bytes(PackBits(packed, bits))));
        return Finish(m, body);
    }

    private static byte[] Finish(Message m, List<byte> body)
    {
        var message = new Writer()
            .Bytes("GRIB"u8.ToArray()).U8(0).U8(0).U8(m.Discipline).U8(2).U64((ulong)(16 + body.Count + 4))
            .Bytes([.. body])
            .Bytes("7777"u8.ToArray());
        return message.ToArray();
    }

    /// <summary>Concatenates messages (or files) into one file.</summary>
    /// <param name="parts">The messages.</param>
    public static byte[] Concat(IEnumerable<byte[]> parts) => [.. parts.SelectMany(p => p)];

    /// <summary>Concatenates messages (or files) into one file.</summary>
    /// <param name="parts">The messages.</param>
    public static byte[] Concat(params byte[][] parts) => Concat((IEnumerable<byte[]>)parts);

    /// <summary>Writes <paramref name="bytes"/> to <paramref name="path"/> and returns the path.</summary>
    public static string Write(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>
    /// The grid over IHO S-101 test cell 101AA00DS0020 (62.00°E to 62.17°E,
    /// 32.30°S to 32.13°S): 20 × 20 points 0.01° apart from 32.12°S, 61.99°E,
    /// rows running south as NCEP writes them.
    /// </summary>
    public static Grid ChartGrid { get; } = new(-32.12, 61.99, 0.01, 20, 20);

    /// <summary>A coarser 5 × 5 wind grid over the same cell, from 32.13°S, 62.005°E.</summary>
    public static Grid ChartWindGrid { get; } = new(-32.13, 62.005, 0.04, 5, 5);

    /// <summary>Significant wave height at a node (rows counted from the south): 0.5 m in the south-west, rising north-east and with time.</summary>
    public static double WaveHeight(int row, int column, int step) => 0.6 + 0.12 * (row + column) + 0.6 * step;

    /// <summary>The direction waves come from: 240° turning 15° a step.</summary>
    public static double WaveDirection(int row, int column, int step) => 240 + 15 * step;

    /// <summary>The peak period: 9 s rising 0.1 s a column.</summary>
    public static double WavePeriod(int row, int column, int step) => 9 + 0.1 * column;

    /// <summary>The wind at a node (rows counted from the south), in knots, and the direction it blows from.</summary>
    public static (double Knots, double FromDirection) Wind(int row, int column, int step, int columns)
    {
        int i = row * columns + column;
        return (12 + i + 6 * step, 180 + 5.0 * i);
    }

    /// <summary>
    /// GFS-Wave style waves over <see cref="ChartGrid"/>: HTSGW, PERPW and
    /// DIRPW at the surface at 00Z, 03Z and 06Z, NCEP process 11, with the
    /// north-east node missing (the bitmap), the hours appended one after
    /// another like a GRIB filter download.
    /// </summary>
    public static byte[] ChartWaves()
    {
        var grid = ChartGrid;
        var messages = new List<byte[]>();
        for (int step = 0; step < 3; step++)
        {
            foreach (var (number, value) in new (byte, Func<int, int, int, double>)[]
            {
                (3, WaveHeight), (11, WavePeriod), (10, WaveDirection),
            })
            {
                messages.Add(Encode(new Message
                {
                    Discipline = 10,
                    Category = 0,
                    Number = number,
                    Process = 11,
                    ForecastHours = 3 * step,
                    Grid = grid,
                    Values = Field(grid, (row, column) =>
                        row == grid.Rows - 1 && column == grid.Columns - 1 ? double.NaN : value(row, column, step)),
                }));
            }
        }

        return Concat(messages);
    }

    /// <summary>
    /// GFS style 10 m wind over <see cref="ChartWindGrid"/>: UGRD and VGRD at
    /// 10 m above ground at 00Z, 03Z and 06Z, NCEP process 96.
    /// </summary>
    public static byte[] ChartWind()
    {
        var grid = ChartWindGrid;
        var messages = new List<byte[]>();
        for (int step = 0; step < 3; step++)
        {
            var u = Field(grid, (row, column) => Components(Wind(row, column, step, grid.Columns)).U);
            var v = Field(grid, (row, column) => Components(Wind(row, column, step, grid.Columns)).V);
            messages.Add(Encode(new Message { Category = 2, Number = 2, SurfaceType = 103, SurfaceValue = 10, ForecastHours = 3 * step, Grid = grid, Values = u }));
            messages.Add(Encode(new Message { Category = 2, Number = 3, SurfaceType = 103, SurfaceValue = 10, ForecastHours = 3 * step, Grid = grid, Values = v }));
        }

        return Concat(messages);
    }

    /// <summary>
    /// The U and V components (m/s) of a wind of <paramref name="wind"/> knots
    /// blowing from the given direction.
    /// </summary>
    public static (double U, double V) Components((double Knots, double FromDirection) wind)
    {
        double speed = wind.Knots * 1852.0 / 3600.0;
        double radians = wind.FromDirection * Math.PI / 180;
        return (-speed * Math.Sin(radians), -speed * Math.Cos(radians));
    }

    /// <summary>
    /// A global grid in the 0 to 360 convention, as GFS writes it: 2° spacing,
    /// 90°N to 90°S, 0°E to 358°E (180 × 91 points). Each node's value is its
    /// longitude east (0 to 358) plus its latitude in thousandths
    /// (<see cref="GlobalValue"/>), so a test can see which column landed where.
    /// </summary>
    public static Grid GlobalGrid { get; } = new(90, 0, 2, 91, 180);

    /// <summary>
    /// An ECMWF WAM style <c>swh</c> message at step 3 on <see cref="EcmwfGrid"/>
    /// packed with CCSDS (template 5.42, placeholder data), as ECMWF Open Data
    /// serves everything: a field the decoder lists as unsupported. The
    /// end-to-end tests use it rather than the JPEG 2000 fixture, which a
    /// JPEG 2000 decoder would make readable.
    /// </summary>
    public static byte[] CcsdsMessage() => Encode(new Message
    {
        Discipline = 10,
        Category = 0,
        Number = 3,
        Centre = 98,
        Process = 153,
        SurfaceType = 101,
        ForecastHours = 3,
        Grid = EcmwfGrid,
        Values = new double[EcmwfGrid.Rows * EcmwfGrid.Columns],
        Ccsds = true,
    });

    /// <summary>The value of <see cref="GlobalFile"/> at a node: longitude east (0 to 360) plus (latitude + 90) / 1000.</summary>
    /// <param name="latitude">The node's latitude.</param>
    /// <param name="longitudeEast">The node's longitude, 0 to 360.</param>
    public static double GlobalValue(double latitude, double longitudeEast) => longitudeEast + (latitude + 90) / 1000;

    /// <summary>
    /// A file on <see cref="GlobalGrid"/>: the significant wave height
    /// (10,0,3) at the surface at 00Z and 03Z, with the 03Z values 1 higher.
    /// </summary>
    public static byte[] GlobalFile() => Concat(
        Enumerable.Range(0, 2).Select(step => Encode(new Message
        {
            Discipline = 10,
            Category = 0,
            Number = 3,
            Process = 11,
            ForecastHours = 3 * step,
            Grid = GlobalGrid,
            Values = Field(
                GlobalGrid,
                (row, column) => GlobalValue(GlobalGrid.Latitude(GlobalGrid.Rows - 1 - row), GlobalGrid.Longitude(column)) + step),
        })));

    /// <summary>
    /// A regional grid that crosses the prime meridian in the 0 to 360
    /// convention: 350°E to 10°E (written 350 to 370 by its first point and
    /// spacing), 1° spacing, 60°N to 50°N (21 × 11 points). Values as
    /// <see cref="GlobalValue"/>, with longitudes past 360 wrapped.
    /// </summary>
    public static Grid PrimeMeridianGrid { get; } = new(60, 350, 1, 11, 21);

    /// <summary>A file on <see cref="PrimeMeridianGrid"/>: the significant wave height at 00Z.</summary>
    public static byte[] PrimeMeridianFile() => Encode(new Message
    {
        Discipline = 10,
        Category = 0,
        Number = 3,
        Process = 11,
        Grid = PrimeMeridianGrid,
        Values = Field(
            PrimeMeridianGrid,
            (row, column) => GlobalValue(
                PrimeMeridianGrid.Latitude(PrimeMeridianGrid.Rows - 1 - row),
                Wrap360(PrimeMeridianGrid.Longitude(column)))),
    });

    /// <summary>
    /// The ECMWF open data grid of the generated ECMWF files: 0.5°, 50°N to
    /// 40°N and 10°W to 0° in the −180 to 180 convention (21 × 21 points).
    /// </summary>
    public static Grid EcmwfGrid { get; } = new(50, -10, 0.5, 21, 21);

    /// <summary>
    /// ECMWF IFS style fields (centre 98) over <see cref="EcmwfGrid"/> at steps
    /// 3 and 6: <c>10u</c> and <c>10v</c> (0,2,2 and 0,2,3 at 10 m),
    /// <c>msl</c> (0,3,0 at mean sea level, in Pa), and the <c>10fg</c> gust
    /// (0,2,22 at 10 m, product template 4.8: the maximum over the 3 hours
    /// before the step).
    /// </summary>
    public static byte[] EcmwfOper()
    {
        var messages = new List<byte[]>();
        foreach (int step in (int[])[3, 6])
        {
            messages.Add(Ecmwf(0, 2, 2, 103, 10, step, null, (r, c) => 5 + 0.1 * c + step));
            messages.Add(Ecmwf(0, 2, 3, 103, 10, step, null, (r, c) => -3 + 0.1 * r));
            messages.Add(Ecmwf(0, 3, 0, 101, 0, step, null, (r, c) => 101_000 + 10 * r + 100 * step, decimalScale: 0));
            messages.Add(Ecmwf(0, 2, 22, 103, 10, step - 3, 3, (r, c) => 12 + 0.2 * c + step));
        }

        return Concat(messages);
    }

    /// <summary>
    /// ECMWF WAM style fields (centre 98) over <see cref="EcmwfGrid"/> at step
    /// 3: <c>swh</c> (10,0,3), <c>mwd</c> (10,0,14), <c>pp1d</c> (10,0,34) and
    /// <c>mwp</c> (10,0,15), at mean sea level as ECMWF labels its wave fields.
    /// </summary>
    public static byte[] EcmwfWave() => Concat(
        Ecmwf(10, 0, 3, 101, 0, 3, null, (r, c) => 1 + 0.05 * c),
        Ecmwf(10, 0, 14, 101, 0, 3, null, (r, c) => 280),
        Ecmwf(10, 0, 34, 101, 0, 3, null, (r, c) => 11),
        Ecmwf(10, 0, 15, 101, 0, 3, null, (r, c) => 7));

    private static byte[] Ecmwf(byte discipline, byte category, byte number, byte surface, int surfaceValue, int forecastHours, int? interval, Func<int, int, double> value, int decimalScale = 3) =>
        Encode(new Message
        {
            Discipline = discipline,
            Category = category,
            Number = number,
            Centre = 98,
            Process = 153,
            SurfaceType = surface,
            SurfaceValue = surfaceValue,
            ForecastHours = forecastHours,
            IntervalHours = interval,
            Grid = EcmwfGrid,
            DecimalScale = decimalScale,
            // The value functions take the row from the north, the file's order.
            Values = Field(EcmwfGrid, (row, column) => value(EcmwfGrid.Rows - 1 - row, column)),
        });

    // Values in the file's order from a function of (row from the south, column).
    private static double[] Field(Grid grid, Func<int, int, double> value)
    {
        var values = new double[grid.Rows * grid.Columns];
        for (int fileRow = 0; fileRow < grid.Rows; fileRow++)
        {
            int row = grid.NorthToSouth ? grid.Rows - 1 - fileRow : fileRow;
            for (int column = 0; column < grid.Columns; column++)
                values[fileRow * grid.Columns + column] = value(row, column);
        }

        return values;
    }

    private static double Wrap360(double longitude) => ((longitude % 360) + 360) % 360;

    private static long Micro(double degrees) => (long)Math.Round(degrees * 1e6);

    private static byte[] Section(int number, Action<Writer> write)
    {
        var body = new Writer();
        write(body);
        var content = body.ToArray();
        return new Writer().U32((uint)(5 + content.Length)).U8(number).Bytes(content).ToArray();
    }

    private static byte[] PackBits(IEnumerable<ulong> values, int bits)
    {
        var bytes = new List<byte>();
        int current = 0, used = 0;
        foreach (var value in values)
        {
            for (int i = bits - 1; i >= 0; i--)
            {
                current = (current << 1) | (int)((value >> i) & 1);
                if (++used == 8)
                {
                    bytes.Add((byte)current);
                    current = 0;
                    used = 0;
                }
            }
        }

        if (used > 0)
            bytes.Add((byte)(current << (8 - used)));
        return [.. bytes];
    }

    // Big-endian octets with GRIB2's sign-and-magnitude signed integers.
    private sealed class Writer
    {
        private readonly List<byte> _bytes = [];

        public Writer U8(int value)
        {
            _bytes.Add(checked((byte)value));
            return this;
        }

        public Writer U16(int value)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(b, checked((ushort)value));
            _bytes.AddRange(b.ToArray());
            return this;
        }

        public Writer U32(uint value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(b, value);
            _bytes.AddRange(b.ToArray());
            return this;
        }

        public Writer U64(ulong value)
        {
            Span<byte> b = stackalloc byte[8];
            BinaryPrimitives.WriteUInt64BigEndian(b, value);
            _bytes.AddRange(b.ToArray());
            return this;
        }

        public Writer S8(int value) => U8(value < 0 ? 0x80 | -value : value);

        public Writer S16(int value) => U16(value < 0 ? 0x8000 | -value : value);

        public Writer S32(long value) => U32(value < 0 ? 0x8000_0000u | (uint)-value : (uint)value);

        public Writer F32(float value)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteSingleBigEndian(b, value);
            _bytes.AddRange(b.ToArray());
            return this;
        }

        public Writer Bytes(byte[] bytes)
        {
            _bytes.AddRange(bytes);
            return this;
        }

        public byte[] ToArray() => [.. _bytes];
    }
}
