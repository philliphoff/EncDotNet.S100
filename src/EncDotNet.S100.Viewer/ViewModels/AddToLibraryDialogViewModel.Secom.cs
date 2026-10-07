using System.Globalization;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The SECOM part of the "Add to Library" dialog (issue #804): the service's
/// objects counted per product, read anonymously through <c>GetSummary</c>,
/// with the products as the one facet.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    private readonly Func<Uri, CancellationToken, Task<SecomServiceDescription>>? _describeSecom;
    private SecomServiceDescription? _secom;

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
            _secom = await _describeSecom(CatalogUri, cancellationToken).ConfigureAwait(true);
            Populate(Products, _secom.Products, f => f.Value,
                f => string.Format(CultureInfo.CurrentCulture, Strings.Library_SecomFacetDetailFormat,
                    f.CellCount, LibraryItemViewModel.FormatBytes(f.TotalBytes)));
            UpdateSelection();
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
        FollowSelectionInName(_includeAll || filter.IsUnscoped, () => DescribeSecomProducts(filter)!);
    }

    private string SecomCountSummary(int count, long bytes, bool all)
    {
        var size = LibraryItemViewModel.FormatBytes(bytes);
        if (_secom is { Truncated: true, TotalItems: { } total })
            return string.Format(CultureInfo.CurrentCulture, Strings.Library_SecomSelectionTruncatedFormat, count, total, size);
        return string.Format(CultureInfo.CurrentCulture,
            all ? Strings.Library_SecomSelectionAllFormat : Strings.Library_SecomSelectionFormat, count, size);
    }

    private CollectionSource BuildSecomSource(Guid id) => _includeAll
        ? new SecomSource(id, FeedName, CatalogUri, SecomFilter.All)
        : new SecomSource(id, DescribeSecomProducts(CurrentSecomFilter) ?? FeedName, CatalogUri, CurrentSecomFilter);

    /// <summary>The selected products, or <see langword="null"/> for all (the source is then named after the service).</summary>
    private static string? DescribeSecomProducts(SecomFilter filter) =>
        filter.IsUnscoped ? null : string.Join(", ", filter.ProductSpecs);
}
