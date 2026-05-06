using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// View-model backing the search activity panel. Drives the
/// <see cref="IFeatureSearchService"/> and surfaces its hits as a flat
/// observable list. Selecting / double-clicking a result asks the pick
/// service to display the feature in the Object Information panel.
/// </summary>
internal sealed class FeatureSearchViewModel : ViewModelBase
{
    private const int DefaultResultLimit = 200;

    private readonly IFeatureSearchService _search;
    private readonly IPickService _pick;

    public FeatureSearchViewModel(IFeatureSearchService search, IPickService pick)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(pick);
        _search = search;
        _pick = pick;

        Results = new ObservableCollection<FeatureSearchResultItem>();
        OpenResultCommand = new RelayCommand<FeatureSearchResultItem>(OpenResult, item => item is not null);
        ClearQueryCommand = new RelayCommand(() => Query = string.Empty);
    }

    private string _query = string.Empty;
    /// <summary>
    /// Current substring query. Setter triggers a synchronous re-search;
    /// at v1 scale (~10 k features) the search is fast enough to run on
    /// the UI thread on every keystroke.
    /// </summary>
    public string Query
    {
        get => _query;
        set
        {
            if (SetProperty(ref _query, value ?? string.Empty))
                RefreshResults();
        }
    }

    public ObservableCollection<FeatureSearchResultItem> Results { get; }

    private string? _summary;
    /// <summary>
    /// Footer text describing how many results matched (and whether the
    /// list was truncated). Null when the query is empty so the panel
    /// can hide the footer.
    /// </summary>
    public string? Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public ICommand OpenResultCommand { get; }
    public ICommand ClearQueryCommand { get; }

    private void RefreshResults()
    {
        Results.Clear();

        if (string.IsNullOrWhiteSpace(_query))
        {
            Summary = null;
            return;
        }

        var (hits, total) = _search.Search(_query, DefaultResultLimit);
        foreach (var hit in hits)
            Results.Add(FeatureSearchResultItem.From(hit));

        Summary = total switch
        {
            0 => Strings.Search_NoResults,
            _ when total > hits.Count => string.Format(
                Strings.Search_TruncatedFooter, hits.Count, total),
            _ => string.Format(Strings.Search_ResultsFooter, total),
        };
    }

    private void OpenResult(FeatureSearchResultItem? item)
    {
        if (item is null)
            return;

        _pick.OpenFeature(item.Hit.Processor, item.Hit.FeatureRef, item.Hit.DatasetFileName);
    }
}

/// <summary>
/// Display projection over a <see cref="FeatureSearchHit"/> for the
/// search results list. Carries the underlying hit so the open command
/// can route back to the pick service without re-querying.
/// </summary>
internal sealed class FeatureSearchResultItem
{
    public required FeatureSearchHit Hit { get; init; }

    /// <summary>Feature type label preferring the FC-resolved name.</summary>
    public required string DisplayType { get; init; }

    public required string FeatureRef { get; init; }

    public required string DatasetFileName { get; init; }

    public required string ProductSpec { get; init; }

    public static FeatureSearchResultItem From(FeatureSearchHit hit)
        => new()
        {
            Hit = hit,
            DisplayType = hit.FeatureTypeName ?? hit.FeatureType,
            FeatureRef = hit.FeatureRef,
            DatasetFileName = hit.DatasetFileName,
            ProductSpec = hit.ProductSpec,
        };
}
