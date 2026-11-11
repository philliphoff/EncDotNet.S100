using EncDotNet.S100.Quantities;
using EncDotNet.S100.Viewer.Services.DynamicSources.OwnShip;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests.DynamicSources.OwnShip;

public sealed class HelmViewModelTests
{
    private sealed class FakeProvider : IOwnShipPositionProvider
    {
        public OwnShipPosition? Current { get; private set; }
        public event EventHandler<OwnShipPosition>? Updated;

        public void Push(OwnShipPosition fix)
        {
            Current = fix;
            Updated?.Invoke(this, fix);
        }
    }

    private sealed class FakeHelmState : IOwnShipHelmState
    {
        public bool IsHeld { get; set; }
        public double TurnRateDegPerSec { get; set; }
        public double CommandedSpeedMs { get; set; }
    }

    private sealed record HelmCall(string Method, double A = 0, double B = 0);

    private sealed class RecordingHelm : IOwnShipHelm
    {
        public List<HelmCall> Calls { get; } = new();

        public void SetState(double latitude, double longitude, double? courseOverGroundDeg = null, double? speedOverGroundMs = null, double? headingDeg = null)
            => Calls.Add(new HelmCall(nameof(SetState), latitude, longitude));
        public void SetCourse(double courseDeg) => Calls.Add(new HelmCall(nameof(SetCourse), courseDeg));
        public void NudgeCourse(double deltaDeg) => Calls.Add(new HelmCall(nameof(NudgeCourse), deltaDeg));
        public void SetSpeed(double speedMs) => Calls.Add(new HelmCall(nameof(SetSpeed), speedMs));
        public void NudgeSpeed(double deltaMs) => Calls.Add(new HelmCall(nameof(NudgeSpeed), deltaMs));
        public void SetTurnRate(double degreesPerSecond) => Calls.Add(new HelmCall(nameof(SetTurnRate), degreesPerSecond));
        public void SteerToward(double latitude, double longitude) => Calls.Add(new HelmCall(nameof(SteerToward), latitude, longitude));
        public void Hold() => Calls.Add(new HelmCall(nameof(Hold)));
        public void Resume() => Calls.Add(new HelmCall(nameof(Resume)));
    }

    private static OwnShipPosition Fix(
        double lat = 50.0, double lon = -1.0, double cog = 90.0, double sogMs = 5.0, double? heading = null)
        => new(lat, lon, Angle.FromDegrees(cog), Speed.FromMetresPerSecond(sogMs), DateTimeOffset.UnixEpoch,
            heading is { } h ? Angle.FromDegrees(h) : null);

    [Fact]
    public void Constructor_SeedsFromCurrentFix()
    {
        var provider = new FakeProvider();
        provider.Push(Fix(cog: 123.0, sogMs: 4.0));
        var state = new FakeHelmState { CommandedSpeedMs = 4.0, TurnRateDegPerSec = 1.5, IsHeld = false };

        using var vm = new HelmViewModel(provider, new RecordingHelm(), state);

        Assert.Equal(123.0, vm.CourseDeg, 3);
        Assert.Equal(4.0, vm.SpeedMs, 3);
        Assert.Equal(1.5, vm.TurnRateDegPerSec, 3);
        Assert.False(vm.IsHeld);
        Assert.NotEqual(string.Empty, vm.CourseText);
    }

    [Fact]
    public void UserEditingCourse_CallsHelm()
    {
        var provider = new FakeProvider();
        provider.Push(Fix(cog: 90.0));
        var helm = new RecordingHelm();

        using var vm = new HelmViewModel(provider, helm, new FakeHelmState());
        helm.Calls.Clear();

        vm.CourseDeg = 200.0;

        Assert.Contains(helm.Calls, c => c.Method == nameof(IOwnShipHelm.SetCourse) && c.A == 200.0);
    }

    [Fact]
    public void UserTogglingHold_CallsHelm()
    {
        var provider = new FakeProvider();
        provider.Push(Fix());
        var helm = new RecordingHelm();

        using var vm = new HelmViewModel(provider, helm, new FakeHelmState());
        helm.Calls.Clear();

        vm.IsHeld = true;
        Assert.Contains(helm.Calls, c => c.Method == nameof(IOwnShipHelm.Hold));

        vm.IsHeld = false;
        Assert.Contains(helm.Calls, c => c.Method == nameof(IOwnShipHelm.Resume));
    }

    [Fact]
    public void PortAndStarboardCommands_NudgeCourse()
    {
        var provider = new FakeProvider();
        provider.Push(Fix());
        var helm = new RecordingHelm();

        using var vm = new HelmViewModel(provider, helm, new FakeHelmState());
        helm.Calls.Clear();

        vm.PortCommand.Execute(null);
        vm.StarboardCommand.Execute(null);

        Assert.Contains(helm.Calls, c => c.Method == nameof(IOwnShipHelm.NudgeCourse) && c.A < 0);
        Assert.Contains(helm.Calls, c => c.Method == nameof(IOwnShipHelm.NudgeCourse) && c.A > 0);
    }

    [Fact]
    public void Refresh_FromPushedFix_DoesNotFeedBackToHelm()
    {
        var provider = new FakeProvider();
        provider.Push(Fix(cog: 90.0, sogMs: 5.0));
        var helm = new RecordingHelm();
        var state = new FakeHelmState { CommandedSpeedMs = 5.0 };

        using var vm = new HelmViewModel(provider, helm, state);
        helm.Calls.Clear();

        // An external actor (MCP/gesture/pirate) drives the helm and a new
        // fix is published; seeding the bound fields must not re-issue
        // helm commands.
        state.CommandedSpeedMs = 9.0;
        state.TurnRateDegPerSec = 3.0;
        provider.Push(Fix(cog: 270.0, sogMs: 9.0));

        Assert.Equal(270.0, vm.CourseDeg, 3);
        Assert.Equal(9.0, vm.SpeedMs, 3);
        Assert.Equal(3.0, vm.TurnRateDegPerSec, 3);
        Assert.Empty(helm.Calls);
    }

    [Fact]
    public void HeadingReadout_PrefersHeadingOverCourse()
    {
        var provider = new FakeProvider();
        provider.Push(Fix(cog: 90.0, heading: 80.0));

        using var vm = new HelmViewModel(provider, new RecordingHelm(), new FakeHelmState());

        Assert.Contains("080", vm.HeadingText);
        Assert.Contains("090", vm.CourseText);
    }
}
