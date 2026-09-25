using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Collections.Usace;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>What an <see cref="AddToLibraryDialogViewModel"/> is adding.</summary>
internal enum AddToLibraryKind
{
    /// <summary>A local folder (scanned recursively).</summary>
    Folder,

    /// <summary>A single exchange set: folder, ZIP or catalogue file.</summary>
    ExchangeSet,

    /// <summary>An S-128 catalogue file.</summary>
    S128Catalogue,

    /// <summary>A scope of the NOAA ENC product catalogue feed.</summary>
    NoaaFeed,

    /// <summary>A scope of the USACE Inland ENC product catalogue feed (issue #670).</summary>
    UsaceFeed,
}

/// <summary>A titled group of selectable facet values (one tab in the dialog).</summary>
/// <param name="Title">The group title (e.g. "States", "Rivers").</param>
/// <param name="Options">The group's values.</param>
internal sealed record FacetGroupViewModel(string Title, ObservableCollection<FacetOptionViewModel> Options);

/// <summary>
/// View model for the "Add to Library" dialog: confirms which collection a
/// new source goes into (a new one, named, or an existing one) and, for an
/// online feed, which part to include: NOAA ENC by state, Coast Guard district
/// or region; USACE Inland ENC by river. Nothing is loaded or downloaded
/// except the feed's catalogue itself.
/// </summary>
internal sealed class AddToLibraryDialogViewModel : ViewModelBase
{
    private readonly LibraryService _library;
    private readonly Func<CancellationToken, Task<NoaaEncProductCatalog>>? _loadCatalog;
    private readonly Func<CancellationToken, Task<UsaceIencProductCatalog>>? _loadUsaceCatalog;

    private AddToLibraryKind _kind;
    private string? _path;
    private bool _createNew = true;
    private string _newCollectionName = string.Empty;
    private LibraryCollection? _selectedCollection;
    private bool _isLoading;
    private string? _loadError;
    private NoaaEncProductCatalog? _catalog;
    private UsaceIencProductCatalog? _usaceCatalog;
    private string _selectionSummary = string.Empty;

    public AddToLibraryDialogViewModel(
        LibraryService library,
        Func<CancellationToken, Task<NoaaEncProductCatalog>>? loadNoaaCatalog,
        Func<CancellationToken, Task<UsaceIencProductCatalog>>? loadUsaceCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(library);
        _library = library;
        _loadCatalog = loadNoaaCatalog;
        _loadUsaceCatalog = loadUsaceCatalog;

        ConfirmCommand = new RelayCommand(Confirm, () => CanConfirm);
        CancelCommand = new RelayCommand(() => Closed?.Invoke(this, false));
        SelectNoneCommand = new RelayCommand(ClearFacetSelection);
    }

    /// <summary>Raised with <see langword="true"/> when confirmed, <see langword="false"/> when cancelled.</summary>
    public event EventHandler<bool>? Closed;

    /// <summary>The persisted collections a source can be added to.</summary>
    public IReadOnlyList<LibraryCollection> ExistingCollections { get; private set; } = [];

    /// <summary>What is being added.</summary>
    public AddToLibraryKind Kind => _kind;

    /// <summary>True when adding a NOAA feed scope.</summary>
    public bool IsNoaaFeed => _kind == AddToLibraryKind.NoaaFeed;

    /// <summary>True when adding a scope of an online feed (NOAA or USACE).</summary>
    public bool IsOnlineFeed => _kind is AddToLibraryKind.NoaaFeed or AddToLibraryKind.UsaceFeed;

    /// <summary>The facet tabs for the current feed.</summary>
    public IReadOnlyList<FacetGroupViewModel> FacetGroups => _kind switch
    {
        AddToLibraryKind.NoaaFeed =>
        [
            new(Strings.Library_NoaaStates, States),
            new(Strings.Library_NoaaDistricts, CoastGuardDistricts),
            new(Strings.Library_NoaaRegions, Regions),
        ],
        AddToLibraryKind.UsaceFeed => [new(Strings.Library_UsaceRivers, Rivers)],
        _ => [],
    };

    /// <summary>The dialog title.</summary>
    public string Title => _kind switch
    {
        AddToLibraryKind.NoaaFeed => Strings.Library_AddNoaaTitle,
        AddToLibraryKind.UsaceFeed => Strings.Library_AddUsaceTitle,
        _ => Strings.Library_AddTitle,
    };

