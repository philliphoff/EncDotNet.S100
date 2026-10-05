using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Rendered frames of real views, verified against committed snapshots in
/// <c>Snapshots/ViewFrameTests/</c> with the perceptual comparer the chart
/// renders use. They catch what binding and input tests cannot: a style,
/// template or theme resource that changes what the user sees.
/// </summary>
/// <remarks>
/// A change that alters a view on purpose fails here too: check the
/// <c>*.received.png</c> against the <c>*.verified.png</c> and, if it is right,
/// replace the verified file with it.
/// </remarks>
public sealed class ViewFrameTests
{
    private static readonly DateTime Run = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    [AvaloniaTheory]
    [InlineData(ChromeTheme.Light)]
    [InlineData(ChromeTheme.S100Dusk)]
    [InlineData(ChromeTheme.S100Night)]
    public Task Datasets_panel_with_its_inspector(ChromeTheme theme)
    {
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        datasets.Add("/data/US5OTHER.000", "S-57");
        datasets.SelectDataset(datasets.Add("/data/US5SEAFL.000", "S-57"));
        using var host = ViewHost.Show(new DatasetsView { DataContext = datasets }, width: 420, height: 640, theme);
        host.Click(host.Find<Avalonia.Controls.TabItem>("Datasets.DatasetsTab"));

        return Verify(host.CaptureFrame(), "png").UseParameters(theme);
    }

    [AvaloniaFact]
    public Task Time_hud_pinned_off_now()
    {
        var service = new GlobalTimeService();
        var timeline = new TimelineViewModel(
            service, new UtcTimeFormat(), new FakeTimeProvider(new DateTimeOffset(Run.AddHours(5))), action => action());
        var samples = Enumerable.Range(0, 49).Select(h => Run.AddHours(h)).ToArray();
        service.ApplySnapshot(new MapsuiMapTimeSnapshot
        {
            Minimum = samples[0],
            Maximum = samples[^1],
            Current = samples[0],
            Samples = samples,
            CoverageSegments = [new MapsuiMapTimeSegment(samples[0], samples[^1])],
            Datasets = [new MapsuiMapTimedDataset("111US00_CBOFS_20261002T00Z_US4MD1DD", samples[0], samples[^1]) { ProductSpec = "S-111", Samples = samples }],
        });
        service.SetCurrentTime(Run.AddHours(20));
        using var host = ViewHost.Show(new TimeHudView { DataContext = timeline, MapWidth = 1200 }, width: 640, height: 120);

        return Verify(host.CaptureFrame(), "png");
    }

    /// <summary>UTC labels, so the frame does not depend on the machine's time zone.</summary>
    private sealed class UtcTimeFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }
}
