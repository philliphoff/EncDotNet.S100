using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.ViewModels.Activities;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// The right dock's <b>Reference</b> tab (symbol dictionary design note
/// §4.4, R7, #1650; legends D13, D22): one entry at a time, with back,
/// forward and a breadcrumb from <see cref="ReferenceNavigator"/>. Before
/// anything is opened it shows its empty state.
/// </summary>
/// <remarks>
/// <see cref="ReferenceNavigator.Open(ReferenceKey)"/> raises
/// <see cref="ContentBecameAvailable"/>, so the shell switches the right dock
/// to the tab; Object information keeps its pick, one tab away.
/// </remarks>
internal sealed class ReferenceViewModel : ViewModelBase, IActivityTabContentSignal, IDisposable
{
    private readonly ReferenceNavigator _navigator;
    private readonly ChartReferenceService _service;
    private readonly ChartReferenceGlyphCache _glyphs;
    private readonly ViewerSettingsService? _settings;
    private readonly Action<Action> _post;
    private readonly Dictionary<ReferenceKey, string> _names = [];
    private readonly RelayCommand _backCommand;
    private readonly RelayCommand _forwardCommand;
    private IChartReferenceMap? _map;
    private ReferenceEntryViewModel? _entry;
    private IReadOnlyList<ReferenceCrumbViewModel> _crumbs = [];
    private bool _isLoading;
    private bool _notFound;
    private int _loadVersion;
    private bool _disposed;

    /// <summary>Creates the tab's view model.</summary>
    /// <param name="navigator">The trail.</param>
    /// <param name="service">The chart reference indexes.</param>
    /// <param name="glyphs">The glyph cache, which follows the chart's palette.</param>
    /// <param name="settings">
    /// The viewer settings: Simplified symbols chooses a feature type's
    /// glyph, and a display setting shows its value. <see langword="null"/> for none.
    /// </param>
    /// <param name="post">Runs an action on the UI thread; <see langword="null"/> runs it at once.</param>
    public ReferenceViewModel(
        ReferenceNavigator navigator,
        ChartReferenceService service,
        ChartReferenceGlyphCache glyphs,
        ViewerSettingsService? settings = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(glyphs);
        _navigator = navigator;
        _service = service;
        _glyphs = glyphs;
        _settings = settings;
        _post = post ?? (a => a());

        _backCommand = new RelayCommand(() => _navigator.Back(), () => _navigator.CanGoBack);
        _forwardCommand = new RelayCommand(() => _navigator.Forward(), () => _navigator.CanGoForward);
        _navigator.Changed += OnNavigatorChanged;
        _navigator.ShowRequested += OnShowRequested;
        _glyphs.PaletteChanged += OnPaletteChanged;
        _service.ProbesReady += OnProbesReady;
        if (_settings is not null)
            _settings.PropertyChanged += OnSettingsChanged;
        if (_navigator.Current is not null)
            Loading = LoadAsync();
    }

    /// <summary>Raised when an entry is opened: the shell shows the right dock on this tab.</summary>
    public event EventHandler? ContentBecameAvailable;

    /// <summary>The trail.</summary>
    public ReferenceNavigator Navigator => _navigator;

    /// <summary>The entry shown, or <see langword="null"/> before one is opened, while it loads, or when it isn't found.</summary>
    public ReferenceEntryViewModel? Entry
    {
        get => _entry;
        private set
        {
            var previous = _entry;
            if (!SetProperty(ref _entry, value))
                return;
            previous?.Close();
            value?.Start();
            OnPropertyChanged(nameof(HasEntry));
        }
    }

    /// <summary>Whether an entry is shown.</summary>
    public bool HasEntry => _entry is not null;

    /// <summary>Whether nothing has been opened yet, so the tab shows its empty state (legends D22).</summary>
    public bool IsEmpty => _navigator.Current is null;

