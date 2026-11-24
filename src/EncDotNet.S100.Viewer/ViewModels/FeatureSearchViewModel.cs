using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.ChartReference;

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
        OpenInReferenceCommand = new RelayCommand<FeatureSearchResultItem>(OpenInReference, item => item is { IsInReference: true });
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

    /// <summary>
    /// True when the query is empty/whitespace, so the panel shows the
    /// initial "type to search" placeholder rather than an empty list.
    /// </summary>
    public bool ShowEmptyPrompt => string.IsNullOrWhiteSpace(_query);

    /// <summary>
    /// True when a non-empty query returned no matches, so the panel shows
    /// the "no matching features" placeholder.
    /// </summary>
    public bool ShowNoResults => !ShowEmptyPrompt && Results.Count == 0;

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
            __cmd.SetTag("s100.viewer.product_spec", value.Hit.Processor.Spec.Name);
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

    /// <summary>
    /// The chart reference behind each hit's feature type term (symbol
    /// dictionary R9b, #1681); the app sets it. <see langword="null"/> keeps
    /// every feature type plain and hides <b>Open in reference</b>.
    /// </summary>
    public ReferenceTerms? Terms { get; set; }

    /// <summary>A hit's context menu <b>Open in reference</b>: opens its feature type's entry.</summary>
    public IRelayCommand<FeatureSearchResultItem> OpenInReferenceCommand { get; }

    /// <summary>Completes when the current results' feature types are checked against the index; for tests.</summary>
    internal Task TermsResolved { get; private set; } = Task.CompletedTask;

    /// <summary>Runs a search still waiting on its debounce at once; for tests, whose dispatcher runs no timers.</summary>
    internal void SearchNow()
    {
        _debounceTimer?.Stop();
        RefreshResults();
    }

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
            OnPropertyChanged(nameof(ShowEmptyPrompt));
            OnPropertyChanged(nameof(ShowNoResults));
            return;
        }

        __cmd.SetTag("s100.viewer.search.query_length", _query.Length);

        var (hits, total) = _search.Search(_query, DefaultResultLimit);
        foreach (var hit in hits)
            Results.Add(FeatureSearchResultItem.From(hit));

        TermsResolved = ResolveTermsAsync([.. Results]);

        __cmd.SetTag("s100.viewer.search.result_count", hits.Count);
        __cmd.SetTag("s100.viewer.search.total_matched", total);
        __cmd.SetTag("s100.viewer.search.truncated", total > hits.Count);

        Summary = total switch
        {
            // No-results is conveyed by the centered placeholder, so the
            // footer stays empty to avoid duplicating the message.
            0 => null,
            _ when total > hits.Count => string.Format(
                Strings.Search_TruncatedFooter, hits.Count, total),
            _ => string.Format(Strings.Search_ResultsFooter, total),
        };

        OnPropertyChanged(nameof(ShowEmptyPrompt));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    /// <summary>
    /// Marks the hits whose feature type the chart reference holds, so their
    /// <b>Open in reference</b> shows, and points an S-57 acronym's term at
    /// the entry its alias names; each product and type is read once.
    /// </summary>
    private async Task ResolveTermsAsync(IReadOnlyList<FeatureSearchResultItem> items)
    {
        if (Terms is not { } terms)
            return;
        foreach (var group in items.Where(i => i.TermKey is not null).GroupBy(i => (i.ProductSpec, i.Hit.FeatureType)))
        {
            var info = await terms.FeatureTypeAsync(group.Key.ProductSpec, group.Key.FeatureType).ConfigureAwait(true);
            if (info is null)
                continue;
            foreach (var item in group)
            {
                item.TermKey = info.Key;
                item.IsInReference = true;
            }
        }

        OpenInReferenceCommand.NotifyCanExecuteChanged();
    }

    private void OpenInReference(FeatureSearchResultItem? item)
    {
        if (item is not { IsInReference: true, TermKey: { } key } || Terms is not { } terms)
            return;
        using var __cmd = ViewerObservability.BeginCommand("search.open_in_reference");
        __cmd.SetTag("s100.viewer.product_spec", item.ProductSpec);
        terms.Open(key);
    }

    private void OpenResult(FeatureSearchResultItem? item)
    {
        if (item is null)
            return;

        using var __cmd = ViewerObservability.BeginCommand("search.open");
        __cmd.SetTag("s100.viewer.product_spec", item.Hit.Processor.Spec.Name);

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
internal sealed class FeatureSearchResultItem : ViewModelBase
{
    private bool _isInReference;
    private Datasets.Pipelines.Reference.ReferenceKey? _termKey;

    public required FeatureSearchHit Hit { get; init; }

    /// <summary>Feature type label preferring the FC-resolved name.</summary>
    public required string DisplayType { get; init; }

    public required string FeatureRef { get; init; }

    public required string DatasetFileName { get; init; }

    public required string ProductSpec { get; init; }

    /// <summary>
    /// The Chart reference entry of the hit's feature type (symbol dictionary
    /// R9b, #1681): its name is a term. An S-57 acronym's moves to the entry
    /// its alias names once the index is read. <see langword="null"/> for a
    /// hit without a type, whose name is plain text.
    /// </summary>
    public Datasets.Pipelines.Reference.ReferenceKey? TermKey
    {
        get => _termKey;
        set => SetProperty(ref _termKey, value);
    }

    /// <summary>
    /// Whether the chart reference holds <see cref="TermKey"/>, so the hit's
    /// context menu offers <b>Open in reference</b>.
    /// </summary>
    public bool IsInReference
    {
        get => _isInReference;
        set => SetProperty(ref _isInReference, value);
    }

    /// <summary>The tooltip of <b>Open in reference</b>: "Open Lateral beacon in the Reference tab".</summary>
    public string OpenInReferenceTip => string.Format(
        System.Globalization.CultureInfo.CurrentCulture, Strings.Tooltip_SearchOpenInReferenceFormat, DisplayType);

    public static FeatureSearchResultItem From(FeatureSearchHit hit)
        => new()
        {
            Hit = hit,
            DisplayType = hit.FeatureTypeName ?? hit.FeatureType,
            FeatureRef = hit.FeatureRef,
            DatasetFileName = hit.DatasetFileName,
            ProductSpec = hit.ProductSpec,
            TermKey = ReferenceTerms.FeatureKeyOf(hit.ProductSpec, hit.FeatureType),
        };
}
