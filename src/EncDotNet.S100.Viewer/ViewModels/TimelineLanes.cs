using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Renderers.Mapsui;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One lane of the Timeline (#710, handoff E1–E2): a loaded time-aware
/// layer's label, layer time and data bands on the shared axis.
/// </summary>
internal sealed class TimelineLaneViewModel : ViewModelBase
{
    private static readonly IBrush ExpiredBrush = new ImmutableSolidColorBrush(Color.Parse("#A1A1AA"));

    private readonly IBrush _color;
    private MapsuiMapTimedDataset _dataset;
    private string _code = string.Empty;
    private string _sub = string.Empty;
    private string _layerTime = string.Empty;
    private string _tooltip = string.Empty;
    private bool _isHidden;
    private bool _isExpired;
    private bool? _isInMapView;
    private DateTime? _nearest;
    private IReadOnlyList<NormalizedCoverageBand> _bands = [];
    private IReadOnlyList<NormalizedGap> _gaps = [];

    public TimelineLaneViewModel(string key, MapsuiMapTimedDataset dataset, Color color, Action<DateTime> jump)
    {
        Key = key;
        _dataset = dataset;
        Color = color;
        _color = new ImmutableSolidColorBrush(color);
        JumpCommand = new RelayCommand(() =>
        {
            if (_nearest is { } nearest)
                jump(nearest);
        }, () => _nearest is not null);
    }

    /// <summary>The lane's identity: the dataset id, else its name.</summary>
    public string Key { get; }

    /// <summary>The layer the lane shows.</summary>
    public MapsuiMapTimedDataset Dataset
    {
        get => _dataset;
        internal set => _dataset = value;
    }

    /// <summary>The session dataset id, or null.</summary>
    public string? DatasetId => _dataset.DatasetId;

    /// <summary>The product colour.</summary>
    public Color Color { get; }

    /// <summary>The swatch and band colour: the product's, grey once expired.</summary>
    public IBrush Swatch => IsExpired ? ExpiredBrush : _color;

    /// <summary>The lane's code, e.g. "cbofs" (mono, semibold).</summary>
    public string Code
    {
        get => _code;
        internal set => SetProperty(ref _code, value);
    }

    /// <summary>The muted sub-label, e.g. "12:00Z run · 1 h".</summary>
    public string Sub
    {
        get => _sub;
        internal set => SetProperty(ref _sub, value);
    }

    /// <summary>The layer time, as the layer list row shows it (handoff D3).</summary>
    public string LayerTime
    {
        get => _layerTime;
        internal set => SetProperty(ref _layerTime, value);
    }

    /// <summary>The full label, since the column truncates.</summary>
    public string Tooltip
    {
        get => _tooltip;
        internal set => SetProperty(ref _tooltip, value);
    }

    /// <summary>True when the layer has no data at the view time (amber).</summary>
    public bool IsHidden
    {
        get => _isHidden;
        internal set => SetProperty(ref _isHidden, value);
    }

    /// <summary>True when the layer is a forecast that has ended (grey, Expired tag).</summary>
    public bool IsExpired
    {
        get => _isExpired;
        internal set
        {
            if (SetProperty(ref _isExpired, value))
                OnPropertyChanged(nameof(Swatch));
        }
    }

    /// <summary>Whether the layer's footprint intersects the map view; null when unknown.</summary>
    public bool? IsInMapView
    {
        get => _isInMapView;
        internal set => SetProperty(ref _isInMapView, value);
    }

    /// <summary>The lane's data on the axis.</summary>
    public IReadOnlyList<NormalizedCoverageBand> Bands
    {
        get => _bands;
        internal set => SetProperty(ref _bands, value);
    }

    /// <summary>The axis's collapsed gaps, hatched through every lane (handoff E3).</summary>
    public IReadOnlyList<NormalizedGap> Gaps
    {
        get => _gaps;
        internal set => SetProperty(ref _gaps, value);
    }

    /// <summary>The nearest data when the layer has none at the view time.</summary>
    internal DateTime? Nearest
    {
        get => _nearest;
        set
        {
            if (_nearest == value)
                return;
            _nearest = value;
            ((RelayCommand)JumpCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Moves the view time to the layer's nearest data (clicking its layer time).</summary>
    public ICommand JumpCommand { get; }
}

/// <summary>A product group of lanes: <c>▾ S-111 Surface currents  2</c> (#710, handoff E1).</summary>
internal sealed class TimelineLaneGroupViewModel : ViewModelBase
{
    private bool _isExpanded;

    public TimelineLaneGroupViewModel(string key, string title, IReadOnlyList<TimelineLaneViewModel> lanes, bool isExpanded, Action<string, bool> expandedChanged)
    {
        Key = key;
        Title = title;
        Lanes = lanes;
        _isExpanded = isExpanded;
        ToggleCommand = new RelayCommand(() =>
        {
            IsExpanded = !IsExpanded;
            expandedChanged(Key, IsExpanded);
        });
    }

    /// <summary>The product specification the group holds.</summary>
    public string Key { get; }

    /// <summary>"S-111 Surface currents".</summary>
    public string Title { get; }

    /// <summary>The number of lanes, shown after the title.</summary>
    public int Count => Lanes.Count;

    /// <summary>The group's lanes.</summary>
    public IReadOnlyList<TimelineLaneViewModel> Lanes { get; }

    /// <summary>True when the lanes are listed.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        private set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>Folds or unfolds the group.</summary>
    public ICommand ToggleCommand { get; }
}
