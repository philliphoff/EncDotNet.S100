using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>
/// Verifies <see cref="SetViewportTool"/> drives the shared
/// <see cref="IViewportController"/> for both the centre+scale and bounding-box
/// forms, and rejects malformed / unsupported requests (#568).
/// </summary>
public class SetViewportToolTests
{
    [Fact]
    public async Task CenterScale_PinsViewport()
    {
        var host = new FakeViewport();
        var tool = new SetViewportTool(Accessor(host));

        var value = AssertOk(await tool.InvokeAsync(new SetViewportRequest(
            CenterLongitude: -1.25, CenterLatitude: 50.5, ScaleDenominator: 50000)));

        Assert.Equal("center", value.Mode);
        Assert.Equal(-1.25, value.CenterLongitude);
        Assert.Equal(50.5, value.CenterLatitude);
        Assert.Equal(50000, value.ScaleDenominator);
        Assert.Equal(0, value.RotationDegrees);
        Assert.Null(value.Previous);

        var applied = Assert.IsType<MapViewport>(host.Current);
        Assert.Equal(-1.25, applied.CenterLongitude);
        Assert.Equal(50.5, applied.CenterLatitude);
        Assert.Equal(50000, applied.ScaleDenominator);
        Assert.Equal(0, applied.RotationDegrees);
    }

    [Fact]
    public async Task CenterScale_ReportsPreviousViewport()
    {
        var host = new FakeViewport();
        await new SetViewportTool(Accessor(host)).InvokeAsync(new SetViewportRequest(
            CenterLongitude: 0, CenterLatitude: 0, ScaleDenominator: 10000));

        var value = AssertOk(await new SetViewportTool(Accessor(host)).InvokeAsync(
            new SetViewportRequest(CenterLongitude: -1.25, CenterLatitude: 50.5, ScaleDenominator: 50000)));

        Assert.Equal("0,0,10000,0", value.Previous);
    }

    [Fact]
    public async Task Bounds_FramesBox()
    {
        var host = new FakeViewport();
        var tool = new SetViewportTool(Accessor(host));

        var value = AssertOk(await tool.InvokeAsync(new SetViewportRequest(
            MinLongitude: -1.5, MinLatitude: 50.0, MaxLongitude: -1.0, MaxLatitude: 50.5)));

        Assert.Equal("bounds", value.Mode);
        var bounds = Assert.IsType<BoundingBox>(host.LastBounds);
        Assert.Equal(-1.5, bounds.WestBoundLongitude);
        Assert.Equal(-1.0, bounds.EastBoundLongitude);
        Assert.Equal(50.0, bounds.SouthBoundLatitude);
        Assert.Equal(50.5, bounds.NorthBoundLatitude);
    }

    [Fact]
    public async Task MixingForms_IsInvalidArgument()
    {
        var host = new FakeViewport();

        Assert.IsType<InvalidArgument>(AssertErr(await new SetViewportTool(Accessor(host))
            .InvokeAsync(new SetViewportRequest(
                CenterLongitude: 0, CenterLatitude: 0, ScaleDenominator: 1000, MinLongitude: -1))));
    }

    [Fact]
    public async Task NeitherForm_IsInvalidArgument()
    {
        var host = new FakeViewport();

        Assert.IsType<InvalidArgument>(AssertErr(await new SetViewportTool(Accessor(host))
            .InvokeAsync(new SetViewportRequest())));
    }