    /// <summary>The path being added, or the feed URL.</summary>
    public string SourceDescription => _kind switch
    {
        AddToLibraryKind.NoaaFeed => NoaaEncFeedSource.DefaultCatalogUri.AbsoluteUri,
        AddToLibraryKind.UsaceFeed => UsaceIencFeedSource.RiversCatalogUri.AbsoluteUri,
        _ => _path ?? string.Empty,
    };

    /// <summary>True to create a new collection; false to add to <see cref="SelectedCollection"/>.</summary>
    public bool CreateNew
    {
        get => _createNew;
        set
        {
            if (SetProperty(ref _createNew, value))
            {
                OnPropertyChanged(nameof(AddToExisting));
                RefreshCanConfirm();
            }
        }
    }

    /// <summary>The inverse of <see cref="CreateNew"/>, for radio-button binding.</summary>
    public bool AddToExisting
    {
        get => !_createNew;
        set => CreateNew = !value;
    }

    /// <summary>True when there is at least one existing collection to add to.</summary>
    public bool HasExistingCollections => ExistingCollections.Count > 0;

    /// <summary>The name of the new collection.</summary>
    public string NewCollectionName
    {
        get => _newCollectionName;
        set
        {
            if (SetProperty(ref _newCollectionName, value ?? string.Empty))
                RefreshCanConfirm();
        }
    }

    /// <summary>The existing collection to add to.</summary>
    public LibraryCollection? SelectedCollection
    {
        get => _selectedCollection;
        set
        {
            if (SetProperty(ref _selectedCollection, value))
                RefreshCanConfirm();
        }
    }

