using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using EncDotNet.S100.Datasets.Aml;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The display panel's Additional Military Layers section (AML handoff frame
/// B4a, decisions 23, 24 and 26): a switch per product, Follow AML scale
/// bands, the no-checks note and the Legend flyout. Shown only while an AML
/// cell is loaded.
/// </summary>
internal sealed class AmlDisplaySectionViewModel : ViewModelBase, IDisposable
{
    private readonly AmlDisplayService _service;
    private readonly AmlStyleCatalog _catalog;
    private IReadOnlyList<AmlLegendGroupViewModel>? _legend;

    /// <summary>Creates the section over <paramref name="service"/>.</summary>
    /// <param name="service">Applies the switches to the map.</param>
    /// <param name="catalog">The AML styles the legend is built from.</param>
    public AmlDisplaySectionViewModel(AmlDisplayService service, AmlStyleCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(catalog);
        _service = service;
        _catalog = catalog;
        _service.Changed += Refresh;
        Refresh();
    }

    /// <summary>The service that applies the switches to the map.</summary>
    public AmlDisplayService Service => _service;

    /// <summary>Whether the section shows: an AML cell is loaded.</summary>
    public bool IsShown => _service.HasAml;

    /// <summary>A row per AML product, loaded ones first.</summary>
    public ObservableCollection<AmlProductRowViewModel> Products { get; } = [];

    /// <summary>The Follow AML scale bands switch (on by default).</summary>
    public bool FollowScaleBands
    {
        get => _service.FollowScaleBands;
        set => _service.SetFollowScaleBands(value);
    }

    /// <summary>
    /// Whether the map's corner chip "AML · CLB not used for depth checks"
    /// shows: while a drawn CLB cell is shown (decision 23).
    /// </summary>
    public bool IsClbChipShown => _service.IsClbDrawn;

    /// <summary>The corner chip's text.</summary>
    public string ClbChipText => Strings.AmlDisplay_ClbChip;

    /// <summary>The fixed note (decision 23).</summary>
    public string NoChecksNote => Strings.Aml_NoDepthChecks;

    /// <summary>
    /// The Legend flyout: one group per colour family, an entry per class the
    /// AML styles draw. Built from the styles (<see cref="AmlStyleCatalog.BuildLegend(PaletteType)"/>)
    /// on first use, so every class a family module adds is listed.
    /// </summary>
    public IReadOnlyList<AmlLegendGroupViewModel> Legend => _legend ??=
        [.. _catalog.BuildLegend(PaletteType.Day).Select(g => new AmlLegendGroupViewModel(
            g.Family.Name,
            [.. g.Entries.Select(e => new LayerStackLegendItemViewModel(e))]))];

    private void Refresh()
    {
        var products = _service.Products;
        if (Products.Count == products.Count && Products.Select(p => p.Code).SequenceEqual(products.Select(p => p.Code)))
        {
            for (var i = 0; i < products.Count; i++)
                Products[i].Update(products[i]);
        }
        else
        {
            Products.Clear();
            foreach (var product in products)
                Products.Add(new AmlProductRowViewModel(_service, product));
        }

        OnPropertyChanged(nameof(IsShown));
        OnPropertyChanged(nameof(FollowScaleBands));
        OnPropertyChanged(nameof(IsClbChipShown));
    }

    /// <inheritdoc />
    public void Dispose() => _service.Changed -= Refresh;
}

/// <summary>One product row of the Additional Military Layers section: switch, swatch, code, name and cell count.</summary>
internal sealed class AmlProductRowViewModel : ViewModelBase
{
    private readonly AmlDisplayService _service;
    private AmlProductState _state;

    /// <summary>Creates the row.</summary>
    /// <param name="service">Applies the switch.</param>
    /// <param name="state">The product's state.</param>
    public AmlProductRowViewModel(AmlDisplayService service, AmlProductState state)
    {
        _service = service;
        _state = state;
        Swatch = Color.TryParse(state.Swatch, out var color) ? new ImmutableSolidColorBrush(color) : Brushes.Gray;
    }

    /// <summary>The product code, such as <c>CLB</c>.</summary>
    public string Code => _state.Code;

    /// <summary>The product's full name.</summary>
    public string Name => _state.Name;

    /// <summary>The product's colour family swatch.</summary>
    public IBrush Swatch { get; }

    /// <summary>Whether a cell of the product is loaded; a product that isn't shows "Not loaded" and no switch.</summary>
    public bool IsLoaded => _state.IsLoaded;

    /// <summary>"1 cell", "2 cells", or "Not loaded".</summary>
    public string CountText => !_state.IsLoaded
        ? Strings.AmlDisplay_NotLoaded
        : string.Format(CultureInfo.CurrentCulture,
            _state.CellCount == 1 ? Strings.AmlDisplay_CellCountOne : Strings.AmlDisplay_CellCountFormat,
            _state.CellCount);

    /// <summary>The tooltip: what the switch does and how many cells the map shows now.</summary>
    public string ToolTip => string.Format(CultureInfo.CurrentCulture, Strings.AmlDisplay_ProductToolTipFormat,
        _state.Code, _state.Name, _state.ShownCount, _state.CellCount);

    /// <summary>The product's switch.</summary>
    public bool IsVisible
    {
        get => _state.IsVisible;
        set
        {
            if (value != _state.IsVisible)
                _service.SetProductVisible(_state.Code, value);
        }
    }

    /// <summary>Takes the product's new state.</summary>
    /// <param name="state">The state.</param>
    public void Update(AmlProductState state)
    {
        if (state == _state)
            return;
        _state = state;
        OnPropertyChanged(nameof(IsLoaded));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ToolTip));
        OnPropertyChanged(nameof(IsVisible));
    }
}

/// <summary>One colour family of the AML legend flyout.</summary>
/// <param name="Name">The family's name, such as "Air".</param>
/// <param name="Entries">Its entries, one per class.</param>
internal sealed record AmlLegendGroupViewModel(string Name, IReadOnlyList<LayerStackLegendItemViewModel> Entries);
