using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines.Sampling;

/// <summary>
/// Samples an S-102 bathymetric surface: depth and uncertainty at the cell
/// containing the point, with no time dimension. Shared by
/// <see cref="S102DatasetProcessor"/> and the catalog's
/// <see cref="Catalog.S102CoverageData"/>.
/// </summary>
/// <remarks>
/// The site has rank 0 and no run, so when several tiles cover the point the
/// first in catalog order answers. A tile the point is inside the declared
/// envelope of but outside the native grid of (a projected tile's WGS-84
/// bounds are an axis-aligned envelope of the rotated grid) finds nothing,
/// so an overlapping tile that does cover the point is not masked (#479).
/// </remarks>
internal sealed class S102CoverageSampler : ICoverageSampler
{
    /// <summary>The product sampled.</summary>
    public const string Spec = "S-102";

    private const string NoTimeDimension = "S-102 has no time dimension; supply 'Time'/'Times' only for S-104 or S-111";

    private static readonly CoverageSiteSearch Nothing =
        CoverageSiteSearch.None with { WindowUnsupportedReason = NoTimeDimension };

    private readonly S102CoverageSource _source;

    public S102CoverageSampler(S102CoverageSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        // The declared bounds are a cheap early out before a projected
        // tile's reprojection. A geographic grid's own extent is checked
        // by the pick itself, which also reads swapped axis order and
        // 0…360° longitudes that the declared bounds don't (#943).
        if (query.DeclaredBounds is { } bounds
            && GridGeoreferencing.Classify(_source.Dataset.HorizontalCRS) != HorizontalCrsKind.Geographic
            && !Contains(bounds, query.Latitude, query.Longitude))
        {
            return Nothing;
        }

        CoveragePickHelper.SamplePoint? pick;
        try
        {
            // The shared CoveragePickHelper snaps to exactly the same cell as
            // the viewer's coverage pick and S102DatasetProcessor.SampleBaseDepth:
            // reproject the WGS-84 point into the (often projected/UTM) grid
            // CRS, floor to the containing cell, and reject points outside the
            // grid extent. One helper keeps every pick path in agreement near
            // cell edges.
            pick = CoveragePickHelper.Sample(_source, query.Transforms, query.Latitude, query.Longitude);
        }
        catch (Exception ex) when (ex is NotSupportedException or FormatException or OverflowException)
        {
            // The tile declares an unsupported or malformed horizontal CRS,
            // so it cannot be reprojected for sampling. Treat it as not
            // covering the point so an overlapping tile can answer.
            return Nothing;
        }

        // Inside the envelope but outside the native grid; another
        // overlapping tile may still cover the point.
        return pick is null
            ? Nothing
            : new CoverageSiteSearch([new Site(pick)]) { WindowUnsupportedReason = NoTimeDimension };
    }

    private static bool Contains(BoundingBox b, double lat, double lon) =>
        lat >= b.SouthLatitude
        && lat <= b.NorthLatitude
        && lon >= b.WestLongitude
        && lon <= b.EastLongitude;

    private sealed class Site(CoveragePickHelper.SamplePoint pick)
        : CoverageSampleSite(CoverageSiteKind.Grid, rank: 0, run: null, window: null)
    {
        public override CoverageReading Read(DateTimeOffset? requestedTime)
        {
            var noData = pick.NoDataValue;
            double? depth = pick.Values.TryGetValue("depth", out var d) && d != noData ? d : null;
            double? uncertainty = pick.Values.TryGetValue("uncertainty", out var u) && u != noData ? u : null;

            return depth is null
                ? new CoverageReading.NoData(pick.Row, pick.Col, Time: null)
                : new CoverageReading.Value(new DepthSample(depth.Value, uncertainty));
        }
    }
}
