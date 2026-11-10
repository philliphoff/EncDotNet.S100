using System.Collections.Specialized;
using System.ComponentModel;
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>One AML product as the display panel's Additional Military Layers section lists it.</summary>
/// <param name="Code">The product's code (<c>CLB</c>, …), or <see cref="AmlDisplayService.IntegratedCode"/> for the AML 3.0 integrated product.</param>
/// <param name="Name">The product's full name, such as "Contour Line Bathymetry".</param>
/// <param name="Swatch">The product's main colour family's Day ink, <c>#RRGGBB</c>.</param>
/// <param name="CellCount">How many cells of the product are loaded.</param>
/// <param name="ShownCount">How many of them the map draws now, after the switches and scale bands.</param>
/// <param name="IsVisible">Whether the product's switch is on.</param>
internal sealed record AmlProductState(string Code, string Name, string Swatch, int CellCount, int ShownCount, bool IsVisible)
{
    /// <summary>Whether any cell of the product is loaded.</summary>
    public bool IsLoaded => CellCount > 0;
}

/// <summary>
/// Applies the Additional Military Layers display controls to the map (AML
/// handoff decisions 24 and 26): each product's switch, and the AML scale
/// bands (<see cref="AmlScaleBandVisibility"/>) while Follow AML scale bands is
/// on. It hides AML cells through <see cref="DatasetEntry.IsHiddenByDisplay"/>,
/// so the Datasets panel's own eye keeps its state and nothing is re-rendered.
/// </summary>
/// <remarks>
/// It also says whether CLB contours are drawn, for the corner chip
/// "AML · CLB not used for depth checks" (decision 23): the chip shows while
/// any drawn CLB cell is shown.
/// </remarks>
internal sealed class AmlDisplayService : IDisposable
{
    /// <summary>The code the section uses for the AML 3.0 integrated product, which has none of its own.</summary>
    public const string IntegratedCode = "3.0";

    /// <summary>The CLB product code.</summary>
    public const string ClbCode = "CLB";

    // The order of the Library's AML product menu (decision 5).
    private static readonly string[] ProductOrder = ["CLB", "ESB", "LBO", "SBO", "MFF", "RAL", IntegratedCode];

    private readonly DatasetsViewModel _datasets;
    private readonly IDatasetLoaderService _loader;
    private readonly EcdisDisplayState _state;
    private readonly IMapViewportNotifier _viewport;
    private readonly Action<Action> _marshal;
    private readonly HashSet<DatasetEntry> _subscribed = [];
    private readonly Dictionary<DatasetEntry, Cell> _seen = [];
    private List<Cell> _cells = [];
    private double _scale = double.NaN;
    private bool _disposed;

    /// <summary>Creates the service and applies the current state.</summary>
    /// <param name="datasets">The loaded datasets.</param>
    /// <param name="loader">The loader, whose processors hold each AML cell's facts.</param>
    /// <param name="state">The display state holding the AML switches.</param>
    /// <param name="viewport">The map viewport, for the scale.</param>
    /// <param name="marshal">Runs an action on the UI thread.</param>
    public AmlDisplayService(
        DatasetsViewModel datasets,
        IDatasetLoaderService loader,
        EcdisDisplayState state,
        IMapViewportNotifier viewport,
        Action<Action> marshal)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(marshal);
        _datasets = datasets;
        _loader = loader;
        _state = state;
        _viewport = viewport;
        _marshal = marshal;

