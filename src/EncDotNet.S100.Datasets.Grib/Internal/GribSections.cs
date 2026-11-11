using System.Globalization;
using static EncDotNet.S100.Datasets.Grib.Internal.GribBinary;

namespace EncDotNet.S100.Datasets.Grib.Internal;

/// <summary>
/// Parses sections 1, 3, 4 and 5 of a GRIB2 message. Each method takes the
/// whole section, including its five-octet header (length and number), so the
/// zero-based index of the spec's octet <c>n</c> is <c>n − 1</c>.
/// </summary>
/// <remarks>
/// Written from the WMO Manual on Codes, Volume I.2, FM 92 GRIB edition 2,
/// and its templates and code tables. A template this decoder doesn't read
/// isn't an error here: the parser returns what it can and a reason, which
/// becomes <see cref="GribField.UnsupportedReason"/>, so the file can still
/// be listed.
/// </remarks>
internal static class GribSections
{
    private const uint Missing32 = 0xFFFF_FFFF;

    public static GribIdentification ParseIdentification(ReadOnlySpan<byte> s)
    {
        Require(s, 21, 1);
        DateTime referenceTime;
        try
        {
            referenceTime = new DateTime(UInt16(s, 12), s[14], s[15], s[16], s[17], s[18], DateTimeKind.Utc);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            throw new GribFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Section 1 has an invalid reference time ({UInt16(s, 12)}-{s[14]}-{s[15]} {s[16]}:{s[17]}:{s[18]})."),
                ex);
        }

