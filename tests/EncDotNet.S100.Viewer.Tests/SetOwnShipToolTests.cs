using System.Collections.Generic;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class SetOwnShipToolTests
{
    private sealed record StateCall(double Lat, double Lon, double? Cog, double? Sog, double? Heading);

    private sealed class RecordingHelm : IOwnShipHelm
    {
        public List<StateCall> States { get; } = new();
        public List<double> Courses { get; } = new();
        public List<double> Speeds { get; } = new();
        public int Holds { get; private set; }
        public int Resumes { get; private set; }

        public void SetState(double latitude, double longitude,
            double? courseOverGroundDeg = null, double? speedOverGroundMs = null, double? headingDeg = null)
            => States.Add(new StateCall(latitude, longitude, courseOverGroundDeg, speedOverGroundMs, headingDeg));

        public void SetCourse(double courseDeg) => Courses.Add(courseDeg);
        public void NudgeCourse(double deltaDeg) { }
        public void SetSpeed(double speedMs) => Speeds.Add(speedMs);
        public void NudgeSpeed(double deltaMs) { }
        public void SetTurnRate(double degreesPerSecond) { }
        public void SteerToward(double latitude, double longitude) { }
        public void Hold() => Holds++;
        public void Resume() => Resumes++;
    }

    private static (SetOwnShipTool tool, RecordingHelm helm) Make()
    {
        var helm = new RecordingHelm();
        return (new SetOwnShipTool(helm), helm);
    }

    [Fact]
    public async Task Position_with_kinematics_calls_SetState()
    {
        var (tool, helm) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(
            Lat: 47.6, Lon: -122.3, Cog: 180.0, Sog: 4.0, Heading: 175.0));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Single(helm.States);
        Assert.Equal(new StateCall(47.6, -122.3, 180.0, 4.0, 175.0), helm.States[0]);
        Assert.Equal(47.6, ok!.Lat);
        Assert.Equal(175.0, ok.Heading);
    }

    [Fact]
    public async Task Course_only_calls_SetCourse_not_SetState()
    {
        var (tool, helm) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Cog: 90.0));

        Assert.True(result.TryGetValue(out _));
        Assert.Empty(helm.States);
        Assert.Equal(new[] { 90.0 }, helm.Courses);
    }

    [Fact]
    public async Task Speed_only_calls_SetSpeed()
    {
        var (tool, helm) = Make();

        await tool.InvokeAsync(new SetOwnShipRequest(Sog: 2.5));

        Assert.Equal(new[] { 2.5 }, helm.Speeds);
    }

    [Fact]
    public async Task Hold_true_calls_Hold()
    {
        var (tool, helm) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Hold: true));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(1, helm.Holds);
        Assert.Equal("hold", ok!.HoldAction);
    }

    [Fact]
    public async Task Hold_false_calls_Resume()
    {
        var (tool, helm) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Hold: false));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(1, helm.Resumes);
        Assert.Equal("resume", ok!.HoldAction);
    }

    [Fact]
    public async Task Lat_without_lon_is_rejected()
    {
        var (tool, helm) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Lat: 10.0));

        Assert.False(result.TryGetValue(out _));
        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Empty(helm.States);
    }

    [Fact]
    public async Task Heading_without_position_is_rejected()
    {
        var (tool, _) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Heading: 90.0));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal("heading", ((InvalidArgument)err!).Parameter);
    }

    [Fact]
    public async Task Empty_request_is_rejected()
    {
        var (tool, _) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest());

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Theory]
    [InlineData(91.0, -122.0)]
    [InlineData(-91.0, -122.0)]
    [InlineData(45.0, 181.0)]
    [InlineData(45.0, -181.0)]
    public async Task Out_of_range_position_is_rejected(double lat, double lon)
    {
        var (tool, helm) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Lat: lat, Lon: lon));

        Assert.True(result.TryGetError(out _));
        Assert.Empty(helm.States);
    }

    [Fact]
    public async Task Negative_speed_is_rejected()
    {
        var (tool, _) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Sog: -1.0));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
        Assert.Equal("sog", ((InvalidArgument)err!).Parameter);
    }

    [Fact]
    public async Task NonFinite_course_is_rejected()
    {
        var (tool, _) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(Cog: double.NaN));

        Assert.True(result.TryGetError(out _));
    }

    [Fact]
    public async Task Resume_and_position_applies_both_in_order()
    {
        var (tool, helm) = Make();

        var result = await tool.InvokeAsync(new SetOwnShipRequest(
            Lat: 1.0, Lon: 2.0, Cog: 45.0, Hold: false));

        Assert.True(result.TryGetValue(out var ok));
        Assert.Equal(1, helm.Resumes);
        Assert.Single(helm.States);
        Assert.Equal(45.0, helm.States[0].Cog);
        Assert.Equal("resume", ok!.HoldAction);
    }
}
