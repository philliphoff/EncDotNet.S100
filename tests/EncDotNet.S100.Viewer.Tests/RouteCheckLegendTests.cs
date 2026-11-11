using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.ViewModels;
using EncDotNet.S100.Viewer.ViewModels.Legends;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The route check on the Legend service (#1408, legends design note D5, D6,
/// D12): its section goes on top while Check route is on, the map card shows
/// only while the Legend tab isn't shown, and MCP <c>set_legend</c> replaces
/// <c>set_map_legend</c>. The scenario is the generated "Warnings on route"
/// (no draft until a test sets one).
/// </summary>
public sealed class RouteCheckLegendTests(WarningsScenarioFixture fixture) : IClassFixture<WarningsScenarioFixture>, IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _culture;

    private sealed class InlineInvoker : IRouteEditInvoker
    {
        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }

    private sealed class FixedFormat : ITimeFormatProvider
    {
        public TimeFormat Current => TimeFormat.Utc;

        public event Action<TimeFormat>? TimeFormatChanged { add { } remove { } }
    }

    /// <summary>
    /// The right dock as far as the Legend tab goes: showing or hiding the tab
    /// sets <see cref="LegendService.IsTabShown"/>, as the dock does.
    /// </summary>
    private sealed class LegendDock(LegendService service, bool available = true) : IViewerUiController
    {
        public List<(string Panel, bool Visible)> Calls { get; } = [];

        public Task<IReadOnlyList<ViewerPanelState>> GetPanelsAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ViewerPanelState>>([State()]);

        public Task<PanelMutationOutcome> SetPanelVisibilityAsync(string panelId, bool visible, CancellationToken ct = default)
        {
            Calls.Add((panelId, visible));
            var previous = service.IsTabShown;
            if (available)
                service.IsTabShown = visible;
            return Task.FromResult(new PanelMutationOutcome(true, available, State(), previous));
        }

        private ViewerPanelState State() => new(ShellLayoutViewModel.LegendTabId, "Legend", "right", available,
            service.IsTabShown, service.IsTabShown, service.IsTabShown);
    }

    private static T Value<T>(ToolResult<T> result) where T : class =>
        result.TryGetValue(out var value) ? value! : throw new Xunit.Sdk.XunitException(result.TryGetError(out var e) ? e.Message : "error");

    private static readonly SwatchLegendEntry[] CurrentLegend =
    [
        new("#D0E8FF", "0–1 kn"),
        new("#8FC1F0", "1–2 kn"),
    ];

    /// <summary>A legend service with the route check's source and an S-111 tile in view.</summary>
    internal static (LegendService Service, RouteCheckLegendSource Source) Legend(RoutesService routes, PassageCheckService check)
    {
        var f = new LegendServiceTests.Fixture();
        f.Add("111US00_a.h5", "S-111", 40, -75, 41, -74);
        f.Loader.SetEntries(LegendServiceTests.VectorEntry(
            "111US00_a.h5", "S-111", "s111.arrows", "Surface currents: 111US00_a.h5", S98DisplayPlane.DynamicArrows, CurrentLegend));
        f.Viewport.Publish(39, -76, 42, -73);
        var service = f.Create();
        var source = new RouteCheckLegendSource(routes, check, () => service.Palette);
        service.AddSource(source);
        service.Refresh();
        return (service, source);
    }

    private static LegendViewModel Tab(LegendService service) =>
        new(service, new ClockViewTime(new FakeTimeProvider(new DateTimeOffset(2026, 11, 10, 14, 20, 0, TimeSpan.Zero))), new FixedFormat());

    private async Task<WarningsRouteCheck> CheckedAsync()
    {
        var s = WarningsRouteCheck.Create(fixture);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        return s;
    }

    [Fact]
    public async Task The_route_check_section_is_on_top_while_check_route_is_on()
    {
        using var s = await CheckedAsync();
        var (service, source) = Legend(s.Routes, s.Check);
        using var _ = service;
        using var __ = source;

        // On top of the product sections; app state, so always in view.
        Assert.Equal([RouteCheckLegendSource.SectionId, "S-111"], service.Sections.Select(x => x.Id));
        var section = service.Sections[0];
        Assert.Equal(("Route", "Route check", s.Route.Name), (section.Spec, section.Title, section.Source));
        Assert.True(section.IsInView);
        // No draft: findings only, with the note as the caption.
        var findings = Assert.Single(section.Groups);
        Assert.Equal(RouteCheckLegendSource.FindingsGroupKey, findings.Key);
        Assert.Equal(["Must review", "Check", "Info (on select)", "Stretch of the route", "Cross-track corridor"], findings.Rows.Select(r => r.Label));
        Assert.Equal([LegendRowKind.Symbol, LegendRowKind.Symbol, LegendRowKind.Symbol, LegendRowKind.Swatch, LegendRowKind.Line],
            findings.Rows.Select(LegendRows.KindOf));
        Assert.Equal("No draft: the line isn't coloured by clearance.", section.Caption);

        // With a draft the clearance rows come first and the note goes.
        s.Route.SetPassageValue(RoutePassageField.Draft, 5);
        await s.Check.WhenIdle();
        section = service.Sections[0];
        Assert.Equal([RouteCheckLegendSource.UkcGroupKey, RouteCheckLegendSource.FindingsGroupKey], section.Groups.Select(g => g.Key));
        Assert.Equal(["Meets your settings", "Tight", "Below your UKC setting", "Dashed: lower confidence", "No data"],
            section.Groups[0].Rows.Select(r => r.Label));
        Assert.Null(section.Caption);

        // Check route off: the section goes.
        s.Routes.IsCheckModeActive = false;
        Assert.Equal(["S-111"], service.Sections.Select(x => x.Id));
    }

    [Fact]
    public async Task The_section_is_recoloured_for_the_palette_and_its_glyphs_render()
    {
        using var s = await CheckedAsync();
        var (service, source) = Legend(s.Routes, s.Check);
        using var _ = service;
        using var __ = source;

        string Sleeve() => ((SwatchLegendEntry)service.Sections[0].Groups[0].Rows.ElementAt(3)).Color;
        var day = Sleeve();
        service.SetPalette(PaletteType.Night);
        Assert.NotEqual(day, Sleeve());

        foreach (var row in service.Sections[0].Rows)
        {
            using var bitmap = Renderers.Skia.LegendGlyphRenderer.Render(row);
            Assert.Contains(bitmap.Pixels, p => p.Alpha > 0);
        }
    }

    [Fact]
    public async Task The_map_card_shows_only_while_check_route_is_on_and_the_tab_is_not_shown()
    {
        using var s = WarningsRouteCheck.Create(fixture);
        var (service, source) = Legend(s.Routes, s.Check);
        using var _ = service;
        using var __ = source;
        using var card = new RouteCheckLegendViewModel(service, new RouteCheckLegendState(settings: null));

        Assert.False(card.IsVisible);
        s.Routes.IsCheckModeActive = true;
        await s.Findings.WhenIdle();
        Assert.True(card.IsVisible);
        Assert.Same(service.Sections[0], card.Section);
        Assert.False(card.HasClearance);

        // D6: the tab shows the section, so the card (and its reserve) goes.
        service.IsTabShown = true;
        Assert.False(card.IsVisible);
        Assert.NotNull(card.Section);
        service.IsTabShown = false;
        Assert.True(card.IsVisible);

        s.Route.SetPassageValue(RoutePassageField.Draft, 5);
        await s.Check.WhenIdle();
        Assert.True(card.HasClearance);

        s.Routes.IsCheckModeActive = false;
        Assert.False(card.IsVisible);
        Assert.Null(card.Section);
    }

    [Fact]
    public async Task Set_legend_opens_and_closes_the_tab_sets_the_scope_and_expands_a_section()
    {
        using var s = await CheckedAsync();
        var (service, source) = Legend(s.Routes, s.Check);
        using var _ = service;
        using var __ = source;
        using var tab = Tab(service);
        using var card = new RouteCheckLegendViewModel(service, new RouteCheckLegendState(settings: null));
        var dock = new LegendDock(service);
        var tool = new SetLegendTool(service, tab, new FakeUiControllerAccessor { Current = dock }, new InlineInvoker());
        var ct = TestContext.Current.CancellationToken;

        // Open: the tab shows and the card hides.
        var opened = Value(await tool.InvokeAsync(new SetLegendRequest(Open: true), ct));
        Assert.True(opened.Legend.TabShown);
        Assert.Equal("in_view", opened.Scope);
        Assert.Equal([RouteCheckLegendSource.SectionId, "S-111"], opened.Legend.Sections.Select(x => x.Id));
        Assert.False(card.IsVisible);

        // Collapse a section, and All loaded.
        var collapsed = Value(await tool.InvokeAsync(new SetLegendRequest(InView: false, Section: "S-111", Expanded: false), ct));
        Assert.Equal("all_loaded", collapsed.Scope);
        Assert.True(tab.IsAllLoaded);
        Assert.False(collapsed.Legend.Sections.Single(x => x.Id == "S-111").Expanded);
        Assert.False(tab.Sections.Single(x => x.Id == "S-111").IsExpanded);

        // Expand it again and go back to In view; omitted arguments are left.
        var expanded = Value(await tool.InvokeAsync(new SetLegendRequest(InView: true, Section: "S-111", Expanded: true), ct));
        Assert.Equal("in_view", expanded.Scope);
        Assert.True(expanded.Legend.Sections.Single(x => x.Id == "S-111").Expanded);
        Assert.True(expanded.Legend.TabShown);

        // Close: the card is back.
        var closed = Value(await tool.InvokeAsync(new SetLegendRequest(Open: false), ct));
        Assert.False(closed.Legend.TabShown);
        Assert.True(card.IsVisible);
        Assert.Equal([(ShellLayoutViewModel.LegendTabId, true), (ShellLayoutViewModel.LegendTabId, false)], dock.Calls);
    }

    [Fact]
    public async Task Set_legend_opens_at_a_section_and_rejects_what_it_cannot_do()
    {
        using var s = await CheckedAsync();
        var (service, source) = Legend(s.Routes, s.Check);
        using var _ = service;
        using var __ = source;
        using var tab = Tab(service);
        var ct = TestContext.Current.CancellationToken;
        var accessor = new FakeUiControllerAccessor();
        var tool = new SetLegendTool(service, tab, accessor, new InlineInvoker());

        // Opening at a collapsed section expands it.
        service.SetExpanded(RouteCheckLegendSource.SectionId, false);
        LegendRevealEventArgs? revealed = null;
        tab.RevealRequested += (_, e) => revealed = e;
        var err = await tool.InvokeAsync(new SetLegendRequest(Open: true, Section: RouteCheckLegendSource.SectionId), ct);
        Assert.True(err.TryGetError(out var notReady));
        Assert.Equal("ui_not_ready", notReady!.Code);

        accessor.Current = new LegendDock(service);
        var shown = Value(await tool.InvokeAsync(new SetLegendRequest(Open: true, Section: RouteCheckLegendSource.SectionId), ct));
        Assert.True(shown.Legend.Sections.Single(x => x.Id == RouteCheckLegendSource.SectionId).Expanded);
        Assert.Equal(RouteCheckLegendSource.SectionId, revealed?.Section.Id);

        var unknown = await tool.InvokeAsync(new SetLegendRequest(Section: "S-999", Expanded: true), ct);
        Assert.True(unknown.TryGetError(out var e1));
        Assert.Equal("invalid_argument", e1!.Code);
        var noSection = await tool.InvokeAsync(new SetLegendRequest(Expanded: false), ct);
        Assert.True(noSection.TryGetError(out var e2));
        Assert.Equal("invalid_argument", e2!.Code);

        // A tab the host doesn't have can't be shown.
        accessor.Current = new LegendDock(service, available: false);
        var unavailable = await tool.InvokeAsync(new SetLegendRequest(Open: true), ct);
        Assert.True(unavailable.TryGetError(out var e3));
        Assert.Equal("panel_unavailable", e3!.Code);
    }
}
