using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers <see cref="MapsuiMapHost.NormalizePreferredSize"/> — the rounding and
/// range-checking that backs the host's <c>IImageRenderer.PreferredSize</c> from
/// its live viewport size. The render_to_image tool's sizing/echo logic is
/// exercised in <c>EncDotNet.S100.Mcp.Tools.Tests</c>.
/// </summary>
public class MapsuiMapHostPreferredSizeTests
{
    [Fact]
    public void NormalizePreferredSize_rounds_the_live_viewport_size()
    {
        Assert.Equal((1600, 900), MapsuiMapHost.NormalizePreferredSize((1599.6, 900.2)));
    }

    [Fact]
    public void NormalizePreferredSize_is_null_without_a_viewport_size()
    {
        Assert.Null(MapsuiMapHost.NormalizePreferredSize(null));
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    [InlineData(1e18, 100)]
    public void NormalizePreferredSize_is_null_for_a_degenerate_viewport(double width, double height)
    {
        Assert.Null(MapsuiMapHost.NormalizePreferredSize((width, height)));
    }
}
