using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// The Chart reference activity (#1637, symbol dictionary design note §4.5,
/// R4): every feature type or symbol of a product's catalogues, grouped by
/// Chart 1 section, glyph first, with a product combo, search and
/// Features / Symbols. Selecting a row opens its entry in the Reference tab
/// (R7, #1650).
/// </summary>
/// <remarks>
/// <para>
/// The index comes from <see cref="ChartReferenceService"/>, built off the UI
/// thread; the glyphs from <see cref="ChartReferenceGlyphCache"/>, drawn off
/// the UI thread as rows are shown. Results come back through the
/// <c>post</c> callback, which the app points at the UI thread.
/// </para>
/// <para>
/// The search waits 250 ms after the last keystroke, as Feature search does,
/// when running in the app; without an application (unit tests) it runs at
/// once.
/// </para>
/// </remarks>
internal sealed partial class ChartReferenceViewModel : ViewModelBase, IChartReferenceRowHost, IDisposable
{
    /// <summary>The tile size in logical pixels (design note §3.4: 40 px tiles).</summary>
    public const int GlyphSize = 40;

    private const int SearchLimit = 2000;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly ChartReferenceService _service;
    private readonly ChartReferenceGlyphCache _glyphs;
    private readonly ViewerSettingsService? _settings;
    private readonly Action<Action> _post;
    private readonly DispatcherTimer? _debounceTimer;
    private readonly Dictionary<(ReferenceKey, PaletteType), Task<IImage?>> _pendingGlyphs = [];

    // The glyph deliveries in flight, guarded by _pendingGlyphs' lock. Each
    // completes once its draw is done and the row's SetGlyph has been posted,
    // so GlyphsDrawn never completes in the gap between a draw finishing (and
    // leaving _pendingGlyphs) and its glyph being posted to the row.
    private readonly HashSet<Task> _glyphDeliveries = [];
    private ChartReferenceProduct? _selectedProduct;
    private ChartReferenceIndex? _index;
    private ChartReferenceArtwork? _artwork;
    private string _query = string.Empty;
    private ChartReferenceMode _mode;
    private IReadOnlyList<ChartReferenceListItem> _items = [];
    private ChartReferenceListItem? _selectedItem;
    private bool _isLoading;
    private bool _refreshingProducts;
    private int _loadVersion;
    private string? _summary;
    private bool _disposed;
    private bool _isActive;
    private ICommand? _openPageCommand;
    private ChartReferenceProductHeaderViewModel? _productHeader;
    private bool _selectingQuietly;

    /// <summary>
    /// Creates the activity's view model and selects the first product. Its
    /// index is built once the panel is first shown (<see cref="Activate"/>),
    /// not at startup.
    /// </summary>
    /// <param name="service">The chart reference indexes.</param>
    /// <param name="glyphs">The glyph cache, which follows the palette.</param>
    /// <param name="settings">
    /// The viewer settings, whose Simplified symbols chooses a feature type's
    /// lead glyph; <see langword="null"/> for Simplified symbols off.
    /// </param>
    /// <param name="post">
    /// Runs an action on the UI thread; <see langword="null"/> runs it at
    /// once on the calling thread.
    /// </param>
    public ChartReferenceViewModel(
        ChartReferenceService service,
        ChartReferenceGlyphCache glyphs,
        ViewerSettingsService? settings = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(glyphs);
        _service = service;
        _glyphs = glyphs;
        _settings = settings;
        _post = post ?? (a => a());

        ClearQueryCommand = new RelayCommand(() => Query = string.Empty);
        ShowFeaturesCommand = new RelayCommand(() => Mode = ChartReferenceMode.Features);
        ShowSymbolsCommand = new RelayCommand(() => Mode = ChartReferenceMode.Symbols);

        if (Avalonia.Application.Current is not null)
        {
            _debounceTimer = new DispatcherTimer { Interval = DebounceDelay };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                Rebuild();
            };
        }