    [Fact]
    public async Task PartialCenterForm_IsInvalidArgument()
    {
        var host = new FakeViewport();

        var error = Assert.IsType<InvalidArgument>(AssertErr(await new SetViewportTool(Accessor(host))
            .InvokeAsync(new SetViewportRequest(CenterLongitude: -1.25, CenterLatitude: 50.5))));
        Assert.Equal("centerLongitude", error.Parameter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public async Task NonPositiveScale_IsInvalidArgument(double scale)
    {
        var host = new FakeViewport();

        var error = Assert.IsType<InvalidArgument>(AssertErr(await new SetViewportTool(Accessor(host))
            .InvokeAsync(new SetViewportRequest(
                CenterLongitude: -1.25, CenterLatitude: 50.5, ScaleDenominator: scale))));
        Assert.Equal("scaleDenominator", error.Parameter);
    }

    [Theory]
    [InlineData(200.0)]   // longitude out of range
    [InlineData(-181.0)]
    public async Task CenterLongitudeOutOfRange_IsInvalidArgument(double lon)
    {
        var host = new FakeViewport();

        var error = Assert.IsType<InvalidArgument>(AssertErr(await new SetViewportTool(Accessor(host))
            .InvokeAsync(new SetViewportRequest(
                CenterLongitude: lon, CenterLatitude: 50.5, ScaleDenominator: 50000))));
        Assert.Equal("centerLongitude", error.Parameter);
    }

    [Fact]
    public async Task NonZeroRotation_IsRejected()
    {
        var host = new FakeViewport();

        var error = Assert.IsType<InvalidArgument>(AssertErr(await new SetViewportTool(Accessor(host))
            .InvokeAsync(new SetViewportRequest(
                CenterLongitude: -1.25, CenterLatitude: 50.5, ScaleDenominator: 50000, RotationDegrees: 45))));
        Assert.Equal("rotationDegrees", error.Parameter);
        Assert.Null(host.Current); // nothing applied
    }

    [Fact]
    public async Task ZeroRotation_IsAccepted()
    {
        var host = new FakeViewport();

        AssertOk(await new SetViewportTool(Accessor(host)).InvokeAsync(new SetViewportRequest(
            CenterLongitude: -1.25, CenterLatitude: 50.5, ScaleDenominator: 50000, RotationDegrees: 0)));
        Assert.NotNull(host.Current);
    }

    [Fact]
    public async Task InvertedBounds_IsGeometryInvalid()
    {
        var host = new FakeViewport();

        var error = Assert.IsType<GeometryInvalid>(AssertErr(await new SetViewportTool(Accessor(host))
            .InvokeAsync(new SetViewportRequest(
                MinLongitude: -1.0, MinLatitude: 50.0, MaxLongitude: -1.5, MaxLatitude: 50.5))));
        Assert.Equal("minLongitude", error.Parameter);
    }

    [Fact]
    public async Task ControllerUnattached_IsHostNotReady()
    {
        var tool = new SetViewportTool(new NullCapabilityAccessor<IViewportController>());

        Assert.IsType<HostNotReady>(AssertErr(await tool.InvokeAsync(new SetViewportRequest(
            CenterLongitude: -1.25, CenterLatitude: 50.5, ScaleDenominator: 50000))));
    }

    private static ICapabilityAccessor<IViewportController> Accessor(IViewportController c)
        => new StaticCapabilityAccessor<IViewportController>(c);

    private static TValue AssertOk<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetValue(out var value), "expected a success result");
        return value!;
    }

    private static ToolError AssertErr<TValue>(ToolResult<TValue> result)
    {
        Assert.True(result.TryGetError(out var error), "expected an error result");
        return error!;
    }

    private sealed class NullCapabilityAccessor<T> : ICapabilityAccessor<T> where T : class
    {
        public T? Current => null;
    }

    /// <summary>
    /// Minimal <see cref="IViewportController"/> that records the last applied
    /// viewport / bounds. Mirrors the headless session's mutual-exclusion:
    /// <see cref="Set"/> and <see cref="SetToBounds"/> each clear the other.
    /// </summary>
    private sealed class FakeViewport : IViewportController
    {
        private MapViewport? _viewport;

        public BoundingBox? LastBounds { get; private set; }

        public MapViewport? Current
        {
            get
            {
                if (_viewport is { } v)
                {
                    return v;
                }
                if (LastBounds is { } b)
                {
                    return new MapViewport(
                        (b.WestBoundLongitude + b.EastBoundLongitude) / 2.0,
                        (b.SouthBoundLatitude + b.NorthBoundLatitude) / 2.0,
                        1.0);
                }
                return null;
            }
        }

        public void Set(MapViewport viewport)
        {
            _viewport = viewport;
            LastBounds = null;
        }

        public void SetToBounds(BoundingBox bounds)
        {
            LastBounds = bounds;
            _viewport = null;
        }
    }
}
