using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Header row surfaced above the dataset list when an exchange set is
/// loaded. Carries catalogue-level metadata (producer, issue date,
/// dataset count, source path) plus a Close command that delegates
/// back to the registering service so it can remove every entry that
/// came from this set in one shot.
/// </summary>
/// <remarks>
/// The viewer keeps one of these per loaded exchange set in
/// <see cref="DatasetsViewModel.ExchangeSetHeaders"/>. Removing the
/// last <see cref="DatasetEntry"/> whose <c>Source</c> matches a
/// header (whether by clicking Close or by removing entries
/// individually) causes the registering service to dispose the
/// underlying <see cref="IAssetSource"/> and unregister the header.
/// <para>
/// A header can also stand for a Library source (#809): every exchange set
/// opened from that source (<see cref="GroupKey"/>) shares it, so a source
/// whose datasets each lie in their own folder — downloaded SECOM objects,
/// NOAA cells — is one row, not one per folder.
/// </para>
/// </remarks>
internal sealed partial class ExchangeSetHeader : ViewModelBase
{
    /// <summary>The asset source backing the exchange set; used to
    /// match this header against <see cref="DatasetEntry.Source"/>.</summary>
    public IAssetSource Source { get; }

    /// <summary>The folder path or .zip path the user opened.</summary>
    public string SourcePath { get; }

    /// <summary>Short, user-facing label: the Library source's name for a
    /// source header, else derived from <see cref="SourcePath"/> (the
    /// folder or archive name).</summary>
    public string DisplayName { get; }

    /// <summary>The Library source this header stands for, or <see langword="null"/> for one exchange set.</summary>
    public string? GroupKey { get; }

    /// <summary>Catalogue-declared producer organisation, or
    /// <c>null</c> if unknown (or the header holds several sets).</summary>
    public string? Producer
    {
        get => _producer;
        private set
        {
            if (SetProperty(ref _producer, value))
                OnPropertyChanged(nameof(MetadataSummary));
        }
    }

    /// <summary>Catalogue-derived issue date string (the latest
    /// <c>DatasetDiscoveryMetadata.IssueDate</c> across the set), or
    /// <c>null</c> if unknown (or the header holds several sets).</summary>
    public string? IssueDate
    {
        get => _issueDate;
        private set
        {
            if (SetProperty(ref _issueDate, value))
                OnPropertyChanged(nameof(MetadataSummary));
        }
    }

    private readonly HashSet<IAssetSource> _sources = new(ReferenceEqualityComparer.Instance);
    private string? _producer;
    private string? _issueDate;

    /// <summary>How many exchange sets (asset sources) the header holds.</summary>
    public int SetCount => _sources.Count;

    /// <summary>True when <paramref name="source"/>'s datasets belong under this header.</summary>
    public bool Contains(IAssetSource? source) => source is not null && _sources.Contains(source);

    /// <summary>
    /// Adds another exchange set of the same Library source. A header of
    /// several sets has no single producer or issue date.
    /// </summary>
    internal void AddSource(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (_sources.Add(source) && _sources.Count > 1)
        {
            Producer = null;
            IssueDate = null;
        }
    }

    /// <summary>Removes an exchange set; returns true when the header holds none any more.</summary>
    internal bool RemoveSource(IAssetSource source)
    {
        _sources.Remove(source);
        return _sources.Count == 0;
    }

    /// <summary>Total number of catalogued datasets.</summary>
    public int DatasetCount { get; }

    [ObservableProperty]
    private int _loadedCount;

    [ObservableProperty]
    private int _unsupportedCount;

    [ObservableProperty]
    private SignatureStatus _signatureStatus = SignatureStatus.Unknown;

    [ObservableProperty]
    private string? _signatureTooltip;

    /// <summary>Single-line summary combining loaded/unsupported counts,
    /// producer and issue date, joined with " · ". Bound to the
    /// trimmable secondary line in the header so any of the three
    /// pieces can fall off into the ellipsis when the pane narrows.
    /// Recomputed whenever <see cref="LoadedCount"/> or
    /// <see cref="UnsupportedCount"/> changes.</summary>
    public string MetadataSummary => BuildMetadataSummary(LoadedCount, UnsupportedCount, Producer, IssueDate);

