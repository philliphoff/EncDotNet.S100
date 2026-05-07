using System.Collections.Generic;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class ExchangeSetServiceUnionBoundingBoxTests
{
    private static DatasetDiscoveryMetadata Dataset(BoundingBox? bbox) =>
        new()
        {
            FileName = "x",
            ProductSpecification = null,
            BoundingBox = bbox,
        };

    private static BoundingBox Box(double w, double e, double s, double n) =>
        new()
        {
            WestBoundLongitude = w,
            EastBoundLongitude = e,
            SouthBoundLatitude = s,
            NorthBoundLatitude = n,
        };

    [Fact]
    public void ReturnsNull_WhenNoDatasetsHaveBoundingBox()
    {
        var datasets = new List<DatasetDiscoveryMetadata>
        {
            Dataset(null), Dataset(null),
        };

        var union = ExchangeSetService.ComputeUnionBoundingBox(datasets);

        Assert.Null(union);
    }

    [Fact]
    public void ReturnsSingleBox_WhenOneDatasetHasBoundingBox()
    {
        var datasets = new List<DatasetDiscoveryMetadata>
        {
            Dataset(Box(-10, 10, -5, 5)), Dataset(null),
        };

        var union = ExchangeSetService.ComputeUnionBoundingBox(datasets);

        Assert.NotNull(union);
        Assert.Equal(-10, union!.WestBoundLongitude);
        Assert.Equal(10, union.EastBoundLongitude);
        Assert.Equal(-5, union.SouthBoundLatitude);
        Assert.Equal(5, union.NorthBoundLatitude);
    }

    [Fact]
    public void Unions_TwoDisjointBoxes()
    {
        var datasets = new List<DatasetDiscoveryMetadata>
        {
            Dataset(Box(-10, -5, 0, 5)),
            Dataset(Box(10, 20, 30, 40)),
        };

        var union = ExchangeSetService.ComputeUnionBoundingBox(datasets);

        Assert.NotNull(union);
        Assert.Equal(-10, union!.WestBoundLongitude);
        Assert.Equal(20, union.EastBoundLongitude);
        Assert.Equal(0, union.SouthBoundLatitude);
        Assert.Equal(40, union.NorthBoundLatitude);
    }

    [Fact]
    public void IgnoresDatasetsWithoutBoundingBox()
    {
        var datasets = new List<DatasetDiscoveryMetadata>
        {
            Dataset(null),
            Dataset(Box(0, 1, 0, 1)),
            Dataset(null),
            Dataset(Box(2, 3, 2, 3)),
        };

        var union = ExchangeSetService.ComputeUnionBoundingBox(datasets);

        Assert.NotNull(union);
        Assert.Equal(0, union!.WestBoundLongitude);
        Assert.Equal(3, union.EastBoundLongitude);
        Assert.Equal(0, union.SouthBoundLatitude);
        Assert.Equal(3, union.NorthBoundLatitude);
    }
}
