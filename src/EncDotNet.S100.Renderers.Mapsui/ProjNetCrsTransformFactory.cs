using EncDotNet.S100.Pipelines;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Creates CRS transforms backed by ProjNet.
/// Supports WGS84 UTM zones (EPSG:326xx, EPSG:327xx) and EPSG:4326 ↔ EPSG:3857.
/// </summary>
public sealed class ProjNetCrsTransformFactory : ICrsTransformFactory
{
    public ICrsTransform Create(string sourceCrs, string targetCrs)
    {
        if (string.Equals(sourceCrs, targetCrs, StringComparison.OrdinalIgnoreCase))
            return IdentityCrsTransform.Instance;

        var source = ResolveCoordinateSystem(sourceCrs);
        var target = ResolveCoordinateSystem(targetCrs);

        var mathTransform = new CoordinateTransformationFactory()
            .CreateFromCoordinateSystems(source, target)
            .MathTransform;

        return new ProjNetCrsTransform(mathTransform);
    }

    private static CoordinateSystem ResolveCoordinateSystem(string crs)
    {
        var epsg = ParseEpsg(crs);

        return epsg switch
        {
            4326 => GeographicCoordinateSystem.WGS84,
            3857 => CreateWebMercator(),
            >= 32601 and <= 32660 => ProjectedCoordinateSystem.WGS84_UTM(epsg - 32600, true),
            >= 32701 and <= 32760 => ProjectedCoordinateSystem.WGS84_UTM(epsg - 32700, false),
            _ => throw new NotSupportedException($"Unsupported CRS: {crs}"),
        };
    }

    private static int ParseEpsg(string crs)
    {
        // Accept "EPSG:4326", "4326", "EPSG:32608"
        var span = crs.AsSpan();
        if (span.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase))
            span = span[5..];
        return int.Parse(span);
    }

    private static CoordinateSystem CreateWebMercator()
    {
        // ProjNet doesn't have a built-in EPSG:3857; parse from WKT
        const string wkt = """
            PROJCS["WGS 84 / Pseudo-Mercator",
                GEOGCS["WGS 84",
                    DATUM["WGS_1984",
                        SPHEROID["WGS 84",6378137,298.257223563]],
                    PRIMEM["Greenwich",0],
                    UNIT["degree",0.0174532925199433]],
                PROJECTION["Mercator_1SP"],
                PARAMETER["central_meridian",0],
                PARAMETER["scale_factor",1],
                PARAMETER["false_easting",0],
                PARAMETER["false_northing",0],
                UNIT["metre",1],
                AUTHORITY["EPSG","3857"]]
            """;
        return new CoordinateSystemFactory().CreateFromWkt(wkt);
    }
}

/// <summary>
/// A CRS transform backed by a ProjNet <see cref="MathTransform"/>.
/// </summary>
internal sealed class ProjNetCrsTransform : ICrsTransform
{
    private readonly MathTransform _mathTransform;

    public ProjNetCrsTransform(MathTransform mathTransform)
    {
        _mathTransform = mathTransform;
    }

    public (double X, double Y) Transform(double x, double y)
        => _mathTransform.Transform(x, y);

    public bool IsIdentity => false;
}
