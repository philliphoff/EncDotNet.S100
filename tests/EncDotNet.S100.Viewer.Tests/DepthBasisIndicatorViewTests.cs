using Avalonia.Automation;
using Avalonia.Controls;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Viewer.Services.Depth;
using EncDotNet.S100.Viewer.Tests.Headless;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.Views;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The status bar's depth basis pill (#1191), laid out headlessly and found by
/// its <c>Map.DepthBasis</c> automation id.
/// </summary>
public sealed class DepthBasisIndicatorViewTests
{
    private static readonly DateTimeOffset ViewTime = new(2026, 10, 31, 14, 20, 0, TimeSpan.Zero);

    internal static DepthBasis Charted { get; } = new(
        DepthBasisKind.Charted, DepthBasisReason.None, null, ViewTime, null, true, null,
        [new DepthBasisSource("US5WA22M", "S-101", 12)], []);

    internal static DepthBasis NotApplied { get; } = new(
        DepthBasisKind.Charted, DepthBasisReason.NotApplied, null, ViewTime, null, true, DatumAgreement.Same,
        [new DepthBasisSource("US5WA22M", "S-101", 12)],
        [new DepthBasisSource("104US00_CBOFS", "S-104", 12,
            new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc),
            WaterLevelSourceKind.Grid)]);

    internal static DepthBasisIndicatorViewModel Pill(DepthBasis basis)
    {
        var vm = new DepthBasisIndicatorViewModel();
        vm.Update(basis, waterLevelLoaded: basis.WaterLevelSources.Count > 0, TimeFormat.Utc);
        return vm;
    }

    [AvaloniaFact]
    public void The_pill_is_hidden_without_depths()
    {
        using var host = ViewHost.Show(new DepthBasisIndicatorView { DataContext = Pill(DepthBasis.Hidden) }, width: 400, height: 40);

        Assert.False(host.IsShown("Map.DepthBasis"));
    }

    [AvaloniaFact]
    public void The_accessible_name_includes_the_state()
    {
        var vm = Pill(Charted);
        using var host = ViewHost.Show(new DepthBasisIndicatorView { DataContext = vm }, width: 400, height: 40);

        var button = host.Find<Button>("Map.DepthBasis");
        Assert.True(host.IsShown("Map.DepthBasis"));
        Assert.Equal("Depth basis: Charted", AutomationProperties.GetName(button));
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(button));

        vm.Update(NotApplied, waterLevelLoaded: true, TimeFormat.Utc);
        host.Settle();

        Assert.Equal("Depth basis: Charted, water level not applied", AutomationProperties.GetName(button));
        Assert.Equal("Charted · water level not applied", ToolTip.GetTip(button));
        Assert.Contains("info", button.Classes);
    }

    [AvaloniaFact]
    public void A_narrow_status_bar_steps_down_through_whole_forms()
    {
        var vm = Pill(NotApplied);
        var view = new DepthBasisIndicatorView { DataContext = vm };
        using var host = ViewHost.Show(view, width: 600, height: 40);
        var value = host.All<TextBlock>().Single(t => t.Name == "ValueText");

        view.Fit(1_000);
        host.Settle();
        Assert.Equal(DepthBasisForm.Full, vm.Form);
        Assert.Equal("Charted · water level not applied", value.Text);

        var full = view.DesiredSize.Width;
        view.Fit(full - 1);
        host.Settle();
        Assert.Equal(DepthBasisForm.Short, vm.Form);
        Assert.Equal("Charted · not applied", value.Text);

        view.Fit(10);
        host.Settle();
        Assert.Equal(DepthBasisForm.Compact, vm.Form);
        Assert.Equal("Charted", value.Text);
        Assert.Equal(Avalonia.Media.TextTrimming.None, value.TextTrimming);

        // The full form stays the tooltip and accessible name whatever is drawn.
        var button = host.Find<Button>("Map.DepthBasis");
        Assert.Equal("Charted · water level not applied", ToolTip.GetTip(button));
        Assert.Equal("Depth basis: Charted, water level not applied", AutomationProperties.GetName(button));
    }

    [Theory]
    [InlineData(500, "Full")]
    [InlineData(300, "Short")]
    [InlineData(200, "Compact")]
    [InlineData(10, "Compact")]
    public void Choose_takes_the_longest_form_that_fits(double room, string expected) =>
        Assert.Equal(Enum.Parse<DepthBasisForm>(expected), DepthBasisIndicatorView.Choose(room, chrome: 100, formWidths: [300, 150, 50]));
}