    partial void OnLoadedCountChanged(int value) => OnPropertyChanged(nameof(MetadataSummary));
    partial void OnUnsupportedCountChanged(int value) => OnPropertyChanged(nameof(MetadataSummary));

    /// <summary>Removes every entry whose <see cref="DatasetEntry.Source"/>
    /// matches this header's <see cref="Source"/>. Wired by the
    /// registering service.</summary>
    public ICommand CloseCommand { get; }

    /// <summary>
    /// Member datasets contributed by this exchange set, in the same
    /// relative order they occupy in
    /// <see cref="DatasetsViewModel.Entries"/>. Maintained by the owning
    /// <see cref="DatasetsViewModel"/> so the Exchange sets tab can nest
    /// each source's datasets beneath it. This is a view (not a reorder
    /// surface) — render order lives in the Datasets tab.
    /// </summary>
    public ObservableCollection<DatasetEntry> Datasets { get; } = new();

    /// <summary>Number of loaded member datasets currently nested under
    /// this source (the count badge in the Exchange sets tree).</summary>
    public int MemberCount => Datasets.Count;

    /// <summary>True when at least one member dataset is visible. Drives
    /// the source row's show/hide (eye) icon. Toggling
    /// <see cref="ToggleVisibilityCommand"/> hides all members when any
    /// are visible, otherwise shows all.</summary>
    public bool IsAnyDatasetVisible => Datasets.Any(d => d.IsVisible);

    /// <summary>Shows or hides every member dataset in one shot: hides all
    /// when any are currently visible, otherwise shows all.</summary>
    public ICommand ToggleVisibilityCommand { get; }

    public ExchangeSetHeader(
        IAssetSource source,
        string sourcePath,
        string? producer,
        string? issueDate,
        int datasetCount,
        Action<ExchangeSetHeader> closeAction,
        string? displayName = null,
        string? groupKey = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(closeAction);

        Source = source;
        _sources.Add(source);
        SourcePath = sourcePath;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? DeriveDisplayName(sourcePath) : displayName;
        GroupKey = groupKey;
        _producer = producer;
        _issueDate = issueDate;
        DatasetCount = datasetCount;
        // Initialise counts to the catalogue total so the header has a
        // sensible label while datasets are still loading. The service
        // overwrites these once it knows the loaded/unsupported split.
        _loadedCount = datasetCount;
        _unsupportedCount = 0;
        CloseCommand = new RelayCommand(() => closeAction(this));
        ToggleVisibilityCommand = new RelayCommand(ToggleVisibility);

        Datasets.CollectionChanged += OnDatasetsCollectionChanged;
    }

    private void ToggleVisibility()
    {
        var hide = IsAnyDatasetVisible;
        foreach (var dataset in Datasets)
        {
            dataset.IsVisible = !hide;
        }
    }

    private void OnDatasetsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<DatasetEntry>())
            {
                item.PropertyChanged -= OnMemberPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<DatasetEntry>())
            {
                item.PropertyChanged += OnMemberPropertyChanged;
            }
        }

        OnPropertyChanged(nameof(MemberCount));
        OnPropertyChanged(nameof(IsAnyDatasetVisible));
    }

    private void OnMemberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DatasetEntry.IsVisible))
        {
            OnPropertyChanged(nameof(IsAnyDatasetVisible));
        }
    }

    private static string BuildMetadataSummary(int loadedCount, int unsupportedCount, string? producer, string? issueDate)
    {
        var parts = new System.Collections.Generic.List<string>(4)
        {
            string.Format(Strings.Pane_ExchangeSetHeader_Count, loadedCount),
        };

        if (unsupportedCount > 0)
        {
            parts.Add(string.Format(Strings.Pane_ExchangeSetHeader_Unsupported, unsupportedCount));
        }

        if (!string.IsNullOrWhiteSpace(producer))
        {
            parts.Add(string.Format(Strings.Pane_ExchangeSetHeader_Producer, producer));
        }

        if (!string.IsNullOrWhiteSpace(issueDate))
        {
            parts.Add(string.Format(Strings.Pane_ExchangeSetHeader_Issued, issueDate));
        }

        return string.Join(" · ", parts);
    }

    private static string DeriveDisplayName(string sourcePath)
    {
        var trimmed = sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? sourcePath : name;
    }
}
