using EncDotNet.S100.ExchangeSets;
using Xunit;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// Verifies the display-scale-band resolution on
/// <see cref="DatasetDiscoveryMetadata"/> (issue #438 Phase 0): the coarsest
/// (most-permissive, largest) <c>minimumDisplayScale</c> and finest
/// (most-permissive, smallest) <c>maximumDisplayScale</c> are selected across
/// all <c>dataCoverage</c> elements.
/// </summary>
public class DisplayScaleBandTests
{
    private static DatasetDiscoveryMetadata Metadata(params DataCoverage[] coverages) =>
        new() { FileName = "101GB00TEST.000", DataCoverages = coverages };

    [Fact]
    public void ResolveMinimum_NoCoverages_ReturnsNull()
    {
        Assert.Null(Metadata().ResolveMinimumDisplayScale());
        Assert.Null(Metadata().ResolveMaximumDisplayScale());
    }

    [Fact]
    public void ResolveMinimum_SingleCoverage_ReturnsDeclaredValues()
    {
        var metadata = Metadata(new DataCoverage
        {
            MinimumDisplayScale = 90000,
            MaximumDisplayScale = 22500,
        });

        Assert.Equal(90000, metadata.ResolveMinimumDisplayScale());
        Assert.Equal(22500, metadata.ResolveMaximumDisplayScale());
    }

    [Fact]
    public void ResolveMinimum_MultipleCoverages_TakesMostPermissiveEdges()
    {
        var metadata = Metadata(
            new DataCoverage { MinimumDisplayScale = 45000, MaximumDisplayScale = 11000 },
            new DataCoverage { MinimumDisplayScale = 22000, MaximumDisplayScale = 4000 },
            new DataCoverage { MinimumDisplayScale = 12000, MaximumDisplayScale = 6000 });

        // Coarsest edge = largest minimum; finest edge = smallest maximum.
        Assert.Equal(45000, metadata.ResolveMinimumDisplayScale());
        Assert.Equal(4000, metadata.ResolveMaximumDisplayScale());
    }

    [Fact]
    public void ResolveMinimum_IgnoresMissingAndNonPositiveValues()
    {
        var metadata = Metadata(
            new DataCoverage { MinimumDisplayScale = null, MaximumDisplayScale = null },
            new DataCoverage { MinimumDisplayScale = 0, MaximumDisplayScale = 0 },
            new DataCoverage { MinimumDisplayScale = 22000, MaximumDisplayScale = 4000 });

        Assert.Equal(22000, metadata.ResolveMinimumDisplayScale());
        Assert.Equal(4000, metadata.ResolveMaximumDisplayScale());
    }

    [Fact]
    public void ResolveMinimum_AllMissing_ReturnsNull()
    {
        var metadata = Metadata(
            new DataCoverage { MinimumDisplayScale = null, MaximumDisplayScale = null });

        Assert.Null(metadata.ResolveMinimumDisplayScale());
        Assert.Null(metadata.ResolveMaximumDisplayScale());
    }
}
