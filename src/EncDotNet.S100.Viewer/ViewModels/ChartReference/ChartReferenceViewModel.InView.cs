using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;
using EncDotNet.S100.Viewer.ViewModels.Activities;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// In view, hover to find and the Legend tab's Show all link (#1638, symbol
/// dictionary §4.1, §4.5, R5).
/// </summary>
/// <remarks>
/// <para>
/// <b>In view</b> limits the list to what the selected product draws in the
/// map view at the view scale, read through <see cref="IChartReferenceMap"/>
/// from the display lists the Legend tab's catalogue key reads. A symbol row
/// is in view when the symbol is drawn (a family when one of its members
/// is), a line style or area fill row when an instruction draws it (#1653),
/// and a feature row when a feature of its class is drawn. Items match by
/// kind as well as id, so a drawn point symbol never puts the line style of
/// the same name in view (<c>CTNARE51</c>). Items no rule names never are
/// (D3). The list follows the map on pan end, never under the pointer.
/// </para>
/// <para>
/// In view is on when the Legend tab's <b>Show all in Chart reference</b>
/// opens the activity (<see cref="ShowAllInView"/>), and off when the user
/// opens it from the Help menu or the activity bar (<see cref="OnOpenedByUser"/>).
/// </para>
/// </remarks>
internal sealed partial class ChartReferenceViewModel : IActivityTabContentSignal, IActivityTabOpenedByUser
{
    private IChartReferenceMap? _map;
    private bool _inView;
    private (string Spec, ChartReferenceDrawn Drawn)? _drawn;

    /// <summary>
    /// Raised by <see cref="ShowAllInView"/>: the shell opens the left dock on
    /// the activity.
    /// </summary>
    public event EventHandler? ContentBecameAvailable;

    /// <summary>Whether the list shows only what is drawn in the map view.</summary>
    public bool InView
    {
        get => _inView;
        set
        {
            if (!SetInView(value))
                return;
            Rebuild();
        }
    }

    /// <summary>Whether there is a map to limit the list to, so the In view toggle shows.</summary>
    public bool CanShowInView => _map is not null;

    /// <summary>
    /// Connects the map: what is drawn in view, and hover to find. The app
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
        OnPropertyChanged(nameof(CanShowInView));
    }

    /// <summary>
    /// The Legend tab's <b>Show all in Chart reference</b> (legends D7): the
    /// activity opens at <paramref name="spec"/>'s symbols, with In view on.
    /// </summary>
    /// <param name="spec">The portrayal spec name of the catalogue key's product (<c>S-101</c>).</param>
    public void ShowAllInView(string spec)
    {
        ArgumentException.ThrowIfNullOrEmpty(spec);
        SetInView(true);
        SetQuietly(string.Empty, ChartReferenceMode.Symbols);
        if (Products.FirstOrDefault(p => string.Equals(p.Spec.Name, spec, StringComparison.OrdinalIgnoreCase)) is { } product)
            SelectedProduct = product;
        ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void OnOpenedByUser() => InView = false;

    /// <summary>
    /// The pointer rests on, or keyboard focus moves to, a row: the features
    /// it describes that are drawn in view are ringed (legends D19).
    /// </summary>
    /// <param name="row">The row.</param>
    public void HoverRow(ChartReferenceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (_map is not null && TargetOf(row) is { } target)
            _map.Hover(target);
    }

    /// <summary>The pointer or focus left a row: its rings clear.</summary>
    /// <param name="row">The row.</param>
    public void LeaveRow(ChartReferenceRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);
        _map?.Leave(TargetOf(row));
    }

    /// <summary>
    /// What hovering <paramref name="row"/> rings: a symbol's features, a
    /// family's members', the curves and surfaces a line style or area fill
    /// is drawn on (#1653), or a feature type's by its code and S-57 acronym.
    /// </summary>
    /// <param name="row">The row.</param>
    internal ChartReferenceTarget? TargetOf(ChartReferenceRowViewModel row)
    {
        if (_selectedProduct?.Spec.Name is not { } spec)
            return null;
        return row.Entry switch
        {
            FeatureTypeEntry f => new ChartReferenceTarget(spec, f.Key.ToString(), [], f.Alias is { } alias ? [f.Code, alias] : [f.Code]),
            PortrayalItemEntry { IsFamily: true } family => new ChartReferenceTarget(spec, family.Key.ToString(), family.Members, []),
            PortrayalItemEntry { Key.Kind: ReferenceKind.Symbol } symbol => new ChartReferenceTarget(spec, symbol.Key.ToString(), [symbol.Code], []),
            PortrayalItemEntry { Key.Kind: ReferenceKind.LineStyle } line => new ChartReferenceTarget(spec, line.Key.ToString(), [], []) { LineStyles = [line.Code] },
            PortrayalItemEntry { Key.Kind: ReferenceKind.AreaFill } fill => new ChartReferenceTarget(spec, fill.Key.ToString(), [], []) { AreaFills = [fill.Code] },
            _ => null,
        };
    }

    // Whether the In view filter keeps a feature type.
    private bool InViewAllows(FeatureTypeEntry feature)
    {
        if (!_inView)
            return true;
        var drawn = Drawn().FeatureTypes;
        return drawn.Contains(feature.Code) || feature.Alias is { } alias && drawn.Contains(alias);
    }

    // Whether the In view filter keeps an item. Unreached items are never
    // drawn (D3); a family is drawn when one of its members is. An item is
    // matched by its kind as well as its id (#1653).
    private bool InViewAllows(PortrayalItemEntry item)
    {
        if (!_inView)
            return true;
        if (item.IsUnreached)
            return false;
        var drawn = Drawn();
        return item.IsFamily
            ? item.Members.Any(drawn.Symbols.Contains)
            : drawn.Contains(item.Key.Kind, item.Code);
    }

    private bool InViewAllows(ReferenceEntry entry) => entry switch
    {
        FeatureTypeEntry f => InViewAllows(f),
        PortrayalItemEntry i => InViewAllows(i),
        _ => !_inView,
    };

    private ChartReferenceDrawn Drawn()
    {
        if (_map is null || _selectedProduct?.Spec.Name is not { } spec)
            return ChartReferenceDrawn.None;
        if (_drawn is { } held && string.Equals(held.Spec, spec, StringComparison.OrdinalIgnoreCase))
            return held.Drawn;
        var drawn = _map.DrawnInView(spec);
        _drawn = (spec, drawn);
        return drawn;
    }

    private bool SetInView(bool value)
    {
        if (!SetProperty(ref _inView, value, nameof(InView)))
            return false;
        _drawn = null;
        OnPropertyChanged(nameof(NoMatchesDescription));
        return true;
    }

    // The map settled or was redrawn: the list follows when what is drawn
    // changed, keeping the selected row if it's still listed.
    private void OnMapChanged(object? sender, EventArgs e) => _post(() =>
    {
        if (_disposed)
            return;
        var before = _drawn;
        _drawn = null;
        if (!_inView || _index is null)
            return;
        var now = Drawn();
        if (before is { } b && string.Equals(b.Spec, _drawn?.Spec, StringComparison.OrdinalIgnoreCase)
            && b.Drawn.SetEquals(now))
        {
            return;
        }
        // Following the map keeps the list where the user is.
        Rebuild(restart: false);
    });
}
