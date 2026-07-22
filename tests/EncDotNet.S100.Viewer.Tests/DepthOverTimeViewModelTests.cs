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
        int? datumCode = 23,
        string? baseSourceId = null)
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
            new BaseDepthResult(baseMetres, source, uncertaintyMetres, datumCode, null, baseSourceId),
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
    public void BaseSourceTooltip_UsesSourceFile_WhenPresent()
    {
        var vm = new DepthOverTimeViewModel(
            Result(baseSourceId: "102NL005_519N043E.H5"),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_SourceData,
                "102NL005_519N043E.H5"),
            vm.BaseSourceTooltip);
    }

    [Fact]
    public void BaseSourceTooltip_FallsBackToLabel_WhenNoSourceFile()
    {
        var vm = new DepthOverTimeViewModel(
            Result(source: BaseDepthSource.Sounding, baseSourceId: null),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.Equal(vm.BaseSourceLabel, vm.BaseSourceTooltip);
    }

    [Fact]
    public void TideSourceTooltip_UsesTideDataset_WhenTidePresent()
    {
        var vm = new DepthOverTimeViewModel(
            Result(withTide: true),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_SourceData,
                "S104DS"),
            vm.TideSourceTooltip);
    }

    [Fact]
    public void TideSourceTooltip_IsEmpty_WhenNoTide()
    {
        var vm = new DepthOverTimeViewModel(
            Result(withTide: false, depths: new[] { double.NaN, double.NaN, double.NaN }),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.Equal(string.Empty, vm.TideSourceTooltip);
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

    [Fact]
    public void IsExpanded_DefaultsToTrue()
    {
        var vm = new DepthOverTimeViewModel(
            Result(), "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.True(vm.IsExpanded);

        vm.IsExpanded = false;
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void DepthNowLabel_ReflectsTidePresence()
    {
        var withTide = new DepthOverTimeViewModel(
            Result(withTide: true), "loc", 51.9, 4.4, DepthUnit.Metres, null, null);
        var noTide = new DepthOverTimeViewModel(
            Result(withTide: false, depths: new[] { double.NaN, double.NaN, double.NaN }),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.Equal(EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_Label_DepthNow, withTide.DepthNowLabel);
        Assert.Equal(EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_Label_DepthStatic, noTide.DepthNowLabel);
    }

    [Fact]
    public void DisplayDepthText_UsesBaseWhenStatic()
    {
        var vm = new DepthOverTimeViewModel(
            Result(baseMetres: 10.0, withTide: false, depths: new[] { double.NaN, double.NaN, double.NaN }),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        // Static readout mirrors the base depth regardless of the global clock.
        Assert.Equal(vm.BaseDepthText, vm.DisplayDepthText);
        Assert.Contains("10", vm.DisplayDepthText);
    }

    [Fact]
    public void DisplayDepthText_TracksNowWhenTidePresent()
    {
        var depths = new[] { 10.0, 12.0, 14.0 };
        var result = Result(baseMetres: 10.0, depths: depths);
        var globalTime = GlobalTimeOver(Times(3));

        var vm = new DepthOverTimeViewModel(
            result, "loc", 51.9, 4.4, DepthUnit.Metres, null, globalTime);

        globalTime.SetCurrentTime(T0.AddHours(2));

        Assert.Equal(vm.DepthNowText, vm.DisplayDepthText);
        Assert.Contains("14", vm.DisplayDepthText);
    }

    [Fact]
    public void SourceKindFlags_ReflectSource()
    {
        AssertSourceKind(BaseDepthSource.Bathymetry, bathymetry: true, sounding: false, chartedArea: false);
        AssertSourceKind(BaseDepthSource.Sounding, bathymetry: false, sounding: true, chartedArea: false);
        AssertSourceKind(BaseDepthSource.DredgedArea, bathymetry: false, sounding: false, chartedArea: true);
        AssertSourceKind(BaseDepthSource.DepthArea, bathymetry: false, sounding: false, chartedArea: true);
    }

    private static void AssertSourceKind(
        BaseDepthSource source, bool bathymetry, bool sounding, bool chartedArea)
    {
        var vm = new DepthOverTimeViewModel(
            Result(source: source), "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.Equal(bathymetry, vm.IsBathymetrySource);
        Assert.Equal(sounding, vm.IsSoundingSource);
        Assert.Equal(chartedArea, vm.IsChartedAreaSource);
    }

    private sealed class FakeTimeAware : ITimeAwareDataset
    {
        public FakeTimeAware(IReadOnlyList<DateTime> times) => AvailableTimes = times;

        public IReadOnlyList<DateTime> AvailableTimes { get; }

        public DateTime? CurrentTime { get; private set; }

        public DateTime? SnapTo(DateTime t) => t;
    }
}
