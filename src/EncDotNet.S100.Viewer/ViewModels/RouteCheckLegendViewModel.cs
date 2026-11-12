using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.Services.PassageCheck;
using EncDotNet.S100.Viewer.Tools;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Check route map card (#959, handoff C8; legends design note D6,
/// #1408): the route check's Legend section
/// (<see cref="RouteCheckLegendSource"/>), drawn compact at the map's bottom
/// left: "UKC AT ETA" (with a draft), "FINDINGS" (the three marker shapes, a
/// sleeve and the corridor edge) and "WEATHER" (the storm at ETA). It shows
/// while Check route is on and the Legend tab isn't shown; when the tab shows,
/// the card hides and the map's legend reserve drops to 0. Its header folds it
/// to a 28 px "Legend" chip; the fold is kept per user.
/// </summary>
/// <remarks>
/// The swatches use the route's state tones for the legend's palette
/// (<see cref="RouteCheckColors"/>), as the map does, so the tones pass
/// (#934) changes them in one place.
/// </remarks>
internal sealed class RouteCheckLegendViewModel : ViewModelBase, IDisposable
{
    private readonly LegendService _legend;
    private readonly RouteCheckLegendState _state;
    private LegendSection? _section;
    private RouteCheckColors _colors;
    private PaletteType _palette = (PaletteType)(-1);

    /// <summary>Creates the card over the legend service.</summary>
    /// <param name="legend">The legend service; the card draws its <see cref="RouteCheckLegendSource.SectionId"/> section.</param>
    /// <param name="state">The card's fold, kept per user.</param>
    public RouteCheckLegendViewModel(LegendService legend, RouteCheckLegendState state)
    {
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(state);
        _legend = legend;
        _state = state;
        ToggleCommand = new RelayCommand(() => _state.IsOpen = !_state.IsOpen);

        _legend.Changed += OnChanged;
        _state.Changed += OnChanged;
        Refresh();
    }

    /// <summary>The route check's section, while Check route is on; otherwise null.</summary>
    public LegendSection? Section => _section;

    /// <summary>
    /// Whether the card shows (D6): while Check route is on and the Legend tab
    /// isn't shown. The map reserves its height only then.
    /// </summary>
    public bool IsVisible => _section is not null && !_legend.IsTabShown;

    /// <summary>Whether the legend is open; otherwise it's the "Legend" chip.</summary>
    public bool IsOpen => _state.IsOpen;

    /// <summary>
    /// Whether the "UKC AT ETA" section shows: the section has its clearance
    /// group, so the line is coloured by clearance.
    /// </summary>
    public bool HasClearance => HasGroup(RouteCheckLegendSource.UkcGroupKey);

    /// <summary>
    /// Whether the "WEATHER" section shows (#1299): the checked route's report
    /// has a closest approach to a tropical cyclone, drawn as the storm-at-ETA
    /// ghost.
    /// </summary>
    public bool HasStormAtEta => HasGroup(RouteCheckLegendSource.WeatherGroupKey);

    /// <summary>The storm-at-ETA glyph's ink.</summary>
    public IBrush InkBrush { get; private set; } = Brushes.Transparent;

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
        _legend.Changed -= OnChanged;
        _state.Changed -= OnChanged;
    }

    private bool HasGroup(string key) =>
        _section?.Groups.Any(g => string.Equals(g.Key, key, StringComparison.Ordinal)) == true;

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        _section = _legend.Sections.FirstOrDefault(s => string.Equals(s.Id, RouteCheckLegendSource.SectionId, StringComparison.Ordinal));
        var palette = _legend.Palette;
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
            InkBrush = Brush(_colors.Ink);
        }

        // Every property can change at once (mode, draft, fold, palette, tab).
        OnPropertyChanged(string.Empty);
    }

    private static SolidColorBrush Brush(SKColor c) => new(Color.FromArgb(c.Alpha, c.Red, c.Green, c.Blue));
}
