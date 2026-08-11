using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.McpCapabilities;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Covers the viewer's <see cref="ViewerImageRenderer"/> adapter — render
/// delegation and the live-viewport <c>PreferredSize</c> probe. The
/// render_to_image tool's sizing/echo logic is exercised in
/// <c>EncDotNet.S100.Mcp.Tools.Tests</c>.
/// </summary>
public class ViewerImageRendererTests
{
    private sealed class FakeSnapshot : IMapSnapshotRenderer
    {
        public (int W, int H, double D)? LastCall { get; private set; }
        public byte[]? Result { get; set; } = [1, 2, 3];

        public Task<byte[]?> RenderCurrentViewToPngAsync(
            int widthPx, int heightPx, double pixelDensity, CancellationToken ct = default)
        {
            LastCall = (widthPx, heightPx, pixelDensity);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeCoordinates : IMapCoordinateConverter
    {
        public (double Width, double Height)? ViewportSize { get; set; }
        public (double Width, double Height)? TryGetViewportSizePx() => ViewportSize;
        public GeoPosition? TryScreenToWgs84(double xPx, double yPx) => null;
        public GeoPosition? TryImagePixelToWgs84(double x, double y, int w, int h) => null;
    }

    [Fact]
    public async Task RenderToPngAsync_delegates_to_the_snapshot_renderer()
    {
        var snapshot = new FakeSnapshot();
        var sut = new ViewerImageRenderer(snapshot, coordinates: null);

        var bytes = await sut.RenderToPngAsync(640, 480, 2.0);

        Assert.Equal([1, 2, 3], bytes);
        Assert.Equal((640, 480, 2.0), snapshot.LastCall);
    }

    [Fact]
    public void PreferredSize_rounds_the_live_viewport_size()
    {
        var coords = new FakeCoordinates { ViewportSize = (1599.6, 900.2) };
        var sut = new ViewerImageRenderer(new FakeSnapshot(), coords);

        Assert.Equal((1600, 900), sut.PreferredSize);
    }

    [Fact]
    public void PreferredSize_is_null_without_a_coordinate_converter()
    {
        var sut = new ViewerImageRenderer(new FakeSnapshot(), coordinates: null);

        Assert.Null(sut.PreferredSize);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(double.NaN, 100)]
    [InlineData(100, double.PositiveInfinity)]
    [InlineData(1e18, 100)]
    public void PreferredSize_is_null_for_a_degenerate_viewport(double width, double height)
    {
        var coords = new FakeCoordinates { ViewportSize = (width, height) };
        var sut = new ViewerImageRenderer(new FakeSnapshot(), coords);

        Assert.Null(sut.PreferredSize);
    }
}