        _service.Changed += OnCataloguesChanged;
        _glyphs.PaletteChanged += OnPaletteChanged;
        if (_settings is not null)
            _settings.PropertyChanged += OnSettingsChanged;

        RefreshProducts();
    }

    /// <summary>The products, those with datasets open first (D4).</summary>
    public ObservableCollection<ChartReferenceProduct> Products { get; } = [];

    /// <summary>The product the list shows; changing it loads that product's index.</summary>
    public ChartReferenceProduct? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            // The combo clears its selection while the products are replaced.
            if (_refreshingProducts || value is null && Products.Count > 0)
            {
                OnPropertyChanged();
                return;
            }

            if (Equals(_selectedProduct?.Spec, value?.Spec))
            {
                SetProperty(ref _selectedProduct, value);
                return;
            }

            SetProperty(ref _selectedProduct, value);
            if (_isActive)
                Loading = LoadAsync();
        }
    }

    /// <summary>The index of the selected product, or <see langword="null"/> while it loads or when there is none.</summary>
    public ChartReferenceIndex? Index => _index;

    /// <summary>The search text. A change searches 250 ms after the last keystroke; clearing it searches at once.</summary>
    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value ?? string.Empty))
                return;
            OnPropertyChanged(nameof(HasQuery));

            if (_debounceTimer is null || _query.Length == 0)
            {
                _debounceTimer?.Stop();
                Rebuild();
                return;
            }

            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    /// <summary>Whether there is search text, so the clear button shows.</summary>
    public bool HasQuery => _query.Length > 0;

    /// <summary>Whether the list shows feature types or symbols.</summary>
    public ChartReferenceMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
                return;
            OnPropertyChanged(nameof(ModeIndex));
            Rebuild();
        }
    }

    /// <summary><see cref="Mode"/> as the Features / Symbols segments' index.</summary>
    public int ModeIndex
    {
        get => (int)_mode;
        set => Mode = value == 1 ? ChartReferenceMode.Symbols : ChartReferenceMode.Features;
    }

    /// <summary>The list: section headers and rows, flat so the list virtualises.</summary>
    public IReadOnlyList<ChartReferenceListItem> Items
    {
        get => _items;
        private set => SetProperty(ref _items, value);
    }

    /// <summary>The rows of <see cref="Items"/>, without the headers.</summary>
    public IEnumerable<ChartReferenceRowViewModel> Rows => _items.OfType<ChartReferenceRowViewModel>();

    /// <summary>
    /// The selected row. Selecting one in the list opens its entry in the
    /// Reference tab (<see cref="OpenEntry"/>); <see cref="Open"/> selects
    /// without opening. A header can't be selected.
    /// </summary>
    public ChartReferenceListItem? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (value is { IsRow: false })
            {
                OnPropertyChanged();
                return;
            }

            if (!SetProperty(ref _selectedItem, value))
                return;
            OnPropertyChanged(nameof(SelectedRow));
            if (!_selectingQuietly && value is ChartReferenceRowViewModel row)
                OpenEntry?.Invoke(row.Key);
        }
    }

    /// <summary>
    /// Opens an entry in the Reference tab when a row is selected in the
    /// list (<see cref="ReferenceNavigator.Open(ReferenceKey)"/>, R7);
    /// <see langword="null"/>, in hosts without the tab, only selects.
    /// </summary>
    public Action<ReferenceKey>? OpenEntry { get; set; }

    /// <summary>The selected row, or <see langword="null"/>.</summary>
    public ChartReferenceRowViewModel? SelectedRow => _selectedItem as ChartReferenceRowViewModel;

    /// <summary>Whether the selected product's index is being built.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                RaiseStates();
        }
    }

    /// <summary>Whether there is no catalogue to show: no product, or one whose index couldn't be built.</summary>
    public bool ShowNoCatalogue => _isActive && !_isLoading && _index is null;

    /// <summary>Whether a search found nothing.</summary>
    public bool ShowNoMatches => !_isLoading && _index is not null && _items.Count == 0;

    /// <summary>What the no-matches placeholder suggests: an S-57 acronym, or the other mode.</summary>
    public string NoMatchesDescription => _inView && !HasQuery ? Strings.ChartReference_NoneInView
        : _mode == ChartReferenceMode.Features
        ? Strings.ChartReference_NoMatchesFeatures
        : Strings.ChartReference_NoMatchesSymbols;

    /// <summary>The footer: how many entries the list shows.</summary>
    public string? Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    /// <inheritdoc />
    public IBrush Tile => _glyphs.Tile;

    /// <summary>Clears the search.</summary>
    public ICommand ClearQueryCommand { get; }

    /// <summary>Lists feature types.</summary>
    public ICommand ShowFeaturesCommand { get; }

    /// <summary>Lists symbols.</summary>
    public ICommand ShowSymbolsCommand { get; }

    /// <summary>
    /// Shows the reference as a page over the map (Open as page, #1639);
    /// <see langword="null"/>, without the app, hides the button.
    /// </summary>
    public ICommand? OpenPageCommand
    {
        get => _openPageCommand;
        set
        {
            if (SetProperty(ref _openPageCommand, value))
                OnPropertyChanged(nameof(CanOpenPage));
        }
    }

    /// <summary>Whether the activity shows Open as page.</summary>
    public bool CanOpenPage => _openPageCommand is not null;

    /// <summary>
    /// The product header (catalogue-panels D8, D9, CR4): the selected
    /// product's catalogues in use, Catalogues… and the eXaminer link; the
    /// activity and its page share it. <see langword="null"/>, without the
    /// app's catalogue registry, hides it.
    /// </summary>
    public ChartReferenceProductHeaderViewModel? ProductHeader
    {
        get => _productHeader;
        set => SetProperty(ref _productHeader, value);
    }

    /// <summary>The latest load of an index; tests wait on it.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    /// <summary>Completes when the glyphs being drawn now are done; tests wait on it.</summary>
    internal Task GlyphsDrawn
    {
        get
        {
            lock (_pendingGlyphs)
                return Task.WhenAll(_glyphDeliveries.ToArray());
        }
    }

    /// <summary>
    /// Starts showing the list: loads the selected product's index the first
    /// time. The view calls it when it is first shown, so the app doesn't
    /// build an index at startup for a panel that may never open.
    /// </summary>
    public void Activate()
    {
        if (_isActive || _disposed)
            return;
        _isActive = true;
        RefreshProducts();
        Loading = LoadAsync();
    }

    /// <summary>
    /// Reads the product list again, after datasets open or close or a
    /// catalogue is added, keeping the selected product when it is still
    /// listed.
    /// </summary>
    public void RefreshProducts()
    {
        var products = _service.Products;
        if (products.SequenceEqual(Products))
            return;

        var selected = _selectedProduct?.Spec;
        _refreshingProducts = true;
        try
        {
            Products.Clear();
            foreach (var product in products)
                Products.Add(product);
        }
        finally
        {
            _refreshingProducts = false;
        }

        var keep = Products.FirstOrDefault(p => Equals(p.Spec, selected)) ?? Products.FirstOrDefault();
        if (Equals(keep?.Spec, selected))
        {
            _selectedProduct = keep;
            OnPropertyChanged(nameof(SelectedProduct));
            if (keep is null)
                RaiseStates();
        }
        else
        {
            SelectedProduct = keep;
            if (keep is null)
            {
                _index = null;
                Rebuild();
            }
        }
    }

    /// <summary>
    /// Selects an entry's row without opening it in the Reference tab (the
    /// study page's tiles): switches to Features or Symbols, and clears or
    /// sets the search, so the row is in the list.
    /// </summary>
    /// <param name="key">The entry.</param>
    public void Open(ReferenceKey key)
    {
        _selectingQuietly = true;
        try
        {
            Select(key);
        }
        finally
        {
            _selectingQuietly = false;
        }
    }

    private void Select(ReferenceKey key)
    {
        var mode = key.Kind == ReferenceKind.FeatureType ? ChartReferenceMode.Features : ChartReferenceMode.Symbols;
        var row = Find(key);
        if (row is null || _mode != mode)
        {
            SetQuietly(string.Empty, mode);
            row = Find(key);
        }

        if (row is null)
        {
            // A family member isn't listed on its own; search for it.
            SetQuietly(key.Code, mode);
            row = Find(key);
        }

        if (row is not null)
            SelectedItem = row;
    }

    /// <inheritdoc />
    public IImage? RequestGlyph(ChartReferenceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var lead = row.Lead;
        var artwork = _artwork;
        if (!lead.HasGlyph || artwork is null)
            return null;
        if (_glyphs.TryGet(lead.Key, GlyphSize, GlyphSize, out var held))
            return held;

        var palette = _glyphs.Palette;
        Task<IImage?> draw;
        lock (_pendingGlyphs)
        {
            if (!_pendingGlyphs.TryGetValue((lead.Key, palette), out draw!))
            {
                draw = Task.Run(() => DrawAsync(artwork, lead, palette));
                _pendingGlyphs[(lead.Key, palette)] = draw;
                draw.ContinueWith(
                    _ =>
                    {
                        lock (_pendingGlyphs)
                            _pendingGlyphs.Remove((lead.Key, palette));
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        var delivery = draw.ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully && _glyphs.Palette == palette)
                    _post(() => row.SetGlyph(t.Result));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        lock (_pendingGlyphs)
            _glyphDeliveries.Add(delivery);
        delivery.ContinueWith(
            d =>
            {
                lock (_pendingGlyphs)
                    _glyphDeliveries.Remove(d);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _productHeader?.Dispose();
        _debounceTimer?.Stop();
        _service.Changed -= OnCataloguesChanged;
        _glyphs.PaletteChanged -= OnPaletteChanged;
        if (_settings is not null)
            _settings.PropertyChanged -= OnSettingsChanged;
        if (_map is not null)
            _map.Changed -= OnMapChanged;
    }

    private async Task<IImage?> DrawAsync(ChartReferenceArtwork artwork, LeadGlyph lead, PaletteType palette)
    {
        try
        {
            var entry = await artwork.BuildAsync(lead, palette).ConfigureAwait(false);
            // Drawn for a palette that has since changed: the rows ask again.
            return _glyphs.Palette != palette
                ? null
                : _glyphs.Get(lead.Key, GlyphSize, GlyphSize, _ => entry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A glyph that can't be drawn leaves its tile empty.
            return null;
        }
    }

    private async Task LoadAsync()
    {
        var product = _selectedProduct;
        var version = ++_loadVersion;
        _index = null;
        _artwork = null;
        IsLoading = product is not null;
        Rebuild();
        if (product is null)
            return;

        ChartReferenceIndex? index;
        try
        {
            index = await _service.GetIndexAsync(product.Spec);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ObjectDisposedException)
        {
            // A catalogue whose rules can't be read shows the no-catalogue state.
            index = null;
        }

        void Apply()
        {
            if (version != _loadVersion || _disposed)
                return;
            _index = index;
            _artwork = index is null ? null : _service.GetArtwork(product.Spec);
            OnPropertyChanged(nameof(Index));
            IsLoading = false;
            Rebuild();
        }

        _post(Apply);
    }

    /// <summary>
    /// Raised after the list is rebuilt for a new mode, product, search or In
    /// view setting (#1657), so a view starts it at the top, or brings the
    /// selected row into view when it is still listed. Not raised when In view
    /// follows the map.
    /// </summary>
    internal event EventHandler? ListRestarted;

    private void Rebuild(bool restart = true)
    {
        // A new list ends a row's hover (#1655); following the map keeps it,
        // so the row under the pointer rings again once the Legend follows.
        if (restart)
            _map?.Leave(null);
        var keep = (_selectedItem as ChartReferenceRowViewModel)?.Key;
        _selectedItem = null;

        var index = _index;
        if (index is null)
        {
            Items = [];
            Summary = null;
        }
        else if (string.IsNullOrWhiteSpace(_query))
        {
            Items = _mode == ChartReferenceMode.Features ? BrowseFeatures(index) : BrowseItems(index);
            var count = Rows.Count();
            Summary = _mode == ChartReferenceMode.Features
                ? Count(count, Strings.ChartReference_FeatureCountOne, Strings.ChartReference_FeatureCount)
                : Count(count, Strings.ChartReference_SymbolCountOne, Strings.ChartReference_SymbolCount);
        }
        else
        {
            Items = Search(index, _query.Trim());
            var count = _items.Count;
            Summary = count == 0 ? null : Count(count, Strings.ChartReference_MatchCountOne, Strings.ChartReference_MatchCount);
        }

        OnPropertyChanged(nameof(SelectedItem));
        OnPropertyChanged(nameof(SelectedRow));
        if (keep is { } key && Find(key) is { } again)
        {
            _selectingQuietly = true;
            SelectedItem = again;
            _selectingQuietly = false;
        }
        RaiseStates();
        if (restart)
            ListRestarted?.Invoke(this, EventArgs.Empty);
    }

    // "1 match", "N matches": the singular and plural are separate strings.
    private static string Count(int count, string one, string many) =>
        string.Format(CultureInfo.CurrentCulture, count == 1 ? one : many, count);

    private List<ChartReferenceListItem> BrowseFeatures(ChartReferenceIndex index)
    {
        bool simplified = _settings?.SimplifiedSymbols ?? false;
        var rows = index.FeatureTypes
            .Where(f => !f.IsAbstract && InViewAllows(f))
            .Select(f => (Section: ChartReferenceSection.Of(f.PrimaryViewingGroup), Row: Row(f, index, simplified)));
        return Grouped(rows, Titles(index), unreached: []);
    }

    private List<ChartReferenceListItem> BrowseItems(ChartReferenceIndex index)
    {
        var featureSections = index.FeatureTypes.ToDictionary(
            f => f.Code,
            f => ChartReferenceSection.Of(f.PrimaryViewingGroup),
            StringComparer.OrdinalIgnoreCase);

        // Family members are listed under their family, not on their own.
        var items = index.Items.Where(i => i.Family is null && InViewAllows(i)).ToList();
        var reached = items
            .Where(i => !i.IsUnreached)
            .Select(i => (Section: SectionOfItem(i, index, featureSections), Row: Row(i)));
        var unreached = items
            .Where(i => i.IsUnreached)
            .Select(i => Row(i))
            .ToList();
        return Grouped(reached, Titles(index), unreached);
    }

    private List<ChartReferenceListItem> Search(ChartReferenceIndex index, string query)
    {
        bool simplified = _settings?.SimplifiedSymbols ?? false;
        var kind = _mode == ChartReferenceMode.Features ? ReferenceKind.FeatureType : (ReferenceKind?)null;
        return
        [
            .. index.Search(query, kind, SearchLimit)
                .Select(h => h.Entry)
                .Where(InViewAllows)
                .Select(e => e switch
                {
                    FeatureTypeEntry f when _mode == ChartReferenceMode.Features => Row(f, index, simplified),
                    PortrayalItemEntry i when _mode == ChartReferenceMode.Symbols => Row(i),
                    _ => null,
                })
                .OfType<ChartReferenceRowViewModel>(),
        ];
    }

    // Sections in Chart 1 order, then "No Chart 1 section", then "Not used
    // by a rule" (D3); rows by name within each.
    private static List<ChartReferenceListItem> Grouped(
        IEnumerable<(ChartReferenceSection? Section, ChartReferenceRowViewModel Row)> rows,
        IReadOnlyDictionary<string, string> titles,
        IReadOnlyList<ChartReferenceRowViewModel> unreached)
    {
        var items = new List<ChartReferenceListItem>();
        var groups = rows
            .GroupBy(r => r.Section?.Key)
            .OrderBy(g => g.Key is null ? 1 : 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var sorted = group.Select(r => r.Row).OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            var title = group.Key is null
                ? Strings.ChartReference_NoSection
                : titles.GetValueOrDefault(group.Key, group.Key);
            items.Add(new ChartReferenceHeaderViewModel(title, sorted.Count));
            items.AddRange(sorted);
        }

        if (unreached.Count > 0)
        {
            items.Add(new ChartReferenceHeaderViewModel(Strings.ChartReference_NotUsedByRule, unreached.Count));
            items.AddRange(unreached.OrderBy(r => r.Code, StringComparer.Ordinal));
        }

        return items;
    }

    // Each section's title as most of its feature types' viewing groups spell it.
    private static Dictionary<string, string> Titles(ChartReferenceIndex index) =>
        index.FeatureTypes
            .Select(f => ChartReferenceSection.Of(f.PrimaryViewingGroup))
            .OfType<ChartReferenceSection>()
            .GroupBy(s => s.Key)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(s => s.Title).OrderByDescending(t => t.Count()).ThenBy(t => t.Key, StringComparer.Ordinal).First().Key);

    // An item's section: the one most of the feature types that use it are
    // in, or that use the line styles and fills that place it.
    private static ChartReferenceSection? SectionOfItem(
        PortrayalItemEntry item,
        ChartReferenceIndex index,
        Dictionary<string, ChartReferenceSection?> featureSections) =>
        (item.UsedBy.Count > 0
            ? item.UsedBy
            : item.UsedIn
                .Select(key => index.TryGet(key, out var e) ? e as PortrayalItemEntry : null)
                .OfType<PortrayalItemEntry>()
                .SelectMany(c => c.UsedBy))
            .Select(code => featureSections.GetValueOrDefault(code))
            .OfType<ChartReferenceSection>()
            .GroupBy(s => s.Key)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.First())
            .FirstOrDefault();

    private ChartReferenceRowViewModel Row(FeatureTypeEntry feature, ChartReferenceIndex index, bool simplified) =>
        new(feature, LeadGlyph.For(feature, index, simplified), this);

    private ChartReferenceRowViewModel Row(PortrayalItemEntry item) =>
        new(item, LeadGlyph.For(item), this);

    private ChartReferenceRowViewModel? Find(ReferenceKey key) =>
        Rows.FirstOrDefault(r => r.Key.Kind == key.Kind && string.Equals(r.Key.Code, key.Code, StringComparison.OrdinalIgnoreCase));

    private void SetQuietly(string query, ChartReferenceMode mode)
    {
        _debounceTimer?.Stop();
        if (SetProperty(ref _query, query, nameof(Query)))
            OnPropertyChanged(nameof(HasQuery));
        if (SetProperty(ref _mode, mode, nameof(Mode)))
            OnPropertyChanged(nameof(ModeIndex));
        Rebuild();
    }

    private void RaiseStates()
    {
        OnPropertyChanged(nameof(ShowNoCatalogue));
        OnPropertyChanged(nameof(ShowNoMatches));
        OnPropertyChanged(nameof(NoMatchesDescription));
    }

    private void OnCataloguesChanged(object? sender, CataloguesChangedEventArgs e) => _post(() =>
    {
        if (_disposed)
            return;
        RefreshProducts();
        if (_isActive && _selectedProduct is { } product && product.Spec.Name == e.Spec.Name)
            Loading = LoadAsync();
    });

    private void OnPaletteChanged(object? sender, EventArgs e) => _post(() =>
    {
        foreach (var row in Rows)
            row.ResetGlyph();
        OnPropertyChanged(nameof(Tile));
    });

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewerSettingsService.SimplifiedSymbols) && _mode == ChartReferenceMode.Features)
            _post(() => Rebuild(restart: false));
    }
}
