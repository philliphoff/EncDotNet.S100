using System;
using EncDotNet.S100.Core;
using System.Collections.Generic;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="CoveragePickHelper"/>: confirms the
/// helper reprojects geographic clicks into the grid CRS and snaps
/// to the containing cell, and rejects clicks outside the grid extent.
/// </summary>
public class CoveragePickHelperTests
{
    [Fact]
    public void Sample_InBoundsCell_ReturnsExpectedRowColAndValue()
    {
        var source = BuildStubSource(originLat: 10.0, originLon: 20.0, spacing: 1.0,
            depths: new[,]
            {
                { 1f, 2f, 3f },
                { 4f, 5f, 6f },
                { 7f, 8f, 9f },
            });

        // Click at (lat 11.5, lon 21.5) → row 1, col 1 (centre cell).
        var result = CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 11.5, longitude: 21.5);

        Assert.NotNull(result);
        Assert.Equal(1, result!.Row);
        Assert.Equal(1, result.Col);
        Assert.Equal(5f, result.Values["depth"]);
    }

    [Fact]
    public void Sample_OutOfExtentClick_ReturnsNull()
    {
        var source = BuildStubSource(originLat: 10.0, originLon: 20.0, spacing: 1.0,
            depths: new[,] { { 1f, 2f }, { 3f, 4f } });

        // Click far north of the grid.
        Assert.Null(CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 50.0, longitude: 21.0));

        // Click south of the origin (negative row).
        Assert.Null(CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 5.0, longitude: 21.0));
    }

    [Fact]
    public void Sample_NoDataCell_ReturnsCellWithFillValue()
    {
        const float fill = 1_000_000f;
        var source = BuildStubSource(originLat: 0.0, originLon: 0.0, spacing: 1.0, fill: fill,
            depths: new[,]
            {
                { fill, 2f },
                { 3f, 4f },
            });

        var result = CoveragePickHelper.Sample(source, IdentityFactory.Instance, latitude: 0.5, longitude: 0.5);
        Assert.NotNull(result);
        Assert.Equal(fill, result!.Values["depth"]);
        Assert.Equal(fill, result.NoDataValue);
    }

    private static StubCoverageSource BuildStubSource(
        double originLat,
        double originLon,
        double spacing,
        float[,] depths,
        float fill = 1_000_000f)
    {
        var rows = depths.GetLength(0);
        var cols = depths.GetLength(1);
        var grid = new GridMetadata
        {
            NumRows = rows,
            NumColumns = cols,
            OriginLatitude = originLat,
            OriginLongitude = originLon,
            SpacingLatitudinal = spacing,
            SpacingLongitudinal = spacing,
        };
        var meta = new CoverageMetadata
        {
            Spec = new SpecRef("S-102", default),
            Extent = new BoundingBox(
                southLatitude: originLat,
                westLongitude: originLon,
                northLatitude: originLat + rows * spacing,
                eastLongitude: originLon + cols * spacing),
            GridMetadata = grid,
            HorizontalCRS = "EPSG:4326",
            VerticalDatum = "MSL",
            NoDataValue = fill,
            ValueFields =
            [
                new CoverageValueField { Name = "depth", Type = CoverageValueType.Float, Units = "m", FillValue = fill },
            ],
        };
        return new StubCoverageSource(meta, depths);
    }

    private sealed class StubCoverageSource : ICoverageSource
    {
        private readonly float[,] _depths;
        public StubCoverageSource(CoverageMetadata metadata, float[,] depths)
        {
            Metadata = metadata;
            _depths = depths;
        }
        public CoverageMetadata Metadata { get; }
        public IReadOnlyList<DateTime> AvailableTimes => Array.Empty<DateTime>();
        public void SelectTime(DateTime time) { }
        public SampledCoverage Sample(GridRegion region)
        {
            var rs = region.RowStart ?? 0;
            var re = region.RowEnd ?? Metadata.GridMetadata.NumRows;
            var cs = region.ColStart ?? 0;
            var ce = region.ColEnd ?? Metadata.GridMetadata.NumColumns;
            var rows = re - rs;
            var cols = ce - cs;
            var slice = new float[rows * cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    slice[r * cols + c] = _depths[rs + r, cs + c];
            return new SampledCoverage
            {
                Region = region,
                Metadata = Metadata.GridMetadata,
                Values = new Dictionary<string, float[]> { ["depth"] = slice },
            };
        }
    }

    private sealed class IdentityFactory : ICrsTransformFactory
    {
        public static readonly IdentityFactory Instance = new();
        public ICrsTransform Create(string sourceCrs, string targetCrs) => IdentityCrsTransform.Instance;
    }
}