    /// <summary>Whether the entry's product index is being built.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set => SetProperty(ref _isLoading, value);
    }

    /// <summary>Whether the catalogues have no entry for the key opened.</summary>
    public bool ShowNotFound
    {
        get => _notFound;
        private set => SetProperty(ref _notFound, value);
    }

    /// <summary>"The catalogues have no entry S-101/Symbol/XYZ."</summary>
    public string NotFoundDescription => _navigator.Current is { } key
        ? string.Format(CultureInfo.CurrentCulture, Strings.Reference_NotFoundDescription, key)
        : string.Empty;

    /// <summary>The breadcrumb: the entries that led here, the current one last.</summary>
    public IReadOnlyList<ReferenceCrumbViewModel> Crumbs
    {
        get => _crumbs;
        private set => SetProperty(ref _crumbs, value);
    }

    /// <summary>Whether the breadcrumb shows: there is a trail of more than one entry.</summary>
    public bool HasTrail => _crumbs.Count > 1;

    /// <summary>Goes back one entry (Alt+Left).</summary>
    public ICommand BackCommand => _backCommand;

    /// <summary>Goes forward one entry (Alt+Right).</summary>
    public ICommand ForwardCommand => _forwardCommand;

    /// <summary>
    /// Opens the Chart reference activity, from the empty state; the app
    /// sets it. <see langword="null"/> hides the button.
    /// </summary>
    public ICommand? ShowChartReferenceCommand { get; set; }

    /// <summary>Whether the empty state offers to open Chart reference.</summary>
    public bool CanShowChartReference => ShowChartReferenceCommand is not null;

    /// <summary>
    /// Opens the settings that change a display setting, by its context
    /// parameter id; the app sets it. <see langword="null"/> hides Change.
    /// </summary>
    public Action<string>? ChangeSetting { get; set; }

    /// <summary>
    /// The terms the app shares (R8, #1651), with eXaminer links; each entry
    /// reads them over its own index. <see langword="null"/> for terms
    /// without eXaminer links.
    /// </summary>
    public ReferenceTerms? Terms { get; set; }

    /// <summary>The latest load of an entry; tests wait on it.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Connects the map, for a feature type's In view and Find. The app
    /// calls it once; hosts without a map don't.
    /// </summary>
    /// <param name="map">The map.</param>
    internal void ConnectMap(IChartReferenceMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (_map is not null)
            _map.Changed -= OnMapChanged;
        _map = map;
        _map.Changed += OnMapChanged;
        if (_navigator.Current is not null)
            Loading = LoadAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _navigator.Changed -= OnNavigatorChanged;
        _navigator.ShowRequested -= OnShowRequested;
        _glyphs.PaletteChanged -= OnPaletteChanged;
        _service.ProbesReady -= OnProbesReady;
        if (_settings is not null)
            _settings.PropertyChanged -= OnSettingsChanged;
        if (_map is not null)
            _map.Changed -= OnMapChanged;
        _entry?.Close();
    }

    private void OnNavigatorChanged(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        _backCommand.NotifyCanExecuteChanged();
        _forwardCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NotFoundDescription));
        Loading = LoadAsync();
    }

    private void OnShowRequested(object? sender, EventArgs e)
    {
        if (!_disposed)
            ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
    }

    private async Task LoadAsync()
    {
        var version = ++_loadVersion;
        if (_navigator.Current is not { } key)
        {
            Entry = null;
            ShowNotFound = false;
            UpdateCrumbs();
            return;
        }

        IsLoading = true;
        ShowNotFound = false;
        UpdateCrumbs();

        ChartReferenceIndex? index;
        try
        {
            index = await _service.GetIndexAsync(key.Spec);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
        {
            // A catalogue whose rules can't be read: the entry isn't found.
            index = null;
        }

        _post(() =>
        {
            if (version != _loadVersion || _disposed)
                return;
            IsLoading = false;
            if (index is null || !index.TryGet(key, out var entry))
            {
                Entry = null;
                ShowNotFound = true;
                return;
            }

            _names[key] = ChartReferenceRowViewModel.NameOf(entry);
            Entry = ReferenceEntryViewModel.Create(entry, Context(index));
            UpdateCrumbs();
        });
    }

    private ReferenceEntryContext Context(ChartReferenceIndex index)
    {
        var feature = _navigator.CurrentFeature;
        var from = _navigator.Current;

        // A link inside an entry opened from a pick keeps the pick for the
        // entries that show its values: its attributes, and its own feature type.
        void Open(ReferenceKey key) => _navigator.Open(
            key,
            feature is not null && (key.Kind == ReferenceKind.Attribute || (from is { } f && f.Kind == key.Kind && string.Equals(f.Code, key.Code, StringComparison.OrdinalIgnoreCase)))
                ? feature
                : null);

        return new()
        {
            Index = index,
            Artwork = _service.GetArtwork(index.Spec),
            Terms = Terms?.With(index, Open, _service.GetArtwork(index.Spec)),
            Glyphs = _glyphs,
            SimplifiedSymbols = _settings?.SimplifiedSymbols ?? false,
            Open = Open,
            Post = _post,
            Map = _map,
            Settings = _settings,
            ChangeSetting = ChangeSetting,
            Feature = feature,
            ProbesPending = index.Probes is null && _service.IsProbing(index.Spec),
        };
    }

    private void UpdateCrumbs()
    {
        var crumbs = _navigator.Breadcrumb;
        var last = crumbs.Count - 1;
        Crumbs =
        [
            .. crumbs.Select((c, i) => new ReferenceCrumbViewModel(
                _names.GetValueOrDefault(c.Key, c.Key.Code),
                isCurrent: i == last,
                () => _navigator.GoTo(c.Position))),
        ];
        OnPropertyChanged(nameof(HasTrail));
    }

    // A background probe finished: a feature type entry of the product shows its variant table (R13b, #1721).
    private void OnProbesReady(object? sender, CataloguesChangedEventArgs e) => _post(() =>
    {
        if (!_disposed && _entry is FeatureTypeEntryViewModel entry && entry.Key.Spec == e.Spec)
            Loading = LoadAsync();
    });

    private void OnPaletteChanged(object? sender, EventArgs e) => _post(() => _entry?.ReloadGlyphs());

    private void OnMapChanged(object? sender, EventArgs e) => _post(() => _entry?.RefreshInView());

    // A feature type leads with the other style's symbol when Simplified symbols changes.
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerSettingsService.SimplifiedSymbols) && _entry is FeatureTypeEntryViewModel)
            _post(() => Loading = LoadAsync());
    }
}

/// <summary>One entry of the Reference tab's breadcrumb.</summary>
internal sealed class ReferenceCrumbViewModel
{
    /// <summary>Creates a crumb.</summary>
    /// <param name="name">The entry's name.</param>
    /// <param name="isCurrent">Whether it is the entry shown.</param>
    /// <param name="open">Goes back to it.</param>
    public ReferenceCrumbViewModel(string name, bool isCurrent, Action open)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(open);
        Name = name;
        IsCurrent = isCurrent;
        OpenCommand = new RelayCommand(open);
    }

    /// <summary>The entry's name.</summary>
    public string Name { get; }

    /// <summary>Whether it is the entry shown, which isn't a link.</summary>
    public bool IsCurrent { get; }

    /// <summary>Whether it is a link to an earlier entry.</summary>
    public bool IsLink => !IsCurrent;

    /// <summary>Goes back to the entry.</summary>
    public ICommand OpenCommand { get; }
}
