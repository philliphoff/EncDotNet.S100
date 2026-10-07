using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The SECOM part of the "Add to Library" dialog (issue #804): the service's
/// objects counted per product, read anonymously through <c>GetSummary</c>,
/// with the products as the one facet. A source can be kept in sync (#807)
/// and narrowed to the current map view.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    /// <summary>The most a new source is synced by default (the sync's own cap).</summary>
    private static readonly long SecomSyncDefaultBytes = new LibrarySyncOptions().MaxBytes;

    private readonly Func<Uri, string?, CancellationToken, Task<SecomServiceDescription>>? _describeSecom;
    private readonly Func<GeoBounds?>? _currentMapView;
    private SecomServiceDescription? _secom;
    private string? _secomArea;
    private bool _secomSync;
    private bool _secomSyncEdited;

    /// <summary>
    /// True to keep a local copy of every object, downloaded and pruned on
    /// each refresh. On by default when the selection is small enough.
    /// </summary>
    public bool SecomSync
    {
        get => _secomSync;
        set
        {
            _secomSyncEdited = true;
            if (SetProperty(ref _secomSync, value))
                OnPropertyChanged(nameof(SecomSyncHint));
        }
    }

    /// <summary>True when the service is read only for the map view as it was when this was ticked.</summary>
    public bool SecomInMapView
    {
        get => _secomArea is not null;
        set => _ = SetSecomInMapViewAsync(value, CancellationToken.None);
    }

    /// <summary>Narrows the service to the current map view (or not) and re-reads what it offers there.</summary>
    internal async Task SetSecomInMapViewAsync(bool inMapView, CancellationToken cancellationToken)
    {
        if (inMapView == SecomInMapView)
            return;
        _secomArea = inMapView && _currentMapView?.Invoke() is { } view ? Wkt(view) : null;
        OnPropertyChanged(nameof(SecomInMapView));
        await LoadSecomAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <summary>True when there is a map view to narrow the service to.</summary>
    public bool CanScopeSecomToMapView => _currentMapView?.Invoke() is not null;

    /// <summary>"Downloads 9.8 MB now", or why syncing is not advised.</summary>
    public string? SecomSyncHint
    {
        get
        {
            if (_secom is null)
                return null;
            var bytes = SelectedSecomBytes;
            if (_secom.Truncated)
                return Strings.Library_SecomSyncTruncated;
            return string.Format(CultureInfo.CurrentCulture,
                bytes > SecomSyncDefaultBytes ? Strings.Library_SecomSyncTooLargeFormat : Strings.Library_SecomSyncSizeFormat,
                LibraryItemViewModel.FormatBytes(bytes));
        }
    }

    private long SelectedSecomBytes
    {
        get
        {
            var filter = CurrentSecomFilter;
            return _secom?.Products
                .Where(p => _includeAll || filter.IsUnscoped || filter.ProductSpecs.Contains(p.Value, StringComparer.OrdinalIgnoreCase))
                .Sum(p => p.TotalBytes) ?? 0;
        }
    }

    /// <summary>A WKT polygon (longitude, latitude) for <paramref name="view"/>, as SECOM's <c>geometry</c> filter takes it.</summary>
    internal static string Wkt(GeoBounds view)
    {
        static string F(double v) => Math.Round(v, 5).ToString(CultureInfo.InvariantCulture);
        var (s, w, n, e) = (Math.Max(-90, view.South), Math.Max(-180, view.West), Math.Min(90, view.North), Math.Min(180, view.East));
        return $"POLYGON(({F(w)} {F(s)},{F(e)} {F(s)},{F(e)} {F(n)},{F(w)} {F(n)},{F(w)} {F(s)}))";
    }

    /// <summary>True when adding a SECOM service.</summary>
    public bool IsSecom => _kind == AddToLibraryKind.Secom;

    /// <summary>The SECOM filter for the current product selection.</summary>
    public SecomFilter CurrentSecomFilter => new()
    {
        ProductSpecs = Products.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
    };

    /// <summary>"All 1,735 objects · 8.6 MB" (or "First 5,000 of 67,640 objects …") for the whole service.</summary>
    private string SecomEverythingSummary => _secom is null
        ? string.Empty
        : SecomCountSummary(_secom.ListedItems, _secom.Products.Sum(p => p.TotalBytes), all: true);

    private async Task LoadSecomAsync(CancellationToken cancellationToken)
    {
        if (_describeSecom is null)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _secom = await _describeSecom(CatalogUri, _secomArea, cancellationToken).ConfigureAwait(true);
            Populate(Products, _secom.Products, f => f.Value,
                f => string.Format(CultureInfo.CurrentCulture, Strings.Library_SecomFacetDetailFormat,
                    f.CellCount, LibraryItemViewModel.FormatBytes(f.TotalBytes)));
            UpdateSelection();
            OnPropertyChanged(nameof(EverythingSummary));
            OnPropertyChanged(nameof(IsLoaded));
            OnPropertyChanged(nameof(ShowsChoices));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateSecomSelection()
    {
        if (_secom is null)
            return;

        var filter = CurrentSecomFilter;
        var selected = _secom.Products.Where(p => filter.IsUnscoped || filter.ProductSpecs.Contains(p.Value, StringComparer.OrdinalIgnoreCase)).ToArray();
        SelectionSummary = SecomCountSummary(selected.Sum(p => p.CellCount), selected.Sum(p => p.TotalBytes), filter.IsUnscoped);
        var allProducts = _includeAll || filter.IsUnscoped;
        FollowSelectionInName(allProducts && _secomArea is null, () => string.Join(", ",
            new[] { allProducts ? null : DescribeSecomProducts(filter), _secomArea is null ? null : Strings.Library_SecomMapArea }.OfType<string>()));

        // Sync by default while the selection is small and complete, until the user decides.
        if (!_secomSyncEdited)
            SetProperty(ref _secomSync, !_secom.Truncated && SelectedSecomBytes <= SecomSyncDefaultBytes, nameof(SecomSync));
        OnPropertyChanged(nameof(SecomSyncHint));
    }

    private string SecomCountSummary(int count, long bytes, bool all)
    {
        var size = LibraryItemViewModel.FormatBytes(bytes);
        if (_secom is { Truncated: true, TotalItems: { } total })
            return string.Format(CultureInfo.CurrentCulture, Strings.Library_SecomSelectionTruncatedFormat, count, total, size);
        return string.Format(CultureInfo.CurrentCulture,
            all ? Strings.Library_SecomSelectionAllFormat : Strings.Library_SecomSelectionFormat, count, size);
    }

    private CollectionSource BuildSecomSource(Guid id)
    {
        var filter = (_includeAll ? SecomFilter.All : CurrentSecomFilter) with { GeometryWkt = _secomArea };
        var name = _includeAll ? FeedName : DescribeSecomProducts(CurrentSecomFilter) ?? FeedName;
        if (_secomArea is not null)
            name = string.Format(CultureInfo.CurrentCulture, Strings.Library_SecomAreaNameFormat, name ?? CatalogUri.Host);
        // A synced source is shown on the map by default (#809).
        return new SecomSource(id, name, CatalogUri, filter, _secomSync) { ShowOnMap = _secomSync };
    }

    /// <summary>The selected products, or <see langword="null"/> for all (the source is then named after the service).</summary>
    private static string? DescribeSecomProducts(SecomFilter filter) =>
        filter.IsUnscoped ? null : string.Join(", ", filter.ProductSpecs);
}
