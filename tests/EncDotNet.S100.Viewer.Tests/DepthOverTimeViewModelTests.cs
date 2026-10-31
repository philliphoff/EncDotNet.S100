using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.ViewModels;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;

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
        int? waterLevelDatumCode = null,
        bool withTide = true,
        double[]? depths = null,
        int? datumCode = 23,
        string? baseSourceId = null,
        double? waterLevelUncertainty = null)
    {
        // Built through the real assimilation, from a series whose heights
        // give the requested adjusted depths.
        var times = Times(depths?.Length ?? 3);
        var heights = times
            .Select((_, i) => depths is not null ? (double.IsNaN(depths[i]) ? (double?)null : depths[i] - baseMetres) : i)
            .ToList();
        var series = new StepWaterLevelSeries("S104DS", waterLevelDatumCode ?? datumCode, times, heights, waterLevelUncertainty);
        var baseDepth = new BaseDepthResult(baseMetres, source, uncertaintyMetres, datumCode, null, baseSourceId);
        return new DepthAssimilationService().Assimilate(baseDepth, withTide ? [series] : [])!;
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
    public void Series_NoDataStep_EmitsNullGapPointForEveryTimeStep()
    {
        // A NoData tide step yields a null DepthMeters; the curve must still
        // carry a point at that time (with a null value) so the line breaks at
        // the gap rather than bridging across it.
        var times = Times(3);
        var points = new List<DepthOverTimePoint>
        {
            new(times[0], 10.0),
            new(times[1], null),
            new(times[2], 12.0),
        };
        var result = new LocationDepthResult(
            new BaseDepthResult(10.0, BaseDepthSource.Bathymetry, null, 23, null, "S102DS"),
            new LocationTideSelection("S104DS", 23, []),
            points,
            UncertaintyMeters: null);

        var vm = new DepthOverTimeViewModel(
            result, "loc", 51.9, 4.4, DepthUnit.Metres, safetyDepthMetres: null, globalTime: null);

        var curve = Assert.IsType<LineSeries<DateTimePoint>>(vm.DepthSeries[^1]);
        var values = Assert.IsAssignableFrom<IEnumerable<DateTimePoint>>(curve.Values).ToList();
        Assert.Equal(3, values.Count);
        Assert.NotNull(values[0].Value);
        Assert.Null(values[1].Value);
        Assert.NotNull(values[2].Value);
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
    public void Refusal_names_both_datums_when_they_differ()
    {
        // S-102 on MLLW (12), S-104 on LAT (23).
        var vm = new DepthOverTimeViewModel(
            Result(datumCode: 12, waterLevelDatumCode: 23),
            "loc",
            51.9,
            4.4,
            DepthUnit.Metres,
            safetyDepthMetres: null,
            globalTime: null);

        Assert.True(vm.HasDatumNote);
        Assert.False(vm.HasTide);
        Assert.False(vm.ShowNoTideInfo);
        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_Refused_DifferentDatum,
                "S-102",
                "Mean Lower Low Water",
                "Lowest Astronomical Tide"),
            vm.DatumNoteText);
        Assert.Equal(vm.BaseDepthText, vm.DisplayDepthText);
    }

    [Fact]
    public void Refusal_names_the_product_without_a_datum()
    {
        var vm = new DepthOverTimeViewModel(
            Result(source: BaseDepthSource.Sounding, datumCode: null, waterLevelDatumCode: 23),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.True(vm.HasDatumNote);
        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_Refused_UndeclaredDatum,
                "S-101"),
            vm.DatumNoteText);
    }

    [Fact]
    public void No_refusal_when_datums_agree()
    {
        var vm = new DepthOverTimeViewModel(Result(), "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        Assert.False(vm.HasDatumNote);
        Assert.Equal(string.Empty, vm.DatumNoteText);
    }

    [Fact]
    public void Uncertainty_combines_s102_and_s104_at_the_timeline_time()
    {
        var globalTime = GlobalTimeOver(Times(3));
        var vm = new DepthOverTimeViewModel(
            Result(uncertaintyMetres: 0.5, waterLevelUncertainty: 0.3),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, globalTime);

        globalTime.SetCurrentTime(T0.AddHours(1));

        // √(0.5² + 0.3²) ≈ 0.58 m.
        Assert.Equal(
            string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_CombinedUncertainty,
                DepthFormatting.Format(Math.Sqrt(0.34), DepthUnit.Metres),
                DepthFormatting.Format(0.5, DepthUnit.Metres),
                DepthFormatting.Format(0.3, DepthUnit.Metres)),
            vm.UncertaintyText);
    }

    [Fact]
    public void Uncertainty_says_when_s104_carries_none()
    {
        var globalTime = GlobalTimeOver(Times(3));
        var vm = new DepthOverTimeViewModel(
            Result(uncertaintyMetres: 0.5),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, globalTime);

        globalTime.SetCurrentTime(T0.AddHours(1));

        Assert.Contains(EncDotNet.S100.Viewer.Resources.Strings.Pick_Depth_WaterLevelUncertaintyNotProvided, vm.UncertaintyText);
    }

    [Fact]
    public void Uncertainty_band_is_the_combined_uncertainty()
    {
        var vm = new DepthOverTimeViewModel(
            Result(uncertaintyMetres: 0.4, waterLevelUncertainty: 0.3, depths: [10.0, 10.0, 10.0]),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, null);

        var upper = Assert.IsType<LineSeries<DateTimePoint>>(vm.DepthSeries[0]);
        var values = Assert.IsAssignableFrom<IEnumerable<DateTimePoint>>(upper.Values).ToList();
        Assert.All(values, v => Assert.Equal(10.5, v.Value!.Value, 6));
    }

    [Fact]
    public void Readout_interpolates_between_steps()
    {
        var globalTime = GlobalTimeOver(Times(3));
        var vm = new DepthOverTimeViewModel(
            Result(baseMetres: 10.0, depths: [10.0, 12.0, 14.0]),
            "loc", 51.9, 4.4, DepthUnit.Metres, null, globalTime);

        globalTime.SetCurrentTime(T0.AddMinutes(30));

        // The passage check's height at 00:30 is 1 m, not the nearest step's.
        Assert.Equal(DepthFormatting.Format(11.0, DepthUnit.Metres), vm.DepthNowText);
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
