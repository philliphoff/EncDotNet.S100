using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The collection-manifest source being edited by "Choose groups…".</summary>
/// <param name="CollectionId">The collection the source belongs to.</param>
/// <param name="Source">The source as it is now.</param>
internal sealed record EditedManifestSource(Guid CollectionId, LocalManifestSource Source);

/// <summary>
/// The collection-manifest part of the "Add to Library" dialog: reads a
/// <c>*.s100collection.json</c>, offers its groups as the one facet, and
/// shows every problem when the file cannot be read. In edit mode ("Choose
/// groups…") it changes an existing source's selection instead of adding one.
/// </summary>
internal sealed partial class AddToLibraryDialogViewModel
{
    private readonly List<LibraryChoice> _allGroups = [];
    private EditedManifestSource? _editing;
    private ICommand? _changeManifestCommand;
    private ICommand? _reloadManifestCommand;
    private ICommand? _openManifestCommand;

    /// <summary>A collection manifest's groups matching the filter text.</summary>
    public ObservableCollection<LibraryChoice> Groups { get; } = [];

    /// <summary>True when adding (or editing) a collection manifest.</summary>
    public bool IsManifest => _kind == LibrarySourceKind.LocalManifest;

    /// <summary>True in "Choose groups…" mode: the source exists, and Save changes its selection.</summary>
    public bool IsEditing => _editing is not null;

    /// <summary>True when the target cards are shown (adding, not editing).</summary>
    public bool ShowsTarget => !IsEditing;

    /// <summary>The primary button's text.</summary>
    public string PrimaryButtonText => IsEditing ? Strings.Button_Save : Strings.Button_AddToLibrary;

    /// <summary>Picks another manifest file; set by the host (a file picker).</summary>
    public Func<Task<string?>>? PickManifest { get; set; }

    /// <summary>Opens the manifest with the system's handler; set by the host.</summary>
    public Action<string>? OpenManifest { get; set; }

    /// <summary>Picks another manifest and reads it into this dialog, keeping picks whose ids still exist.</summary>
    public ICommand ChangeManifestCommand => _changeManifestCommand ??= new AsyncRelayCommand(ChangeManifestAsync);

    /// <summary>Reads the manifest again, keeping picks whose ids still exist.</summary>
    public ICommand ReloadManifestCommand => _reloadManifestCommand ??= new AsyncRelayCommand(() => LoadManifestAsync());

    /// <summary>Opens the manifest in the system's editor.</summary>
    public ICommand OpenManifestCommand => _openManifestCommand ??= new RelayCommand(
        () => { if (_path is { } path) OpenManifest?.Invoke(path); });

    /// <summary>The manifest's title, or its file name without the suffix.</summary>
    public string ManifestTitle => ManifestScope?.Title ?? DefaultName(_path);

    private CollectionManifestScope? ManifestScope => _scope as CollectionManifestScope;

    /// <summary>The manifest's full path.</summary>
    public string ManifestPath => _path ?? string.Empty;

    /// <summary>One line per problem that makes the manifest unreadable ("line 12 · groups[5].id: …").</summary>
    public IReadOnlyList<string> ManifestProblems => ManifestScope?.Problems ?? [];

    /// <summary>True when the manifest could not be read.</summary>
    public bool HasManifestProblems => ManifestProblems.Count > 0;

    /// <summary>True when the manifest was read and its groups are shown.</summary>
    public bool ShowsManifestGroups => IsManifest && !HasManifestProblems;

    /// <summary>The footer's hint: what to fix, or that nothing is copied.</summary>
    public string ManifestFooterHint => HasManifestProblems ? Strings.Manifest_FooterFix : Strings.Manifest_FooterNote;

    /// <summary>The hint under the existing-collection dropdown: the name the source gets.</summary>
    public string ExistingHint => string.Format(CultureInfo.CurrentCulture, Strings.Manifest_ExistingHintFormat, AutoManifestName);

    /// <summary>What the new source lists, for the "done" message ("every group in IC-ENC", "Belgium and Germany").</summary>
    public string DoneMessage
    {
        get
        {
            var lists = _includeAll
                ? string.Format(CultureInfo.CurrentCulture, Strings.Manifest_DoneEveryGroupFormat, ManifestBaseName)
                : DescribeManifestSelection() ?? string.Empty;
            var target = _createNew ? _newCollectionName : _selectedCollection?.Definition.Name ?? string.Empty;
            return string.Format(CultureInfo.CurrentCulture, Strings.Manifest_DoneFormat, target, lists);
        }
    }