        return new GribIdentification(
            Centre: UInt16(s, 5),
            SubCentre: UInt16(s, 7),
            MasterTablesVersion: s[9],
            LocalTablesVersion: s[10],
            ReferenceTimeSignificance: s[11],
            ReferenceTime: referenceTime,
            ProductionStatus: s[19],
            DataType: s[20]);
    }

    public static GribGrid ParseGrid(ReadOnlySpan<byte> s, out string? unsupported)
    {
        Require(s, 14, 3);
        var source = s[5];
        long points = UInt32(s, 6);
        var optionalListOctets = s[10];
        int template = UInt16(s, 12);

        if (source != 0)
        {
            unsupported = string.Create(
                CultureInfo.InvariantCulture,
                $"The grid is a predetermined grid (source of grid definition {source}, code table 3.0) rather than a template, which isn't supported.");
            return new GribGrid(template, points, null);
        }

        if (template != 0)
        {
            unsupported = string.Create(
                CultureInfo.InvariantCulture,
                $"Grid definition template 3.{template} ({DescribeGridTemplate(template)}) isn't supported; only template 3.0, the regular latitude/longitude grid, is.");
            return new GribGrid(template, points, null);
        }

        Require(s, 72, 3);
        if (optionalListOctets != 0)
        {
            unsupported = "The grid is a quasi-regular (reduced) latitude/longitude grid, with a list of points per row, which isn't supported.";
            return new GribGrid(template, points, null);
        }

        var ni = UInt32(s, 30);
        var nj = UInt32(s, 34);
        if (ni == Missing32 || nj == Missing32)
        {
            unsupported = "The grid is a quasi-regular (reduced) latitude/longitude grid (Ni or Nj missing), which isn't supported.";
            return new GribGrid(template, points, null);
        }

        if (ni == 0 || nj == 0 || ni > int.MaxValue || nj > int.MaxValue || (long)ni * nj != points)
        {
            throw new GribFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Grid template 3.0 has Ni = {ni} and Nj = {nj}, which don't match its {points} data points."));
        }

        var scanning = (GribScanningMode)s[71];
        if ((scanning & GribScanningMode.StaggeredMask) != 0)
        {
            unsupported = string.Create(
                CultureInfo.InvariantCulture,
                $"The grid's scanning mode 0x{s[71]:X2} (flag table 3.4) uses bits 5 to 8, for staggered grids, which aren't supported.");
            return new GribGrid(template, points, null);
        }

        // Angles are in units of basicAngle / subdivisions degrees, or
        // millionths of a degree when either is 0 or missing.
        var basicAngle = UInt32(s, 38);
        var subdivisions = UInt32(s, 42);
        var unit = basicAngle is 0 or Missing32 || subdivisions is 0 or Missing32
            ? 1e-6
            : (double)basicAngle / subdivisions;

        var la1 = SignedInt32(s, 46) * unit;
        var lo1 = SignedInt32(s, 50) * unit;
        var resolutionFlags = s[54];
        var la2 = SignedInt32(s, 55) * unit;
        var lo2 = SignedInt32(s, 59) * unit;
        var di = UInt32(s, 63);
        var dj = UInt32(s, 67);

        var iIncrement = (resolutionFlags & 0x20) != 0 && di != Missing32
            ? di * unit
            : DeriveLongitudeIncrement(lo1, lo2, (int)ni, (scanning & GribScanningMode.INegative) != 0);
        var jIncrement = (resolutionFlags & 0x10) != 0 && dj != Missing32
            ? dj * unit
            : nj > 1 ? Math.Abs(la2 - la1) / (nj - 1) : 0;

        unsupported = null;
        return new GribGrid(
            template,
            points,
            new GribLatLonGrid((int)ni, (int)nj, la1, lo1, la2, lo2, iIncrement, jIncrement, scanning, s[14]));
    }

    public static GribProduct ParseProduct(ReadOnlySpan<byte> s, out string? unsupported)
    {
        Require(s, 11, 4);
        int template = UInt16(s, 7);
        var category = s[9];
        var number = s[10];

        if (template is not (0 or 8))
        {
            unsupported = string.Create(
                CultureInfo.InvariantCulture,
                $"Product definition template 4.{template} ({DescribeProductTemplate(template)}) isn't supported; only templates 4.0 and 4.8 are.");
            return new GribProduct(template, category, number, null, null, null, null, null, null, [], null);
        }

        Require(s, template == 0 ? 34 : 46, 4);
        var forecastTime = new GribTimeRange((GribTimeUnit)s[17], SignedInt32(s, 18));
        var first = ParseSurface(s, 22);
        var second = ParseSurface(s, 28);

        DateTime? intervalEnd = null;
        List<GribStatisticalProcess> statistics = [];
        long? missingInStatistics = null;
        if (template == 8)
        {
            try
            {
                intervalEnd = new DateTime(UInt16(s, 34), s[36], s[37], s[38], s[39], s[40], DateTimeKind.Utc);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new GribFormatException("Product template 4.8 has an invalid end of overall time interval.", ex);
            }

            int ranges = s[41];
            missingInStatistics = UInt32(s, 42);
            Require(s, 46 + (12 * ranges), 4);
            for (var i = 0; i < ranges; i++)
            {
                var o = 46 + (12 * i);
                statistics.Add(new GribStatisticalProcess(
                    Type: s[o],
                    TimeIncrementType: s[o + 1],
                    Length: new GribTimeRange((GribTimeUnit)s[o + 2], SignedInt32(s, o + 3)),
                    Increment: new GribTimeRange((GribTimeUnit)s[o + 7], SignedInt32(s, o + 8))));
            }
        }

        unsupported = null;
        return new GribProduct(
            template,
            category,
            number,
            generatingProcessType: s[11],
            generatingProcessIdentifier: s[13],
            forecastTime,
            first,
            second,
            intervalEnd,
            statistics,
            missingInStatistics);
    }

    public static GribDataRepresentation ParseDataRepresentation(ReadOnlySpan<byte> s, out string? unsupported)
    {
        Require(s, 11, 5);
        long values = UInt32(s, 5);
        int template = UInt16(s, 9);

        // Templates 5.0 to 5.3, 5.40, 5.41 and 5.42 all start with R, E, D,
        // the bits per value and the original field type.
        var hasCommon = template is (0 or 1 or 2 or 3 or 40 or 41 or 42 or 50 or 51 or 61) && s.Length >= 21;
        var reference = hasCommon ? Float32(s, 11) : 0f;
        var e = hasCommon ? SignedInt16(s, 15) : 0;
        var d = hasCommon ? SignedInt16(s, 17) : 0;
        var bits = hasCommon ? s[19] : 0;
        var originalType = hasCommon ? s[20] : (byte)0;

        ComplexPackingParameters? complex = null;
        Jpeg2000PackingParameters? jpeg2000 = null;
        unsupported = null;
        switch (template)
        {
            case 0:
                Require(s, 21, 5);
                if (bits > 32)
                    unsupported = string.Create(CultureInfo.InvariantCulture, $"Simple packing (template 5.0) with {bits} bits per value isn't supported; at most 32 are.");
                break;

            case 2:
            case 3:
                Require(s, template == 2 ? 47 : 49, 5);
                complex = new ComplexPackingParameters(
                    GroupSplittingMethod: s[21],
                    MissingValueManagement: (GribMissingValueManagement)s[22],
                    PrimaryMissingSubstitute: UInt32(s, 23),
                    SecondaryMissingSubstitute: UInt32(s, 27),
                    NumberOfGroups: UInt32(s, 31),
                    GroupWidthReference: s[35],
                    GroupWidthBits: s[36],
                    GroupLengthReference: UInt32(s, 37),
                    GroupLengthIncrement: s[41],
                    LastGroupLength: UInt32(s, 42),
                    GroupLengthBits: s[46],
                    SpatialDifferencingOrder: template == 3 ? s[47] : 0,
                    ExtraDescriptorOctets: template == 3 ? s[48] : 0);
                if (s[22] > 2)
                    unsupported = string.Create(CultureInfo.InvariantCulture, $"Complex packing missing value management {s[22]} (code table 5.5) isn't supported.");
                else if (template == 3 && complex.SpatialDifferencingOrder is not (1 or 2))
                    unsupported = string.Create(CultureInfo.InvariantCulture, $"Spatial differencing of order {complex.SpatialDifferencingOrder} (template 5.3) isn't supported; only orders 1 and 2 are.");
                else if (template == 3 && complex.ExtraDescriptorOctets is < 1 or > 4)
                    unsupported = string.Create(CultureInfo.InvariantCulture, $"Template 5.3 with {complex.ExtraDescriptorOctets}-octet extra descriptors isn't supported.");
                else if (bits > 32 || complex.GroupWidthBits > 32 || complex.GroupLengthBits > 32)
                    unsupported = "Complex packing with more than 32 bits per group reference, width or length isn't supported.";
                break;

            case 40:
                Require(s, 23, 5);
                jpeg2000 = new Jpeg2000PackingParameters(CompressionType: s[21], TargetCompressionRatio: s[22]);
                if (bits > 31)
                    unsupported = string.Create(CultureInfo.InvariantCulture, $"JPEG 2000 packing (template 5.40) with {bits} bits per value isn't supported; at most 31 are.");
                break;

            case 41:
                Require(s, 21, 5);
                if (bits > 32)
                    unsupported = string.Create(CultureInfo.InvariantCulture, $"PNG packing (template 5.41) with a depth of {bits} bits isn't supported; at most 32 are.");
                break;

            default:
                unsupported = string.Create(
                    CultureInfo.InvariantCulture,
                    $"Data representation template 5.{template} ({DescribeDataTemplate(template)}) isn't supported; only templates 5.0, 5.2, 5.3, 5.40 and 5.41 are.");
                break;
        }

        return new GribDataRepresentation(template, values, reference, e, d, bits, originalType, complex, jpeg2000);
    }

    private static GribFixedSurface? ParseSurface(ReadOnlySpan<byte> s, int offset)
    {
        var type = s[offset];
        if (type == GribFixedSurface.None)
            return null;

        var scaleRaw = s[offset + 1];
        var valueRaw = UInt32(s, offset + 2);
        double? value = null;
        if (scaleRaw != 0xFF && valueRaw != Missing32)
        {
            var scale = SignedInt8(s, offset + 1);
            value = SignedInt32(s, offset + 2) * Math.Pow(10, -scale);
        }

        return new GribFixedSurface(type, value);
    }

    private static double DeriveLongitudeIncrement(double lo1, double lo2, int ni, bool west)
    {
        if (ni < 2)
            return 0;

        var span = west ? lo1 - lo2 : lo2 - lo1;
        if (span < 0)
            span += 360;
        return span / (ni - 1);
    }

    private static void Require(ReadOnlySpan<byte> s, int length, int section)
    {
        if (s.Length < length)
        {
            throw new GribFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Section {section} is {s.Length} octets long, too short for its template ({length} octets needed)."));
        }
    }

    private static string DescribeGridTemplate(int template) => template switch
    {
        1 => "rotated latitude/longitude",
        10 => "Mercator",
        20 => "polar stereographic",
        30 => "Lambert conformal",
        40 => "Gaussian latitude/longitude",
        50 => "spherical harmonic coefficients",
        90 => "space view perspective",
        101 => "general unstructured",
        140 => "Lambert azimuthal equal area",
        _ => "see code table 3.1",
    };

    private static string DescribeProductTemplate(int template) => template switch
    {
        1 => "individual ensemble forecast",
        2 => "derived ensemble forecast",
        5 => "probability forecast",
        9 => "probability forecast over a time interval",
        11 => "individual ensemble forecast over a time interval",
        12 => "derived ensemble forecast over a time interval",
        15 => "statistically processed over a spatial area",
        _ => "see code table 4.0",
    };

    private static string DescribeDataTemplate(int template) => template switch
    {
        1 => "matrix values",
        4 => "IEEE floating point",
        40 => "JPEG 2000",
        42 => "CCSDS/AEC",
        50 or 51 => "spectral",
        61 => "simple packing with logarithmic preprocessing",
        200 => "run length",
        _ => "see code table 5.0",
    };
}