    /// <summary>True while the NOAA catalogue is being fetched.</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
                RefreshCanConfirm();
        }
    }

    /// <summary>Why the NOAA catalogue could not be fetched, if it failed.</summary>
    public string? LoadError
    {
        get => _loadError;
        private set
        {
            if (SetProperty(ref _loadError, value))
                OnPropertyChanged(nameof(HasLoadError));
        }
    }

    /// <summary>True when <see cref="LoadError"/> is set.</summary>
    public bool HasLoadError => _loadError is not null;

    /// <summary>States present in the NOAA catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> States { get; } = [];

    /// <summary>Coast Guard districts present in the NOAA catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> CoastGuardDistricts { get; } = [];

    /// <summary>Regions present in the NOAA catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> Regions { get; } = [];

    /// <summary>Rivers present in the USACE catalogue.</summary>
    public ObservableCollection<FacetOptionViewModel> Rivers { get; } = [];

    /// <summary>"N cells · X MB" for the current NOAA selection.</summary>
    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    public ICommand ConfirmCommand { get; }

    public ICommand CancelCommand { get; }

    /// <summary>Clears every NOAA facet selection (meaning "all cells").</summary>
    public ICommand SelectNoneCommand { get; }

    /// <summary>
    /// Prepares the dialog for adding <paramref name="path"/> (or, for
    /// <see cref="AddToLibraryKind.NoaaFeed"/>, the feed), preselecting
    /// <paramref name="targetCollectionId"/> when it names a collection.
    /// </summary>
    public void Initialize(AddToLibraryKind kind, string? path, Guid? targetCollectionId)
    {
        _kind = kind;
        _path = path;
        ExistingCollections = _library.Collections.Where(c => !c.IsSession).ToArray();
        _selectedCollection = targetCollectionId is { } id
            ? ExistingCollections.FirstOrDefault(c => c.Id == id)
            : ExistingCollections.FirstOrDefault();
        _createNew = targetCollectionId is null || _selectedCollection is null;
        _newCollectionName = FeedName ?? DefaultName(path);

        OnPropertyChanged(string.Empty);
        RefreshCanConfirm();
    }

    /// <summary>Fetches the feed's catalogue and populates the facet lists.</summary>
    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_kind == AddToLibraryKind.UsaceFeed)
        {
            await LoadUsaceCatalogAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (_loadCatalog is null || _kind != AddToLibraryKind.NoaaFeed)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _catalog = await _loadCatalog(cancellationToken).ConfigureAwait(true);
            var facets = NoaaEncFacets.Compute(_catalog);

            Populate(States, facets.States.OrderBy(f => UsStateNames.SortKey(f.Value), StringComparer.CurrentCulture),
                f => UsStateNames.Describe(f.Value));
            Populate(CoastGuardDistricts, facets.CoastGuardDistricts.OrderBy(f => int.Parse(f.Value, CultureInfo.InvariantCulture)),
                f => string.Format(CultureInfo.CurrentCulture, Strings.Library_DistrictFormat, f.Value));
            Populate(Regions, facets.Regions.OrderBy(f => int.Parse(f.Value, CultureInfo.InvariantCulture)),
                f => string.Format(CultureInfo.CurrentCulture, Strings.Library_RegionFormat, f.Value));
            UpdateSelection();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadUsaceCatalogAsync(CancellationToken cancellationToken)
    {
        if (_loadUsaceCatalog is null)
            return;

        IsLoading = true;
        LoadError = null;
        try
        {
            _usaceCatalog = await _loadUsaceCatalog(cancellationToken).ConfigureAwait(true);
            Populate(Rivers, EncDotNet.S100.Collections.Indexing.UsaceIencFeedIndexer.Rivers(_usaceCatalog), f => f.Value);
            UpdateSelection();
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or System.Xml.XmlException or TaskCanceledException)
        {
            LoadError = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>The USACE filter for the current river selection.</summary>
    public UsaceIencFilter CurrentUsaceFilter => new()
    {
        Rivers = Rivers.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
    };

    /// <summary>The default collection name for an online feed, or <see langword="null"/> for local sources.</summary>
    private string? FeedName => _kind switch
    {
        AddToLibraryKind.NoaaFeed => Strings.Library_NoaaFeed,
        AddToLibraryKind.UsaceFeed => Strings.Library_UsaceFeed,
        _ => null,
    };

    /// <summary>The NOAA filter for the current facet selection.</summary>
    public NoaaEncFilter CurrentFilter => new()
    {
        States = States.Where(o => o.IsSelected).Select(o => o.Value).ToArray(),
        CoastGuardDistricts = CoastGuardDistricts.Where(o => o.IsSelected)
            .Select(o => int.Parse(o.Value, CultureInfo.InvariantCulture)).ToArray(),
        Regions = Regions.Where(o => o.IsSelected)
            .Select(o => int.Parse(o.Value, CultureInfo.InvariantCulture)).ToArray(),
    };

    private bool CanConfirm =>
        (_createNew ? !string.IsNullOrWhiteSpace(_newCollectionName) : _selectedCollection is not null)
        && _kind switch
        {
            AddToLibraryKind.NoaaFeed => _catalog is not null && !_isLoading,
            AddToLibraryKind.UsaceFeed => _usaceCatalog is not null && !_isLoading,
            _ => !string.IsNullOrEmpty(_path),
        };

    private void RefreshCanConfirm() => ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();

    private void Confirm()
    {
        if (!CanConfirm)
            return;

        var source = BuildSource();
        if (_createNew)
            _library.AddCollection(_newCollectionName, [source]);
        else
            _library.AddSources(_selectedCollection!.Id, [source]);

        Closed?.Invoke(this, true);
    }

    /// <summary>Builds the source the dialog describes.</summary>
    internal CollectionSource BuildSource()
    {
        var id = Guid.NewGuid();
        return _kind switch
        {
            AddToLibraryKind.Folder => new LocalFolderSource(id, null, _path!),
            AddToLibraryKind.ExchangeSet => new ExchangeSetSource(id, null, _path!),
            AddToLibraryKind.S128Catalogue => new S128CatalogueSource(id, null, _path!),
            AddToLibraryKind.UsaceFeed => new UsaceIencFeedSource(
                id, DescribeUsaceFilter(CurrentUsaceFilter), UsaceIencFeedSource.RiversCatalogUri, CurrentUsaceFilter),
            _ => new NoaaEncFeedSource(id, DescribeFilter(CurrentFilter), NoaaEncFeedSource.DefaultCatalogUri, CurrentFilter),
        };
    }

    private void Populate(
        ObservableCollection<FacetOptionViewModel> target,
        IEnumerable<CatalogFacetValue> values,
        Func<CatalogFacetValue, string> label)
    {
        foreach (var option in target)
            option.PropertyChanged -= OnFacetChanged;
        target.Clear();

        foreach (var value in values)
        {
            var option = new FacetOptionViewModel(value, label(value));
            option.PropertyChanged += OnFacetChanged;
            target.Add(option);
        }
    }

    private void OnFacetChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FacetOptionViewModel.IsSelected))
            UpdateSelection();
    }

    private void ClearFacetSelection()
    {
        foreach (var option in States.Concat(CoastGuardDistricts).Concat(Regions).Concat(Rivers))
            option.IsSelected = false;
    }

    private void UpdateSelection()
    {
        if (_kind == AddToLibraryKind.UsaceFeed)
        {
            UpdateUsaceSelection();
            return;
        }

        if (_catalog is null)
            return;

        var filter = CurrentFilter;
        var (count, bytes) = NoaaEncFacets.Summarize(_catalog, filter);
        SelectionSummary = string.Format(
            CultureInfo.CurrentCulture,
            filter.IsUnscoped ? Strings.Library_NoaaSelectionAllFormat : Strings.Library_NoaaSelectionFormat,
            count,
            LibraryItemViewModel.FormatBytes(bytes));

        // Follow the selection in the suggested name until the user edits it.
        if (_createNew && (string.IsNullOrWhiteSpace(_newCollectionName) || _newCollectionName.StartsWith(Strings.Library_NoaaFeed, StringComparison.Ordinal)))
            NewCollectionName = filter.IsUnscoped ? Strings.Library_NoaaFeed : $"{Strings.Library_NoaaFeed} — {DescribeFilter(filter)}";
    }

    private void UpdateUsaceSelection()
    {
        if (_usaceCatalog is null)
            return;

        var filter = CurrentUsaceFilter;
        var selected = _usaceCatalog.Cells.Where(filter.Matches).ToArray();
        SelectionSummary = string.Format(
            CultureInfo.CurrentCulture,
            filter.IsUnscoped ? Strings.Library_NoaaSelectionAllFormat : Strings.Library_NoaaSelectionFormat,
            selected.Length,
            LibraryItemViewModel.FormatBytes(selected.Sum(c => c.ZipSize ?? 0)));

        if (_createNew && (string.IsNullOrWhiteSpace(_newCollectionName) || _newCollectionName.StartsWith(Strings.Library_UsaceFeed, StringComparison.Ordinal)))
            NewCollectionName = filter.IsUnscoped ? Strings.Library_UsaceFeed : $"{Strings.Library_UsaceFeed} — {DescribeUsaceFilter(filter)}";
    }

    private static string DescribeUsaceFilter(UsaceIencFilter filter)
    {
        if (filter.IsUnscoped)
            return Strings.Library_UsaceAll;

        var rivers = filter.Rivers.ToArray();
        return rivers.Length <= 3
            ? string.Join(", ", rivers)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", rivers.Take(2)), rivers.Length - 2);
    }

    private string DescribeFilter(NoaaEncFilter filter)
    {
        if (filter.IsUnscoped)
            return Strings.Library_NoaaAll;

        var parts = States.Where(o => o.IsSelected).Select(o => UsStateNames.SortKey(o.Value))
            .Concat(CoastGuardDistricts.Where(o => o.IsSelected).Select(o => o.Label))
            .Concat(Regions.Where(o => o.IsSelected).Select(o => o.Label))
            .ToArray();
        return parts.Length <= 3
            ? string.Join(", ", parts)
            : string.Format(CultureInfo.CurrentCulture, Strings.Library_AndMoreFormat, string.Join(", ", parts.Take(2)), parts.Length - 2);
    }

    private static string DefaultName(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileNameWithoutExtension(trimmed);
        return string.IsNullOrEmpty(name) ? trimmed : name;
    }
}

/// <summary>One selectable facet value (a NOAA state, district or region, or a USACE river).</summary>
internal sealed class FacetOptionViewModel : ViewModelBase
{
    private bool _isSelected;

    public FacetOptionViewModel(CatalogFacetValue value, string label)
    {
        Value = value.Value;
        Label = label;
        Detail = string.Format(CultureInfo.CurrentCulture, Strings.Library_FacetDetailFormat,
            value.CellCount, LibraryItemViewModel.FormatBytes(value.TotalBytes));
    }

    /// <summary>The facet value (state code, district/region number, or river name).</summary>
    public string Value { get; }

    /// <summary>The display label.</summary>
    public string Label { get; }

    /// <summary>"N cells · X MB".</summary>
    public string Detail { get; }

    /// <summary>Whether the value is included.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
