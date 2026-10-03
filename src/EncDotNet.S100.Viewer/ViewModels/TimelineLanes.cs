using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One lane of the Timeline (#710, handoff E1–E2): a loaded time-aware
/// layer's label, layer time and data bands on the shared axis; or data the
/// Library knows about but has not loaded (#711), with its actions.
/// </summary>
internal sealed class TimelineLaneViewModel : ViewModelBase
{
    private static readonly IBrush ExpiredBrush = new ImmutableSolidColorBrush(Color.Parse("#A1A1AA"));

    private readonly IBrush _color;
    private readonly ILibraryTimeSource? _library;
    private MapsuiMapTimedDataset? _dataset;
    private IReadOnlyList<LibraryTimedEntry> _entries = [];
    private IReadOnlyList<NormalizedCoverageBand> _onlineBands = [];
    private IReadOnlyList<NormalizedCoverageBand> _onDiskBands = [];
    private bool _isNewRun;
    private double _progress = double.NaN;
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

    public TimelineLaneViewModel(string key, MapsuiMapTimedDataset? dataset, string spec, Color color, Action<DateTime> jump, ILibraryTimeSource? library = null)
    {
        Key = key;
        _dataset = dataset;
        Spec = spec;
        Color = color;
        _color = new ImmutableSolidColorBrush(color);
        _library = library;
        JumpCommand = new RelayCommand(() =>
        {
            if (_nearest is { } nearest)
                jump(nearest);
        }, () => _nearest is not null);
        GetCommand = new AsyncRelayCommand(() => _library?.GetAsync([.. _entries.Where(e => e.State == LibraryTimedState.Online)]) ?? Task.CompletedTask, () => CanGet);
        LoadCommand = new AsyncRelayCommand(() => _library?.LoadAsync([.. _entries.Where(e => e.State == LibraryTimedState.OnDisk)]) ?? Task.CompletedTask, () => CanLoad);
        RevealCommand = new RelayCommand(() =>
        {
            if (_entries.FirstOrDefault() is { } entry)
                _library?.Reveal(entry);
        }, () => HasActions);
    }

    /// <summary>The lane's identity: the dataset id, else its name.</summary>
    public string Key { get; }

    /// <summary>The loaded layer the lane shows, or null for data only the Library knows about.</summary>
    public MapsuiMapTimedDataset? Dataset
    {
        get => _dataset;
        internal set => _dataset = value;
    }

    /// <summary>The session dataset id, or null.</summary>
    public string? DatasetId => _dataset?.DatasetId;

    /// <summary>The product specification.</summary>
    public string Spec { get; }

    /// <summary>True for a lane of data the Library knows about but has not loaded (#711).</summary>
    public bool IsKnown => _dataset is null;

    /// <summary>The Library's data for the lane: its loaded copy, other runs, and what is online or on disk.</summary>
    public IReadOnlyList<LibraryTimedEntry> Entries
    {
        get => _entries;
        internal set
        {
            _entries = value;
            OnPropertyChanged(nameof(HasActions));
            OnPropertyChanged(nameof(CanGet));
            OnPropertyChanged(nameof(CanLoad));
            OnPropertyChanged(nameof(GetText));
            OnPropertyChanged(nameof(ActionTitle));
            OnPropertyChanged(nameof(ActionDetail));
            ((AsyncRelayCommand)GetCommand).NotifyCanExecuteChanged();
            ((AsyncRelayCommand)LoadCommand).NotifyCanExecuteChanged();
            ((RelayCommand)RevealCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>Online windows: dashed, the fill at 6 % (handoff E2).</summary>
    public IReadOnlyList<NormalizedCoverageBand> OnlineBands
    {
        get => _onlineBands;
        internal set => SetProperty(ref _onlineBands, value);
    }

    /// <summary>Windows on disk, not loaded: outlined.</summary>
    public IReadOnlyList<NormalizedCoverageBand> OnDiskBands
    {
        get => _onDiskBands;
        internal set => SetProperty(ref _onDiskBands, value);
    }

    /// <summary>True when a newer run than the loaded or downloaded one is online ("New run").</summary>
    public bool IsNewRun
    {
        get => _isNewRun;
        internal set
        {
            if (SetProperty(ref _isNewRun, value))
                OnPropertyChanged(nameof(IsExpiredTag));
        }
    }

    /// <summary>True when the Expired tag shows (New run takes its place, as in the Library).</summary>
    public bool IsExpiredTag => IsExpired && !IsNewRun;

    /// <summary>How far the lane's download has got (0–1), filling its online band; NaN when none runs.</summary>
    public double Progress
    {
        get => _progress;
        internal set
        {
            if (_progress.Equals(value))
                return;
            _progress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionDetail));
            OnPropertyChanged(nameof(CanGet));
            ((AsyncRelayCommand)GetCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>True when the band popover has something to offer (handoff E5).</summary>
    public bool HasActions => _library is not null && _entries.Count > 0;

    /// <summary>True when some of the lane's data is online to download.</summary>
    public bool CanGet => _library is not null && _entries.Any(e => e.State == LibraryTimedState.Online) && double.IsNaN(_progress);

    /// <summary>True when some of the lane's data is on disk to load.</summary>
    public bool CanLoad => _library is not null && _entries.Any(e => e.State == LibraryTimedState.OnDisk);

    /// <summary>"Get · 13 MB".</summary>
    public string GetText
    {
        get
        {
            var bytes = _entries.Where(e => e.State == LibraryTimedState.Online).Sum(e => e.SizeBytes ?? 0);
            return bytes > 0
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.Strings.TimelinePanel_BandGetFormat, LibraryItemViewModel.FormatBytes(bytes))
                : Resources.Strings.TimelinePanel_BandGet;
        }
    }

    /// <summary>The popover's title: "cbofs · 12:00Z run".</summary>
    public string ActionTitle
    {
        get => _actionTitle;
        internal set => SetProperty(ref _actionTitle, value);
    }

    private string _actionTitle = string.Empty;

    /// <summary>The popover's details: the window, the tiles and size, or the download's progress.</summary>
    public string ActionDetail
    {
        get => double.IsNaN(_progress)
            ? _actionDetail
            : string.Format(System.Globalization.CultureInfo.CurrentCulture, Resources.Strings.TimelinePanel_BandDownloadingFormat, _progress);
        internal set
        {
            _actionDetail = value;
            OnPropertyChanged();
        }
    }

    private string _actionDetail = string.Empty;

    /// <summary>Downloads the lane's online data and loads it ("Get").</summary>
    public ICommand GetCommand { get; }

    /// <summary>Loads the lane's data from disk ("Load").</summary>
    public ICommand LoadCommand { get; }

    /// <summary>Shows the lane's dataset in the Library ("Reveal in Library").</summary>
    public ICommand RevealCommand { get; }

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
            {
                OnPropertyChanged(nameof(Swatch));
                OnPropertyChanged(nameof(IsExpiredTag));
            }
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
