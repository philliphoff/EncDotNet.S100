using System;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="ChartTheme"/> and the chart view-models'
/// chrome-theme subscription behaviour.
/// </summary>
public class ChartThemeTests
{
    private sealed class StubThemeService : IThemeService
    {
        public bool IsDarkTheme { get; private set; }
        public event EventHandler? ThemeChanged;
        public bool ToggleTheme()
        {
            IsDarkTheme = !IsDarkTheme;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
            return IsDarkTheme;
        }
        public void RaiseChanged() => ThemeChanged?.Invoke(this, EventArgs.Empty);
        public void Set(bool dark)
        {
            IsDarkTheme = dark;
            ThemeChanged?.Invoke(this, EventArgs.Empty);
        }
        public ChromeTheme Current => IsDarkTheme ? ChromeTheme.Dark : ChromeTheme.Light;
        public void SetTheme(ChromeTheme theme) => Set(ChromeThemes.IsDark(theme));
    }

    private static StationTimeSeriesSnapshot WaterLevelSnapshot()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new StationTimeSeriesSnapshot
        {
            StationId = "ST01",
            Latitude = 0,
            Longitude = 0,
            Times = new[] { start, start.AddMinutes(15) },
            Channels = new[]
            {
                new StationTimeSeriesChannel
                {
                    Key = "waterLevelHeight",
                    DisplayName = "Water Level Height",
                    Unit = "m",
                    Values = new[] { 1f, 2f },
                    FillValue = -9999f,
                },
            },
        };
    }

    private static StationTimeSeriesSnapshot CurrentsSnapshot()
    {
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        return new StationTimeSeriesSnapshot
        {
            StationId = "ST02",
            Latitude = 0,
            Longitude = 0,
            Times = new[] { start, start.AddMinutes(15) },
            Channels = new[]
            {
                new StationTimeSeriesChannel
                {
                    Key = "surfaceCurrentSpeed",
                    DisplayName = "Speed",
                    Unit = "m/s",
                    Values = new[] { 0.1f, 0.2f },
                    FillValue = -9999f,
                },
                new StationTimeSeriesChannel
                {
                    Key = "surfaceCurrentDirection",
                    DisplayName = "Direction",
                    Unit = "deg",
                    Values = new[] { 90f, 180f },
                    FillValue = -9999f,
                },
            },
        };
    }

    private static SKColor GetStrokeColor(LiveChartsCore.ISeries series)
    {
        // Stroke lives on IStrokedAndFilled which the LineSeries
        // implementations satisfy. Cast through that interface to avoid
        // depending on the concrete generic type.
        var strokable = (LiveChartsCore.Kernel.Sketches.IStrokedAndFilled)series;
        var paint = (SolidColorPaint?)strokable.Stroke;
        Assert.NotNull(paint);
        return paint!.Color;
    }

    private static SolidColorPaint GetStrokePaint(LiveChartsCore.ISeries series)
    {
        var strokable = (LiveChartsCore.Kernel.Sketches.IStrokedAndFilled)series;
        var paint = (SolidColorPaint?)strokable.Stroke;
        Assert.NotNull(paint);
        return paint!;
    }

    [Fact]
    public void Resolve_LightAndDark_ProduceDistinctValuesForEveryColour()
    {
        var light = ChartTheme.Resolve(isDark: false);
        var dark = ChartTheme.Resolve(isDark: true);

        Assert.NotEqual(light.SeriesPrimary, dark.SeriesPrimary);
        Assert.NotEqual(light.SeriesSpeed, dark.SeriesSpeed);
        Assert.NotEqual(light.SeriesDirection, dark.SeriesDirection);
        Assert.NotEqual(light.NowMarker, dark.NowMarker);
        Assert.NotEqual(light.AxisLabel, dark.AxisLabel);
        Assert.NotEqual(light.AxisName, dark.AxisName);
        Assert.NotEqual(light.Separator, dark.Separator);
    }

    [Fact]
    public void Resolve_NowMarker_HasFullAlphaOnBothThemes()
    {
        // Section strokes need to be opaque or LiveCharts2 antialiases
        // them away on a busy chart canvas.
        Assert.Equal((byte)0xFF, ChartTheme.Resolve(false).NowMarker.Alpha);
        Assert.Equal((byte)0xFF, ChartTheme.Resolve(true).NowMarker.Alpha);
    }

    [Fact]
    public void Resolve_NoApplicationDependency()
    {
        // Resolve must not require Avalonia.Application.Current — it's
        // pure on its bool input. Verify by simply calling it; if it
        // ever touches Application.Current under test, this throws.
        var theme = ChartTheme.Resolve(isDark: false);
        Assert.NotNull(theme);
    }

    [Fact]
    public void StationTimeSeriesViewModel_DefaultsToLightWhenNoThemeService()
    {
        using var vm = new S104StationTimeSeriesViewModel(WaterLevelSnapshot(), globalTime: null);
        Assert.Equal(ChartTheme.Resolve(false).SeriesPrimary, GetStrokeColor(vm.HeightSeries[0]));
    }

    [Fact]
    public void StationTimeSeriesViewModel_AppliesInitialThemeFromService()
    {
        var theme = new StubThemeService();
        theme.Set(dark: true);
        using var vm = new S104StationTimeSeriesViewModel(WaterLevelSnapshot(), null, null, theme);
        Assert.Equal(ChartTheme.Resolve(true).SeriesPrimary, GetStrokeColor(vm.HeightSeries[0]));
    }

    [Fact]
    public void S104_HeightStroke_FollowsThemeChange()
    {
        var theme = new StubThemeService();
        using var vm = new S104StationTimeSeriesViewModel(WaterLevelSnapshot(), null, null, theme);

        Assert.Equal(ChartTheme.Resolve(false).SeriesPrimary, GetStrokeColor(vm.HeightSeries[0]));

        theme.Set(dark: true);

        Assert.Equal(ChartTheme.Resolve(true).SeriesPrimary, GetStrokeColor(vm.HeightSeries[0]));
        Assert.Equal(ChartTheme.Resolve(true), vm.CurrentChartTheme);
    }

    [Fact]
    public void S111_SpeedAndDirectionStrokes_FollowThemeChange()
    {
        var theme = new StubThemeService();
        using var vm = new S111StationTimeSeriesViewModel(CurrentsSnapshot(), null, null, theme);

        Assert.Equal(ChartTheme.Resolve(false).SeriesSpeed, GetStrokeColor(vm.SpeedSeries[0]));
        Assert.Equal(ChartTheme.Resolve(false).SeriesDirection, GetStrokeColor(vm.DirectionSeries[0]));

        theme.Set(dark: true);

        Assert.Equal(ChartTheme.Resolve(true).SeriesSpeed, GetStrokeColor(vm.SpeedSeries[0]));
        Assert.Equal(ChartTheme.Resolve(true).SeriesDirection, GetStrokeColor(vm.DirectionSeries[0]));
    }

    [Fact]
    public void NowMarkerStroke_FollowsThemeChange()
    {
        var theme = new StubThemeService();
        using var vm = new S104StationTimeSeriesViewModel(WaterLevelSnapshot(), null, null, theme);

        var nowPaint = (SolidColorPaint)vm.Sections[0].Stroke!;
        Assert.Equal(ChartTheme.Resolve(false).NowMarker, nowPaint.Color);

        theme.Set(dark: true);

        Assert.Equal(ChartTheme.Resolve(true).NowMarker, nowPaint.Color);
    }

    [Fact]
    public void Dispose_UnsubscribesFromThemeChanged()
    {
        var theme = new StubThemeService();
        var vm = new S104StationTimeSeriesViewModel(WaterLevelSnapshot(), null, null, theme);
        var stroke = GetStrokePaint(vm.HeightSeries[0]);
        var initial = stroke.Color;

        vm.Dispose();

        // After Dispose the VM must no longer respond to theme changes.
        theme.Set(dark: true);
        Assert.Equal(initial, stroke.Color);
    }
}