    /// <summary>Prepares the dialog to change the groups of an existing manifest source.</summary>
    public void InitializeEdit(Guid collectionId, LocalManifestSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Initialize(LibrarySourceKind.LocalManifest, source.Path, collectionId);
        _editing = new EditedManifestSource(collectionId, source);
        SetIncludeAll(source.Filter.IsUnscoped);
        if (ManifestScope is { } scope)
            scope.PendingPicks = source.Filter.Groups.ToArray();
        OnPropertyChanged(string.Empty);
        RefreshCanConfirm();
    }

    private string ManifestBaseName => ManifestTitle;

    private bool CanConfirmManifest =>
        ManifestScope?.CanBuild == true && !_isLoading
        && (IsEditing || (_createNew ? !string.IsNullOrWhiteSpace(_newCollectionName) : _selectedCollection is not null));

    private bool IsManifestSummaryWarning => ManifestScope?.IsSummaryWarning == true;

    /// <summary>"IC-ENC", or "IC-ENC — Belgium and Germany" for a selection.</summary>
    private string AutoManifestName => ManifestScope?.AutoName ?? ManifestBaseName;

    private void ResetManifest()
    {
        foreach (var option in _allGroups)
            option.PropertyChanged -= OnFacetChanged;
        _allGroups.Clear();
        Groups.Clear();
        _editing = null;
    }

    private async Task ChangeManifestAsync()
    {
        if (PickManifest is null || await PickManifest().ConfigureAwait(true) is not { } path)
            return;

        _path = path;
        if (ManifestScope is { } scope)
            scope.ManifestPath = path;
        OnPropertyChanged(nameof(ManifestPath));
        OnPropertyChanged(nameof(SourceDescription));
        await LoadManifestAsync().ConfigureAwait(true);
    }

    private async Task LoadManifestAsync(CancellationToken cancellationToken = default)
    {
        if (ManifestScope is not { } scope)
            return;

        IsLoading = true;
        try
        {
            // The scope keeps the picks across a reload (or a different file) where the ids still exist.
            await scope.LoadAsync(cancellationToken).ConfigureAwait(true);

            foreach (var option in _allGroups)
                option.PropertyChanged -= OnFacetChanged;
            _allGroups.Clear();
            foreach (var option in scope.ManifestGroups)
            {
                option.PropertyChanged += OnFacetChanged;
                _allGroups.Add(option);
            }

            ShowMatchingCharts();
            UpdateSelection();
        }
        finally
        {
            IsLoading = false;
            RaiseManifestChanged();
            OnPropertyChanged(nameof(Title));
        }
    }

    private void UpdateManifestSelection()
    {
        if (ManifestScope is not { IsLoaded: true } scope)
            return;

        SelectionSummary = scope.SelectionSummary;

        // In edit mode the collection keeps its name; otherwise the name follows the selection.
        if (!IsEditing)
            FollowSelectionInName(scope.IsEverything, () => scope.DescribeSelection()!);
    }

    private LocalManifestSource BuildManifestSource(Guid id)
    {
        var scope = ManifestScope!;
        if (_editing is { Source: var existing })
            return existing with { Path = _path!, Filter = scope.CurrentFilter };

        // The source is named like the collection would be, so an added-to-existing
        // source reads "IC-ENC — Belgium"; a new collection's source takes its name.
        return (LocalManifestSource)scope.Build(id, _createNew ? _newCollectionName : null);
    }

    /// <summary>"Belgium", "Belgium and Germany", "Belgium, Germany and 2 more"; null when nothing is ticked.</summary>
    private string? DescribeManifestSelection() => ManifestScope?.DescribeSelection();

    private void RaiseManifestChanged()
    {
        OnPropertyChanged(nameof(ManifestTitle));
        OnPropertyChanged(nameof(ManifestProblems));
        OnPropertyChanged(nameof(HasManifestProblems));
        OnPropertyChanged(nameof(ShowsManifestGroups));
        OnPropertyChanged(nameof(ManifestFooterHint));
        OnPropertyChanged(nameof(ExistingHint));
        OnPropertyChanged(nameof(NameHint));
        RefreshCanConfirm();
    }
}
