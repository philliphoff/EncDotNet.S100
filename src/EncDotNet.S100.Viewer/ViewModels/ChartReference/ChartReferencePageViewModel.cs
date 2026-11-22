using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// The Chart reference page (#1639, symbol dictionary design note §4.5, R6):
/// the reference over the map for study, as a tile grid by Chart 1 section
/// with the selected entry on the right.
/// </summary>
/// <remarks>
/// <para>
/// The page shares <see cref="ChartReferenceViewModel"/> with the activity,
/// so the product, search, Features / Symbols and the selection carry over
/// both ways. It shows the same entries in the same order, as tiles.
/// </para>
/// <para>
/// The <b>Day / Dusk / Night</b> and <b>Simplified / Paper chart</b> switches
/// only preview: they choose the page's own palette and symbol style, drawn
/// through the page's own <see cref="ChartReferenceGlyphCache"/>, and never
/// change <see cref="ViewerSettingsService"/>. Each time the page is shown
/// (<see cref="Show"/>) they start from the chart's current values.
/// </para>
/// </remarks>
internal sealed class ChartReferencePageViewModel : ViewModelBase, IChartReferenceRowHost, IDisposable
{
    /// <summary>The selected entry's large glyph, in logical pixels.</summary>
    public const int HeroGlyphSize = 96;

    /// <summary>The narrowest a tile gets, in logical pixels (the prototype's 150 px columns).</summary>
    public const double MinTileWidth = 150;

    /// <summary>The gap between tiles, in logical pixels.</summary>
    public const double TileSpacing = 10;

    private readonly ChartReferenceViewModel _reference;
    private readonly ChartReferenceService _service;
    private readonly ViewerSettingsService? _settings;
    private readonly ChartReferenceGlyphCache _glyphs;
    private readonly Action<Action> _post;
    private readonly Dictionary<(ReferenceKey, PaletteType, int), Task<IImage?>> _pending = [];
    private ChartReferenceArtwork? _artwork;
    private bool _previewSimplified;
    private int _columns = 4;
    private IReadOnlyList<ChartReferenceRowViewModel> _tiles = [];
    private IReadOnlyList<object> _lines = [];
    private ChartReferenceRowViewModel? _selectedTile;
    private IImage? _heroGlyph;
    private bool _disposed;

    /// <summary>Creates the page over the activity's view model.</summary>
    /// <param name="reference">The activity's view model, which holds the product, search and selection.</param>
    /// <param name="service">The chart reference indexes, whose artwork the page draws its glyphs from.</param>
    /// <param name="settings">
    /// The viewer settings the preview switches start from; read, never
    /// written. <see langword="null"/> starts at Day and Paper chart.
    /// </param>
    /// <param name="post">
    /// Runs an action on the UI thread; <see langword="null"/> runs it at
    /// once on the calling thread.
    /// </param>
    public ChartReferencePageViewModel(
        ChartReferenceViewModel reference,
        ChartReferenceService service,
        ViewerSettingsService? settings = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(service);
        _reference = reference;
        _service = service;
        _settings = settings;
        _post = post ?? (a => a());
        _glyphs = new ChartReferenceGlyphCache(settings?.SelectedPalette ?? PaletteType.Day);
        _previewSimplified = settings?.SimplifiedSymbols ?? false;

        SelectCommand = new RelayCommand<ChartReferenceRowViewModel>(Select);
        _glyphs.PaletteChanged += OnPaletteChanged;
        _reference.PropertyChanged += OnReferenceChanged;
        _reference.ListRestarted += OnListRestarted;
        Rebuild();
    }

    /// <summary>
    /// Raised after the grid is rebuilt for a new mode, product or search
    /// (#1657), so the view starts it at the top, or brings the selected tile
    /// into view (<see cref="SelectedLineIndex"/>).
    /// </summary>
    internal event EventHandler? GridRestarted;

