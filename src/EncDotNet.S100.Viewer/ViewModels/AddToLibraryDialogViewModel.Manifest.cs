using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.Manifests;
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
    private CollectionManifestDocument? _manifest;
    private IReadOnlyList<string> _manifestProblems = [];
    private EditedManifestSource? _editing;
    private IReadOnlyCollection<string>? _pendingPicks;
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
    public string ManifestTitle => _manifest?.Title ?? DefaultName(_path);

    /// <summary>The manifest's full path.</summary>
    public string ManifestPath => _path ?? string.Empty;

    /// <summary>One line per problem that makes the manifest unreadable ("line 12 · groups[5].id: …").</summary>
    public IReadOnlyList<string> ManifestProblems => _manifestProblems;

    /// <summary>True when the manifest could not be read.</summary>
    public bool HasManifestProblems => _manifestProblems.Count > 0;

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
        _includeAll = source.Filter.IsUnscoped;
        _pendingPicks = source.Filter.Groups.ToArray();
        OnPropertyChanged(string.Empty);
        RefreshCanConfirm();
    }

    private string ManifestBaseName => ManifestTitle;

    private bool CanConfirmManifest =>
        _manifest is not null && !_isLoading && !HasManifestProblems
        && (_includeAll || HasSelection)
        && (IsEditing || (_createNew ? !string.IsNullOrWhiteSpace(_newCollectionName) : _selectedCollection is not null));

    private string ManifestEverythingSummary => _allGroups.Count == 1
        ? Strings.Manifest_EverythingOne
        : string.Format(CultureInfo.CurrentCulture, Strings.Manifest_EverythingFormat, _allGroups.Count);

    /// <summary>Names of the groups that matter and are missing: the ticked ones, or all of them for Everything.</summary>
    private string[] MissingInScope => _allGroups
        .Where(o => o.IsMissing && (_includeAll || o.IsSelected))
        .Select(o => o.Label)
        .ToArray();

    private string ManifestScopeSummary
    {
        get
        {
            if (!_includeAll && !HasSelection)
                return Strings.Manifest_SummaryNothing;

            if (MissingInScope is { Length: > 0 } missing)
            {
                return string.Format(
                    CultureInfo.CurrentCulture,
                    missing.Length == 1 ? Strings.Manifest_SummaryMissingOneFormat : Strings.Manifest_SummaryMissingManyFormat,
                    JoinNames(missing));
            }

            if (_includeAll)
                return Strings.Manifest_SummaryEverything;

            var names = string.Join(", ", _allGroups.Where(o => o.IsSelected).Select(o => o.Label));
            var count = SelectedCount;
            return count == 1
                ? string.Format(CultureInfo.CurrentCulture, Strings.Manifest_SummaryOneFormat, names)
                : string.Format(CultureInfo.CurrentCulture, Strings.Manifest_SummaryManyFormat, count, names);
        }
    }

    private bool IsManifestSummaryWarning => (!_includeAll && !HasSelection) || MissingInScope.Length > 0;

    /// <summary>"IC-ENC", or "IC-ENC — Belgium and Germany" for a selection.</summary>
    private string AutoManifestName =>
        _includeAll || DescribeManifestSelection() is not { } selection
            ? ManifestBaseName
            : $"{ManifestBaseName} — {selection}";

    private void ResetManifest()
    {
        foreach (var option in _allGroups)
            option.PropertyChanged -= OnFacetChanged;
        _allGroups.Clear();
        Groups.Clear();
        _manifest = null;
        _manifestProblems = [];
        _editing = null;
        _pendingPicks = null;
    }

    private async Task ChangeManifestAsync()
    {
        if (PickManifest is null || await PickManifest().ConfigureAwait(true) is not { } path)
            return;

        _path = path;
        OnPropertyChanged(nameof(ManifestPath));
        OnPropertyChanged(nameof(SourceDescription));
        await LoadManifestAsync().ConfigureAwait(true);
    }

    private async Task LoadManifestAsync(CancellationToken cancellationToken = default)
    {
        if (_path is not { } path)
            return;

        // Keep the picks across a reload (or a different file) where the ids still exist.
        var picks = _pendingPicks ?? _allGroups.Where(o => o.IsSelected).Select(o => o.Value).ToArray();
        _pendingPicks = null;

        IsLoading = true;
        try
        {
            var (manifest, summary, problems) = await Task.Run(() => Read(path), cancellationToken).ConfigureAwait(true);
            _manifest = manifest;
            _manifestProblems = problems;

            foreach (var option in _allGroups)
                option.PropertyChanged -= OnFacetChanged;
            _allGroups.Clear();

            if (manifest is not null)
            {
                var selected = new HashSet<string>(picks, StringComparer.OrdinalIgnoreCase);
                foreach (var (group, info) in manifest.Groups.Zip(summary))
                {
                    var option = new LibraryChoice(group.Id, group.DisplayName, string.Empty)
                    {
                        PathText = group.Paths[0],
                        MorePathsText = group.Paths.Count > 1
                            ? string.Format(CultureInfo.CurrentCulture, Strings.Manifest_MorePathsFormat, group.Paths.Count - 1)
                            : null,
                        PathsTooltip = string.Join(Environment.NewLine, group.Paths),
                        IsMissing = info.MissingPathCount > 0,
                        IsSelected = selected.Contains(group.Id),
                    };
                    option.PropertyChanged += OnFacetChanged;
                    _allGroups.Add(option);
                }
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

        static (CollectionManifestDocument?, IReadOnlyList<CollectionManifestGroupSummary>, IReadOnlyList<string>) Read(string path)
        {
            try
            {
                var manifest = CollectionManifest.ReadFile(path);
                return (manifest, CollectionManifest.Summarize(manifest, Path.GetDirectoryName(Path.GetFullPath(path))!), []);
            }
            catch (CollectionManifestException ex)
            {
                return (null, [], ex.Problems.Select(Describe).ToArray());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or System.Text.Json.JsonException or NotSupportedException)
            {
                return (null, [], [ex.Message]);
            }
        }

        static string Describe(CollectionManifestProblem problem) => problem.Line is { } line
            ? string.Format(CultureInfo.CurrentCulture, Strings.Manifest_ErrorLineFormat, line, problem)
            : problem.ToString();
    }

    private void UpdateManifestSelection()
    {
        if (_manifest is null)
            return;

        SelectionSummary = string.Format(
            CultureInfo.CurrentCulture, Strings.Manifest_SelectionOfFormat, SelectedCount, _allGroups.Count);

        // In edit mode the collection keeps its name; otherwise the name follows the selection.
        if (!IsEditing)
            FollowSelectionInName(_includeAll || !HasSelection, () => DescribeManifestSelection()!);
    }

    private LocalManifestSource BuildManifestSource(Guid id)
    {
        var filter = _includeAll
            ? LocalManifestFilter.All
            : new LocalManifestFilter { Groups = _allGroups.Where(o => o.IsSelected).Select(o => o.Value).ToArray() };

        if (_editing is { Source: var existing })
            return existing with { Path = _path!, Filter = filter };

        // The source is named like the collection would be, so an added-to-existing
        // source reads "IC-ENC — Belgium"; a new collection's source takes its name.
        var name = _createNew && !string.IsNullOrWhiteSpace(_newCollectionName) ? _newCollectionName.Trim() : AutoManifestName;
        return new LocalManifestSource(id, name, _path!, filter);
    }

    /// <summary>"Belgium", "Belgium and Germany", "Belgium, Germany and 2 more"; null when nothing is ticked.</summary>
    private string? DescribeManifestSelection()
    {
        var labels = _allGroups.Where(o => o.IsSelected).Select(o => o.Label).ToArray();
        return labels.Length == 0 ? null : JoinNames(labels);
    }

    private static string JoinNames(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        2 => string.Format(CultureInfo.CurrentCulture, Strings.Manifest_TwoFormat, names[0], names[1]),
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Manifest_AndMoreFormat, names[0], names[1], names.Count - 2),
    };

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
