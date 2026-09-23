using EncDotNet.S100.Pipelines;
using global::ProjNet.CoordinateSystems;
using global::ProjNet.CoordinateSystems.Transformations;

namespace EncDotNet.S100.Crs.ProjNet;

/// <summary>
/// Creates CRS transforms backed by ProjNet.
/// Supports WGS84 UTM zones (EPSG:326xx, EPSG:327xx), EPSG:4326 and EPSG:3857
/// (spherical Pseudo-Mercator), and transforms between any of them.
/// </summary>
/// <remarks>
/// This is the only <see cref="ICrsTransformFactory"/> implementation in the
/// repository. It depends solely on ProjNet (no map renderer), so headless
/// consumers can reproject coverage products without linking Mapsui.
/// </remarks>
public sealed class ProjNetCrsTransformFactory : ICrsTransformFactory
{
    /// <inheritdoc />
    public ICrsTransform Create(string sourceCrs, string targetCrs)
    {
        if (string.Equals(sourceCrs, targetCrs, StringComparison.OrdinalIgnoreCase))
            return IdentityCrsTransform.Instance;

        // EPSG:3857 (WGS 84 / Pseudo-Mercator, EPSG method 1024) applies the
        // spherical Mercator formulas to WGS 84 coordinates. ProjNet's
        // Mercator_1SP on the WGS 84 ellipsoid gives the ellipsoidal Mercator
        // instead, whose northings differ by ~20 km at 50°N from the spherical
        // Web Mercator every renderer here (and Mapsui) draws in. Transforms to
        // or from EPSG:3857 therefore go through EPSG:4326 and the spherical
        // formulas.
        if (ParseEpsg(targetCrs) == WebMercatorEpsg)
            return Chain(ToWgs84(sourceCrs), PseudoMercatorTransform.Forward);
        if (ParseEpsg(sourceCrs) == WebMercatorEpsg)
            return Chain(PseudoMercatorTransform.Inverse, FromWgs84(targetCrs));

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

    private const int WebMercatorEpsg = 3857;
    private const int Wgs84Epsg = 4326;

    private ICrsTransform ToWgs84(string crs)
        => ParseEpsg(crs) == Wgs84Epsg ? IdentityCrsTransform.Instance : Create(crs, "EPSG:4326");

    private ICrsTransform FromWgs84(string crs)
        => ParseEpsg(crs) == Wgs84Epsg ? IdentityCrsTransform.Instance : Create("EPSG:4326", crs);

    private static ICrsTransform Chain(ICrsTransform first, ICrsTransform second)
        => first.IsIdentity ? second : second.IsIdentity ? first : new ChainedCrsTransform(first, second);
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

/// <summary>
/// WGS 84 / Pseudo-Mercator (EPSG:3857, EPSG method 1024): the spherical
/// Mercator formulas applied to WGS 84 longitude/latitude with the WGS 84
/// semi-major axis as the sphere radius.
/// </summary>
internal sealed class PseudoMercatorTransform : ICrsTransform
{
    private const double Radius = 6378137.0;
    private const double DegToRad = Math.PI / 180.0;

    public static PseudoMercatorTransform Forward { get; } = new(inverse: false);
    public static PseudoMercatorTransform Inverse { get; } = new(inverse: true);

    private readonly bool _inverse;

    private PseudoMercatorTransform(bool inverse) => _inverse = inverse;

    public (double X, double Y) Transform(double x, double y)
        => _inverse
            ? (x / Radius / DegToRad, (2.0 * Math.Atan(Math.Exp(y / Radius)) - Math.PI / 2.0) / DegToRad)
            : (Radius * x * DegToRad, Radius * Math.Log(Math.Tan(Math.PI / 4.0 + y * DegToRad / 2.0)));

    public bool IsIdentity => false;
}

/// <summary>Applies one transform, then another.</summary>
internal sealed class ChainedCrsTransform(ICrsTransform first, ICrsTransform second) : ICrsTransform
{
    public (double X, double Y) Transform(double x, double y)
    {
        var (ix, iy) = first.Transform(x, y);
        return second.Transform(ix, iy);
    }

    public bool IsIdentity => false;
}