    /// <summary>The index in <see cref="Lines"/> of the line holding the selected tile, or -1.</summary>
    internal int SelectedLineIndex
    {
        get
        {
            if (_selectedTile is not { } tile)
                return -1;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (_lines[i] is ChartReferenceTileLine line && line.Tiles.Contains(tile))
                    return i;
            }

            return -1;
        }
    }

    /// <summary>The activity's view model: products, search, Features / Symbols and the selection.</summary>
    public ChartReferenceViewModel Reference => _reference;

    /// <summary>The palette the page previews its glyphs in; the chart's palette doesn't change.</summary>
    public PaletteType PreviewPalette
    {
        get => _glyphs.Palette;
        set
        {
            if (_glyphs.Palette == value)
                return;
            _glyphs.SetPalette(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(PaletteIndex));
        }
    }

    /// <summary><see cref="PreviewPalette"/> as the Day / Dusk / Night segments' index.</summary>
    public int PaletteIndex
    {
        get => (int)PreviewPalette;
        set => PreviewPalette = value switch
        {
            1 => PaletteType.Dusk,
            2 => PaletteType.Night,
            _ => PaletteType.Day,
        };
    }

    /// <summary>
    /// Whether the page leads feature types with their simplified symbols
    /// rather than paper-chart ones; the chart's Simplified symbols doesn't change.
    /// </summary>
    public bool PreviewSimplified
    {
        get => _previewSimplified;
        set
        {
            if (!SetProperty(ref _previewSimplified, value))
                return;
            OnPropertyChanged(nameof(SymbolStyleIndex));
            Rebuild();
        }
    }

    /// <summary><see cref="PreviewSimplified"/> as the Simplified / Paper chart segments' index.</summary>
    public int SymbolStyleIndex
    {
        get => _previewSimplified ? 0 : 1;
        set => PreviewSimplified = value == 0;
    }

    /// <summary>How many tiles a line holds; the view sets it from its width.</summary>
    public int Columns
    {
        get => _columns;
        set
        {
            if (SetProperty(ref _columns, Math.Max(1, value)))
                Lines = Lay(_reference.Items, _tiles, _columns);
        }
    }

    /// <summary>The tiles, in the activity's order.</summary>
    public IReadOnlyList<ChartReferenceRowViewModel> Tiles
    {
        get => _tiles;
        private set => SetProperty(ref _tiles, value);
    }

    /// <summary>
    /// The grid, flat so it virtualises: section headers
    /// (<see cref="ChartReferenceHeaderViewModel"/>) and lines of up to
    /// <see cref="Columns"/> tiles (<see cref="ChartReferenceTileLine"/>).
    /// </summary>
    public IReadOnlyList<object> Lines
    {
        get => _lines;
        private set => SetProperty(ref _lines, value);
    }

    /// <summary>The selected tile, whose entry shows on the right; it follows the activity's selection.</summary>
    public ChartReferenceRowViewModel? SelectedTile
    {
        get => _selectedTile;
        private set
        {
            var previous = _selectedTile;
            if (!SetProperty(ref _selectedTile, value))
                return;
            if (previous is not null)
                previous.IsExpanded = false;
            if (value is not null)
                value.IsExpanded = true;
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(ShowNoSelection));
            RequestHeroGlyph();
        }
    }

    /// <summary>Whether a tile is selected.</summary>
    public bool HasSelection => _selectedTile is not null;

    /// <summary>Whether the entry pane says to choose a tile.</summary>
    public bool ShowNoSelection => _selectedTile is null && _tiles.Count > 0;

    /// <summary>The selected entry's large glyph, or <see langword="null"/> until it is drawn.</summary>
    public IImage? HeroGlyph
    {
        get => _heroGlyph;
        private set => SetProperty(ref _heroGlyph, value);
    }

    /// <inheritdoc />
    public IBrush Tile => _glyphs.Tile;

    /// <summary>Selects a tile's entry, in the page and in the activity.</summary>
    public ICommand SelectCommand { get; }

    /// <summary>Completes when the glyphs being drawn now are done; tests wait on it.</summary>
    internal Task GlyphsDrawn
    {
        get
        {
            lock (_pending)
                return Task.WhenAll(_pending.Values);
        }
    }

    /// <summary>
    /// Starts the page each time it is shown: the switches take the chart's
    /// current palette and symbol style, and the activity's index loads if it
    /// hasn't yet.
    /// </summary>
    public void Show()
    {
        if (_disposed)
            return;
        if (_settings is not null)
        {
            PreviewPalette = _settings.SelectedPalette;
            PreviewSimplified = _settings.SimplifiedSymbols;
        }

        _reference.Activate();
    }

    /// <inheritdoc />
    public IImage? RequestGlyph(ChartReferenceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var lead = row.Lead;
        var size = ChartReferenceViewModel.GlyphSize;
        if (!lead.HasGlyph || _artwork is null)
            return null;
        if (_glyphs.TryGet(lead.Key, size, size, out var held))
            return held;

        var palette = _glyphs.Palette;
        Draw(lead, palette, size).ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully && _glyphs.Palette == palette)
                    _post(() => row.SetGlyph(t.Result));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return null;
    }

    /// <inheritdoc />
    public void Open(ReferenceKey key) => _reference.Open(key);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reference.PropertyChanged -= OnReferenceChanged;
        _reference.ListRestarted -= OnListRestarted;
        _glyphs.PaletteChanged -= OnPaletteChanged;
        _glyphs.Dispose();
    }

    private void Select(ChartReferenceRowViewModel? tile)
    {
        if (tile is not null)
            _reference.Open(tile.Key);
    }

    // The tiles mirror the activity's rows, with the page's own lead glyphs.
    private void Rebuild()
    {
        var index = _reference.Index;
        _artwork = index is null ? null : _service.GetArtwork(index.Spec);
        var tiles = index is null
            ? []
            : _reference.Rows.Select(r => MakeTile(r, index)).ToList();
        if (_selectedTile is { } previous)
            previous.IsExpanded = false;
        _selectedTile = null;
        Tiles = tiles;
        Lines = Lay(_reference.Items, tiles, _columns);
        OnPropertyChanged(nameof(SelectedTile));
        SyncSelection();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ShowNoSelection));
    }

    private ChartReferenceRowViewModel MakeTile(ChartReferenceRowViewModel row, ChartReferenceIndex index)
    {
        var lead = row.Entry is FeatureTypeEntry feature
            ? LeadGlyph.For(feature, index, _previewSimplified)
            : row.Lead;
        return new ChartReferenceRowViewModel(row.Entry, index, lead, this);
    }

    // Headers as they are; each section's tiles in lines of `columns`. A
    // search has no headers: its tiles are one untitled run.
    private static List<object> Lay(
        IReadOnlyList<ChartReferenceListItem> items,
        IReadOnlyList<ChartReferenceRowViewModel> tiles,
        int columns)
    {
        var lines = new List<object>();
        var run = new List<ChartReferenceRowViewModel>();
        int next = 0;

        void Flush()
        {
            for (int i = 0; i < run.Count; i += columns)
                lines.Add(new ChartReferenceTileLine([.. run.Skip(i).Take(columns)], columns));
            run.Clear();
        }

        foreach (var item in items)
        {
            if (item is ChartReferenceHeaderViewModel header)
            {
                Flush();
                lines.Add(header);
            }
            else if (next < tiles.Count)
            {
                run.Add(tiles[next++]);
            }
        }

        Flush();
        return lines;
    }

    private void SyncSelection()
    {
        var key = _reference.SelectedRow?.Key;
        SelectedTile = key is null ? null : _tiles.FirstOrDefault(t => t.Key == key);
    }

    private void RequestHeroGlyph()
    {
        HeroGlyph = null;
        var tile = _selectedTile;
        if (tile is null || !tile.Lead.HasGlyph || _artwork is null)
            return;
        if (_glyphs.TryGet(tile.Lead.Key, HeroGlyphSize, HeroGlyphSize, out var held))
        {
            HeroGlyph = held;
            return;
        }

        var palette = _glyphs.Palette;
        Draw(tile.Lead, palette, HeroGlyphSize).ContinueWith(
            t =>
            {
                if (t.IsCompletedSuccessfully)
                    _post(() =>
                    {
                        if (ReferenceEquals(_selectedTile, tile) && _glyphs.Palette == palette)
                            HeroGlyph = t.Result;
                    });
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private Task<IImage?> Draw(LeadGlyph lead, PaletteType palette, int size)
    {
        var artwork = _artwork!;
        var key = (lead.Key, palette, size);
        lock (_pending)
        {
            if (_pending.TryGetValue(key, out var pending))
                return pending;
            var draw = Task.Run(() => DrawAsync(artwork, lead, palette, size));
            _pending[key] = draw;
            draw.ContinueWith(
                _ =>
                {
                    lock (_pending)
                        _pending.Remove(key);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return draw;
        }
    }

    private async Task<IImage?> DrawAsync(ChartReferenceArtwork artwork, LeadGlyph lead, PaletteType palette, int size)
    {
        try
        {
            var entry = await artwork.BuildAsync(lead, palette).ConfigureAwait(false);
            // Drawn for a palette that has since changed: the tiles ask again.
            return _glyphs.Palette != palette
                ? null
                : _glyphs.Get(lead.Key, size, size, _ => entry);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A glyph that can't be drawn leaves its tile empty.
            return null;
        }
    }

    private void OnReferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
            return;
        switch (e.PropertyName)
        {
            case nameof(ChartReferenceViewModel.Items):
                Rebuild();
                break;
            case nameof(ChartReferenceViewModel.SelectedRow):
                SyncSelection();
                break;
        }
    }

    private void OnListRestarted(object? sender, EventArgs e)
    {
        if (!_disposed)
            GridRestarted?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaletteChanged(object? sender, EventArgs e) => _post(() =>
    {
        foreach (var tile in _tiles)
            tile.ResetGlyph();
        OnPropertyChanged(nameof(Tile));
        RequestHeroGlyph();
    });
}
