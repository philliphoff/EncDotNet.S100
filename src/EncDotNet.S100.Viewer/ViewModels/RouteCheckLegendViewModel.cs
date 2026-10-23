using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tools;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Check route map legend (#959, handoff C8): one box at the map's
/// bottom left with "UKC AT ETA" (with a draft) and "FINDINGS" (the three
/// marker shapes, a sleeve and the corridor edge). Its header folds it to a
/// 28 px "Legend" chip; the fold is kept per user.
/// </summary>
/// <remarks>
/// The swatches use the route's state tones for the chart palette
/// (<see cref="RouteCheckColors"/>), as the map does, so the tones pass
/// (#934) changes them in one place.
/// </remarks>
internal sealed class RouteCheckLegendViewModel : ViewModelBase, IDisposable
{
    private readonly RoutesService _routes;
    private readonly PassageCheckService? _check;
    private readonly IMeasureOverlayAppearanceProvider? _appearance;
    private readonly RouteCheckLegendState _state;
    private RouteCheckColors _colors;
    private PaletteType _palette = (PaletteType)(-1);

    public RouteCheckLegendViewModel(
        RoutesService routes,
        PassageCheckService? check,
        IMeasureOverlayAppearanceProvider? appearance,
        RouteCheckLegendState state)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(state);
        _routes = routes;
        _check = check;
        _appearance = appearance;
        _state = state;
        ToggleCommand = new RelayCommand(() => _state.IsOpen = !_state.IsOpen);

        _routes.CheckModeChanged += OnChanged;
        _state.Changed += OnChanged;
        if (_check is not null)
            _check.Changed += OnChanged;
        if (_appearance is not null)
            _appearance.Changed += OnChanged;
        Refresh();
    }

    /// <summary>Whether the legend shows: while Check route is on.</summary>
    public bool IsVisible => _routes.IsCheckModeActive && _check is not null;

    /// <summary>Whether the legend is open; otherwise it's the "Legend" chip.</summary>
    public bool IsOpen => _state.IsOpen;

    /// <summary>
    /// Whether the "UKC AT ETA" section shows: the report's clearance check
    /// ran, so the line is coloured by clearance. Until the checked route's
    /// first report lands, whether it has a draft.
    /// </summary>
    public bool HasClearance =>
        _check is { IsActive: true } check
        && (check.Report is { } report && check.ReportRouteId == check.Input?.RouteId
            ? report.Status(ClearanceCheck.CheckName) is { State: not RouteCheckState.Skipped }
            : check.HasDraft);

    /// <summary>Whether the "No draft" note shows.</summary>
    public bool IsNoDraft => !HasClearance;

    /// <summary>The header: the first section's title while open, "Legend" folded.</summary>
    public string HeaderText => !IsOpen ? Strings.Map_LegendChip : HasClearance ? Strings.Check_LegendTitle : Strings.Map_LegendFindings;

    /// <summary>Whether the "FINDINGS" title shows under the UKC section (with a draft; otherwise it's the header).</summary>
    public bool ShowsFindingsTitle => HasClearance;

    /// <summary>The header's tooltip.</summary>
    public string ToggleTip => IsOpen ? Strings.Tooltip_MapLegendFold : Strings.Tooltip_MapLegendOpen;

    /// <summary>Folds or opens the legend.</summary>
    public IRelayCommand ToggleCommand { get; }

    public IBrush MeetsBrush { get; private set; } = Brushes.Transparent;

    public IBrush TightBrush { get; private set; } = Brushes.Transparent;

    public IBrush BelowBrush { get; private set; } = Brushes.Transparent;

    public IBrush NoDataBrush { get; private set; } = Brushes.Transparent;

    /// <summary>Must review markers and sleeves.</summary>
    public IBrush MustReviewBrush { get; private set; } = Brushes.Transparent;

    /// <summary>Check markers.</summary>
    public IBrush CheckBrush { get; private set; } = Brushes.Transparent;

    /// <summary>Info markers.</summary>
    public IBrush InfoBrush { get; private set; } = Brushes.Transparent;

    /// <summary>The sleeve swatch: Must review at the sleeve's opacity.</summary>
    public IBrush SleeveBrush { get; private set; } = Brushes.Transparent;

    /// <summary>The corridor edge.</summary>
    public IBrush CorridorBrush { get; private set; } = Brushes.Transparent;

    /// <summary>The markers' ring and glyph: the casing colour.</summary>
    public IBrush CasingBrush { get; private set; } = Brushes.Transparent;

    public void Dispose()
    {
        _routes.CheckModeChanged -= OnChanged;
        _state.Changed -= OnChanged;
        if (_check is not null)
            _check.Changed -= OnChanged;
        if (_appearance is not null)
            _appearance.Changed -= OnChanged;
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var palette = _appearance?.Current.Palette ?? PaletteType.Day;
        if (palette != _palette)
        {
            _palette = palette;
            _colors = RouteCheckColors.For(palette);
            MeetsBrush = Brush(_colors.Meets);
            TightBrush = Brush(_colors.Tight);
            BelowBrush = Brush(_colors.Below);
            NoDataBrush = Brush(_colors.NoData);
            MustReviewBrush = Brush(RouteCheckOverlay.ToneOf(RouteFindingGroup.MustReview, _colors));
            CheckBrush = Brush(RouteCheckOverlay.ToneOf(RouteFindingGroup.Check, _colors));
            InfoBrush = Brush(RouteCheckOverlay.ToneOf(RouteFindingGroup.Info, _colors));
            var (_, sleeve) = RouteOverlayRenderer.SleeveStyle(false);
            SleeveBrush = Brush(_colors.Below.WithAlpha((byte)Math.Round(255 * sleeve)));
            CorridorBrush = Brush(_colors.Ink);
            CasingBrush = Brush(_colors.Casing);
        }

        // Every property can change at once (mode, draft, fold, palette).
        OnPropertyChanged(string.Empty);
    }

    private static SolidColorBrush Brush(SKColor c) => new(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
}
