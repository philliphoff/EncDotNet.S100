using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class DepthOverTimeViewModelTests
{
    private static readonly DateTime T0 = new(2030, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    private static IReadOnlyList<DateTime> Times(int n)
    {
        var times = new List<DateTime>(n);
        for (var i = 0; i < n; i++)
            times.Add(T0.AddHours(i));
        return times;
    }

    private static LocationDepthResult Result(
        double baseMetres = 10.0,
        BaseDepthSource source = BaseDepthSource.Bathymetry,
        double? uncertaintyMetres = null,
        bool datumsNotReconciled = false,
        bool withTide = true,
        double[]? depths = null,
        int? datumCode = 23)
    {
        var times = Times(depths?.Length ?? 3);
        var points = new List<DepthOverTimePoint>(times.Count);
        for (var i = 0; i < times.Count; i++)
        {
            double? d = depths is not null ? depths[i] : baseMetres + i;
            points.Add(new DepthOverTimePoint(times[i], d));
        }

        var tide = withTide ? new LocationTideSelection("S104DS", datumCode) : null;

        return new LocationDepthResult(
            new BaseDepthResult(baseMetres, source, uncertaintyMetres, datumCode, null),
            tide,
            points,
            uncertaintyMetres,
            datumsNotReconciled);
    }

    private static GlobalTimeService GlobalTimeOver(IReadOnlyList<DateTime> times)
    {
        var globalTime = new GlobalTimeService();
        globalTime.Register(new DatasetEntry("/tmp/depth.h5", "S-104"), new FakeTimeAware(times));
        return globalTime;
    }

    [Fact]
    public void Series_WithoutUncertainty_HasSingleCurve()
    {
        var vm = new DepthOverTimeViewModel(
            Result(uncertaintyMetres: null),
            "51.9 N, 4.4 E",
            51.9,
            4.4,
            DepthUnit.Metres,
            safetyDepthMetres: null,
            globalTime: null);

        Assert.Single(vm.DepthSeries);
        Assert.False(vm.HasUncertainty);
    }

    [Fact]
    public void Series_WithUncertainty_HasBoundaryLinesPlusCurve()
    {
        var vm = new DepthOverTimeViewModel(
            Result(uncertaintyMetres: 0.5),
            "loc",
            51.9,
            4.4,
            DepthUnit.Metres,
            safetyDepthMetres: null,
            globalTime: null);

        Assert.Equal(3, vm.DepthSeries.Length);
        Assert.True(vm.HasUncertainty);
        Assert.Contains("0.5", vm.UncertaintyText);
    }

    [Fact]
    public void SafetyDepth_AddsSectionBeyondNowMarker()
    {
        var withSafety = new DepthOverTimeViewModel(
            Result(),
            "loc",
            51.9,
            4.4,
            DepthUnit.Metres,
            safetyDepthMetres: 8.0,
            globalTime: null);

        var withoutSafety = new DepthOverTimeViewModel(
            Result(),
            "loc",
            51.9,
            4.4,
            DepthUnit.Metres,
            safetyDepthMetres: null,
            globalTime: null);

        Assert.True(withSafety.HasSafetyDepth);
        Assert.False(withoutSafety.HasSafetyDepth);
        Assert.Equal(withoutSafety.DepthSections.Count + 1, withSafety.DepthSections.Count);
    }

    [Fact]
    public void DatumNote_ExposedWhenNotReconciled()
    {
        var vm = new DepthOverTimeViewModel(
            Result(datumsNotReconciled: true),
            "loc",
            51.9,
            4.4,
            DepthUnit.Metres,
            safetyDepthMetres: null,
            globalTime: null);

        Assert.True(vm.HasDatumNote);
        Assert.False(string.IsNullOrWhiteSpace(vm.DatumNoteText));
    }

    [Fact]
    public void BaseSourceLabel_ReflectsSource()
    {
        var sounding = new DepthOverTimeViewModel(
            Result(source: BaseDepthSource.Sounding),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.Equal(EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_Source_Sounding, sounding.BaseSourceLabel);
    }

    [Fact]
    public void NoTide_FlagsAbsentSeries()
    {
        var vm = new DepthOverTimeViewModel(
            Result(withTide: false, depths: new[] { double.NaN, double.NaN, double.NaN }),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.False(vm.HasTide);
    }

    [Fact]
    public void Readout_TracksGlobalTime()
    {
        var depths = new[] { 10.0, 12.0, 14.0 };
        var result = Result(baseMetres: 10.0, depths: depths);
        var globalTime = GlobalTimeOver(Times(3));

        var vm = new DepthOverTimeViewModel(
            result, "loc", 51.9, 4.4, DepthUnit.Metres, null, globalTime);

        globalTime.SetCurrentTime(T0.AddHours(2));

        // Depth at t2 is 14 m; tide = 14 - baseline 10 = 4 m.
        Assert.Contains("14", vm.DepthNowText);
        Assert.Contains("4", vm.TideNowText);
    }

    [Fact]
    public void Readout_ConvertsToFeet()
    {
        var depths = new[] { 10.0, 10.0, 10.0 };
        var result = Result(baseMetres: 10.0, depths: depths);
        var globalTime = GlobalTimeOver(Times(3));

        var vm = new DepthOverTimeViewModel(
            result, "loc", 51.9, 4.4, DepthUnit.Feet, null, globalTime);

        globalTime.SetCurrentTime(T0.AddHours(1));

        // 10 m ≈ 32.8 ft.
        Assert.Contains("32", vm.DepthNowText);
    }

    [Fact]
    public void Readout_UnavailableWhenNoGlobalTime()
    {
        var vm = new DepthOverTimeViewModel(
            Result(),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, globalTime: null);

        Assert.Equal(EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_Value_Unavailable, vm.DepthNowText);
    }

    private sealed class FakeTimeAware : ITimeAwareDataset
    {
        public FakeTimeAware(IReadOnlyList<DateTime> times) => AvailableTimes = times;

        public IReadOnlyList<DateTime> AvailableTimes { get; }

        public DateTime? CurrentTime { get; private set; }

        public DateTime? SnapTo(DateTime t) => t;
    }
}
