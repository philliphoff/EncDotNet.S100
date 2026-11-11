using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Pipelines.Tests;

public class SvgPivotMetricsTests
{
    [Fact]
    public void TryParse_ReturnsZeroOffset_WhenPivotEqualsBoundsCenter()
    {
        // Symmetric viewBox around (0, 0) with pivot at (0, 0): no shift
        // needed to align pivot with bounds centre.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="4mm" viewBox="-2 -2 4 4">
              <circle class="pivotPoint" cx="0" cy="0" r="1"/>
            </svg>
            """;

        var metrics = SvgPivotMetrics.TryParse(svg);

        Assert.NotNull(metrics);
        Assert.Equal(0.0, metrics!.PivotToBoundsCenterMm.X, 6);
        Assert.Equal(0.0, metrics.PivotToBoundsCenterMm.Y, 6);
        Assert.Equal(0.0, metrics.RelativeOffset.X, 6);
        Assert.Equal(0.0, metrics.RelativeOffset.Y, 6);
    }

    [Fact]
    public void TryParse_ReturnsNegativeXOffset_ForLeftAlignedSymbol()
    {
        // SOUNDG15 (integer "5") — viewBox sits to the left of pivot (0,0),
        // so the bbox centre is to the left and the pivot-to-centre vector
        // is negative-X.  Renderers must shift the bitmap left to keep the
        // pivot at the anchor.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="2.07mm" height="2.82mm" viewBox="-1.91 -1.41 2.07 2.82">
              <circle class="pivotPoint" cx="0" cy="0" r="1"/>
            </svg>
            """;

        var metrics = SvgPivotMetrics.TryParse(svg);

        Assert.NotNull(metrics);
        // viewBox centre = (-1.91 + 2.07/2, -1.41 + 2.82/2) = (-0.875, 0.0)
        Assert.Equal(-0.875, metrics!.PivotToBoundsCenterMm.X, 6);
        Assert.Equal(0.0, metrics.PivotToBoundsCenterMm.Y, 6);
        // Same offset as a fraction of viewBox size
        Assert.Equal(-0.875 / 2.07, metrics.RelativeOffset.X, 6);
        Assert.Equal(0.0, metrics.RelativeOffset.Y, 6);
    }

    [Fact]
    public void TryParse_ReturnsPositiveOffset_ForRightAlignedFractionalSymbol()
    {
        // SOUNDG50 (fractional ".0") — viewBox sits to the right of and
        // below pivot (0,0).  Renderers must shift the bitmap right and
        // down to keep the pivot at the anchor.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="2.07mm" height="2.82mm" viewBox="-0.16 -0.16 2.07 2.82">
              <circle class="pivotPoint" cx="0" cy="0" r="1"/>
            </svg>
            """;

        var metrics = SvgPivotMetrics.TryParse(svg);

        Assert.NotNull(metrics);
        // viewBox centre = (-0.16 + 2.07/2, -0.16 + 2.82/2) = (0.875, 1.25)
        Assert.Equal(0.875, metrics!.PivotToBoundsCenterMm.X, 6);
        Assert.Equal(1.25, metrics.PivotToBoundsCenterMm.Y, 6);
        Assert.Equal(0.875 / 2.07, metrics.RelativeOffset.X, 6);
        Assert.Equal(1.25 / 2.82, metrics.RelativeOffset.Y, 6);
    }

    [Fact]
    public void TryParse_HonoursCommaSeparatedViewBox()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="-1, -1, 2, 2">
              <circle class="pivotPoint" cx="0" cy="0" r="1"/>
            </svg>
            """;

        var metrics = SvgPivotMetrics.TryParse(svg);

        Assert.NotNull(metrics);
        Assert.Equal(0.0, metrics!.PivotToBoundsCenterMm.X, 6);
    }

    [Fact]
    public void TryParse_HonoursNonZeroPivot()
    {
        // Bounds centre at (1, 1); pivot at (0.25, 0.5) → offset (0.75, 0.5).
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 2 2">
              <circle class="pivotPoint" cx="0.25" cy="0.5" r="0.1"/>
            </svg>
            """;

        var metrics = SvgPivotMetrics.TryParse(svg);

        Assert.NotNull(metrics);
        Assert.Equal(0.75, metrics!.PivotToBoundsCenterMm.X, 6);
        Assert.Equal(0.5, metrics.PivotToBoundsCenterMm.Y, 6);
    }

    [Fact]
    public void TryParse_ReturnsNull_WhenPivotPointMissing()
    {
        // Some symbols (e.g. area-fill pattern tiles) have no pivot
        // declaration; callers should treat the offset as zero.
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 4 4">
              <rect x="0" y="0" width="4" height="4"/>
            </svg>
            """;

        Assert.Null(SvgPivotMetrics.TryParse(svg));
    }

    [Fact]
    public void TryParse_ReturnsNull_WhenViewBoxMissing()
    {
        var svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="4mm" height="4mm">
              <circle class="pivotPoint" cx="0" cy="0" r="1"/>
            </svg>
            """;

        Assert.Null(SvgPivotMetrics.TryParse(svg));
    }

    [Fact]
    public void TryParse_ReturnsNull_OnMalformedXml()
    {
        Assert.Null(SvgPivotMetrics.TryParse("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 1 1\""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryParse_ReturnsNull_OnEmptyInput(string? content)
    {
        Assert.Null(SvgPivotMetrics.TryParse(content!));
    }
}
