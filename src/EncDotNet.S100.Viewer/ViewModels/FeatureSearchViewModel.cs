using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Diagnostics;
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

    /// <summary>
    /// Delay between the user's last keystroke and the search firing.
    /// 250ms feels responsive while avoiding a fresh search on every
    /// character (each search is synchronous and rebuilds the result
    /// list from scratch).
    /// </summary>
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(250);

    private readonly IFeatureSearchService _search;
    private readonly IPickService _pick;
    private readonly DispatcherTimer? _debounceTimer;

    public FeatureSearchViewModel(IFeatureSearchService search, IPickService pick)
    {
        ArgumentNullException.ThrowIfNull(search);
        ArgumentNullException.ThrowIfNull(pick);
        _search = search;
        _pick = pick;

        Results = new ObservableCollection<FeatureSearchResultItem>();
        OpenResultCommand = new RelayCommand<FeatureSearchResultItem>(OpenResult, item => item is not null);
        ClearQueryCommand = new RelayCommand(() => Query = string.Empty);

        // Debounce only when running inside a real Avalonia application
        // — unit tests don't pump a dispatcher, so we fall back to a
        // synchronous search on every keystroke (still cheap at v1
        // scale).
        if (Avalonia.Application.Current is not null)
        {
            _debounceTimer = new DispatcherTimer { Interval = DebounceDelay };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                RefreshResults();
            };
        }
    }

    private string _query = string.Empty;
    /// <summary>
    /// Current substring query. Setter schedules a debounced re-search
    /// (~250ms after the last keystroke) so a multi-character query
    /// only triggers one search. Clearing the query fires immediately
    /// so the result list disappears as soon as the user empties the
    /// box.
    /// </summary>
    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value ?? string.Empty))
                return;

            if (_debounceTimer is null || string.IsNullOrEmpty(_query))
            {
                _debounceTimer?.Stop();
                RefreshResults();
                return;
            }

            // Restart the timer on every keystroke; only the final
            // pause triggers RefreshResults.
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    public ObservableCollection<FeatureSearchResultItem> Results { get; }

    private FeatureSearchResultItem? _selectedResult;
    /// <summary>
    /// Currently-selected row in the results list. Setter routes to
    /// <see cref="IPickService.OpenFeature"/> so a single click opens
    /// the feature in the Object Information panel — the panel reads
    /// like a navigation tree rather than a list of buttons.
    /// </summary>
    public FeatureSearchResultItem? SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (!SetProperty(ref _selectedResult, value) || value is null)
                return;
            using var __cmd = ViewerObservability.BeginCommand("search.open");
            __cmd.SetTag("s100.viewer.product_spec", value.Hit.Processor.ProductSpec);
            var ok = _pick.OpenFeatureAt(value.Hit.Processor, value.Hit.Ordinal, value.Hit.DatasetFileName);
            if (!ok)
                __cmd.SetStatus(false, "feature not found at ordinal");
        }
    }

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
        using var __cmd = ViewerObservability.BeginCommand("search");

        // Avoid re-entering the SelectedResult setter (which would
        // re-open the previous selection); clear the field directly
        // and notify so XAML resets.
        if (_selectedResult is not null)
        {
            _selectedResult = null;
            OnPropertyChanged(nameof(SelectedResult));
        }
        Results.Clear();

        if (string.IsNullOrWhiteSpace(_query))
        {
            Summary = null;
            __cmd.SetTag("s100.viewer.search.query_length", 0);
            return;
        }

        __cmd.SetTag("s100.viewer.search.query_length", _query.Length);

        var (hits, total) = _search.Search(_query, DefaultResultLimit);
        foreach (var hit in hits)
            Results.Add(FeatureSearchResultItem.From(hit));

        __cmd.SetTag("s100.viewer.search.result_count", hits.Count);
        __cmd.SetTag("s100.viewer.search.total_matched", total);
        __cmd.SetTag("s100.viewer.search.truncated", total > hits.Count);

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

        using var __cmd = ViewerObservability.BeginCommand("search.open");
        __cmd.SetTag("s100.viewer.product_spec", item.Hit.Processor.ProductSpec);

        var ok = _pick.OpenFeatureAt(item.Hit.Processor, item.Hit.Ordinal, item.Hit.DatasetFileName);
        if (!ok)
            __cmd.SetStatus(false, "feature not found at ordinal");
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