        _scale = ScaleOf(viewport.Current);
        _datasets.Entries.CollectionChanged += OnEntriesChanged;
        _loader.DatasetLoaded += OnDatasetChanged;
        _loader.DatasetRemoved += OnDatasetChanged;
        _state.AmlDisplayChanged += OnSwitchesChanged;
        _viewport.ViewportChanged += OnViewportChanged;
        foreach (var entry in _datasets.Entries)
            Subscribe(entry);
        Rebuild();
    }

    /// <summary>Raised on the UI thread after the products, the cells shown or the chip change.</summary>
    public event Action? Changed;

    /// <summary>Whether any AML cell is loaded: the section shows only then.</summary>
    public bool HasAml => _cells.Count > 0;

    /// <summary>Every AML product, loaded ones first, each in the Library menu's order.</summary>
    public IReadOnlyList<AmlProductState> Products { get; private set; } = [];

    /// <summary>Whether a drawn CLB cell is shown, so the "not used for depth checks" chip shows.</summary>
    public bool IsClbDrawn { get; private set; }

    /// <summary>The map scale denominator the bands were last applied at; NaN before the map has a viewport.</summary>
    public double ScaleDenominator => _scale;

    /// <summary>The switch of <paramref name="code"/>, as <see cref="EcdisDisplayState.HiddenAmlProducts"/> holds it.</summary>
    /// <param name="code">The product code.</param>
    public bool IsProductVisible(string code) => !_state.HiddenAmlProducts.Contains(code);

    /// <summary>Whether the AML scale bands drive visibility.</summary>
    public bool FollowScaleBands => _state.FollowAmlScaleBands;

    /// <summary>Turns <paramref name="code"/>'s switch on or off.</summary>
    /// <param name="code">The product code (<c>CLB</c>, …, or <see cref="IntegratedCode"/>).</param>
    /// <param name="visible">Whether the product shows.</param>
    /// <exception cref="ArgumentException"><paramref name="code"/> isn't an AML product.</exception>
    public void SetProductVisible(string code, bool visible)
    {
        if (NormaliseCode(code) is not { } known)
            throw new ArgumentException($"'{code}' isn't an AML product; use one of {string.Join(", ", ProductOrder)}.", nameof(code));
        _state.SetAmlProductVisible(known, visible);
    }

    /// <summary>Turns Follow AML scale bands on or off.</summary>
    /// <param name="follow">Whether the bands apply.</param>
    public void SetFollowScaleBands(bool follow) => _state.SetFollowAmlScaleBands(follow);

    /// <summary>The known product code <paramref name="code"/> stands for, in its canonical case, or <see langword="null"/>.</summary>
    /// <param name="code">A product code in any case.</param>
    public static string? NormaliseCode(string? code) =>
        ProductOrder.FirstOrDefault(c => string.Equals(c, code?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The section's code for a cell's product.</summary>
    /// <param name="info">The cell's AML facts.</param>
    public static string CodeOf(AmlDatasetInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return NormaliseCode(info.ProductCode) ?? IntegratedCode;
    }

    /// <summary>The product's main colour family (the swatch, B4a): RAL is air, the seabed products seabed, ESB environmental.</summary>
    /// <param name="code">The product code.</param>
    public static AmlFamily? FamilyOf(string code) => code switch
    {
        "RAL" => AmlFamily.Air,
        "CLB" or "LBO" or "SBO" => AmlFamily.Seabed,
        "ESB" => AmlFamily.Environmental,
        _ => null,
    };

    /// <summary>The product's full name.</summary>
    /// <param name="code">The product code.</param>
    public static string NameOf(string code) => AmlProducts.Name(code) ?? AmlProducts.IntegratedName;

    // MFF and the integrated product draw in several families: neutral ink (the AML tag's).
    private static string SwatchOf(string code) => FamilyOf(code)?.Colors.Day.ToHex() ?? "#52525B";

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (DatasetEntry entry in e.OldItems)
                Unsubscribe(entry);
        }

        if (e.NewItems is not null)
        {
            foreach (DatasetEntry entry in e.NewItems)
                Subscribe(entry);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var entry in _subscribed.ToArray())
                Unsubscribe(entry);
            foreach (var entry in _datasets.Entries)
                Subscribe(entry);
        }

        _marshal(Rebuild);
    }

    private void Subscribe(DatasetEntry entry)
    {
        if (_subscribed.Add(entry))
            entry.PropertyChanged += OnEntryPropertyChanged;
    }

    private void Unsubscribe(DatasetEntry entry)
    {
        if (_subscribed.Remove(entry))
            entry.PropertyChanged -= OnEntryPropertyChanged;
    }

    // A cell the row's eye hides doesn't cover coarser cells.
    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatasetEntry.IsVisible) && sender is DatasetEntry entry && _cells.Any(c => c.Entry == entry))
            _marshal(Rebuild);
    }

    private void OnDatasetChanged(DatasetEntry entry) => _marshal(Rebuild);

    private void OnSwitchesChanged() => _marshal(Apply);

    private void OnViewportChanged(object? sender, MapViewportSnapshot snapshot)
    {
        var scale = ScaleOf(snapshot);
        _marshal(() =>
        {
            if (SameScale(scale, _scale))
                return;
            _scale = scale;
            Apply();
        });
    }

    private static bool SameScale(double a, double b) =>
        (double.IsNaN(a) && double.IsNaN(b)) || Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * 1e-6;

    private static double ScaleOf(MapViewportSnapshot? snapshot) => snapshot is null
        ? double.NaN
        : LazyCellGate.ScaleDenominator(snapshot.MercatorResolution, (snapshot.MinLatitude + snapshot.MaxLatitude) / 2.0);

    /// <summary>Reads the AML cells again and applies the controls.</summary>
    internal void Rebuild()
    {
        if (_disposed)
            return;

        // A processor the loader doesn't hold right now isn't in its snapshot,
        // so each cell's facts are kept from when it was last seen.
        var cells = new List<Cell>();
        using (var processors = _loader.AcquireProcessors())
        {
            foreach (var entry in _datasets.Entries)
            {
                if (processors.TryGetValue(entry, out var processor) && processor is AmlDatasetProcessor aml)
                    _seen[entry] = new Cell(entry, CodeOf(aml.Info), aml.Info.ScaleBand, aml.Coverage, aml.Drawn);
                if (_seen.TryGetValue(entry, out var cell))
                    cells.Add(cell);
            }
        }

        foreach (var removed in _seen.Keys.Where(e => !_datasets.Entries.Contains(e)).ToList())
            _seen.Remove(removed);

        // Cells the row's eye hides take no part: they mustn't hide coarser ones.
        var shown = cells.Where(c => c.Entry.IsVisible).ToList();
        foreach (var cell in cells)
            cell.Window = AmlBandWindow.Always;
        var windows = AmlScaleBandVisibility.Resolve([.. shown.Select(c => new AmlBandCell(c.Code, c.ScaleBand, c.Coverage))]);
        for (var i = 0; i < shown.Count; i++)
            shown[i].Window = windows[i];

        // Cells that left the map are no longer this service's to hide.
        foreach (var gone in _cells.Where(old => !cells.Any(c => c.Entry == old.Entry)))
            gone.Entry.IsHiddenByDisplay = false;

        _cells = cells;
        Apply();
    }

    private void Apply()
    {
        if (_disposed)
            return;

        var hidden = _state.HiddenAmlProducts;
        var follow = _state.FollowAmlScaleBands;
        foreach (var cell in _cells)
        {
            cell.Entry.IsHiddenByDisplay = hidden.Contains(cell.Code)
                || (follow && !cell.Window.ShowsAt(_scale));
        }

        var rows = ProductOrder.Select(code =>
        {
            var ofProduct = _cells.Where(c => c.Code == code).ToList();
            return new AmlProductState(
                code,
                NameOf(code),
                SwatchOf(code),
                ofProduct.Count,
                ofProduct.Count(IsShown),
                !hidden.Contains(code));
        });
        Products = [.. rows.OrderByDescending(r => r.IsLoaded)];
        IsClbDrawn = _cells.Any(c => c.Code == ClbCode && c.Drawn && IsShown(c));
        Changed?.Invoke();
    }

    private static bool IsShown(Cell cell) => cell.Entry.IsVisible && !cell.Entry.IsHiddenByDisplay;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _datasets.Entries.CollectionChanged -= OnEntriesChanged;
        _loader.DatasetLoaded -= OnDatasetChanged;
        _loader.DatasetRemoved -= OnDatasetChanged;
        _state.AmlDisplayChanged -= OnSwitchesChanged;
        _viewport.ViewportChanged -= OnViewportChanged;
        foreach (var entry in _subscribed.ToArray())
            Unsubscribe(entry);
    }

    private sealed class Cell(DatasetEntry entry, string code, int? scaleBand, IReadOnlyList<DataModel.CoverageArea> coverage, bool drawn)
    {
        public DatasetEntry Entry { get; } = entry;

        public string Code { get; } = code;

        public int? ScaleBand { get; } = scaleBand;

        public IReadOnlyList<DataModel.CoverageArea> Coverage { get; } = coverage;

        public bool Drawn { get; } = drawn;

        public AmlBandWindow Window { get; set; } = AmlBandWindow.Always;
    }
}
