using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.Notifications;
using EncDotNet.S100.Viewer.ViewModels.Activities;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>The Catalogues activity's filter pills (design note <c>catalogue-panels.md</c> D3).</summary>
internal enum CataloguesPill
{
    /// <summary>Every product.</summary>
    All,

    /// <summary>Products with at least one dataset open.</summary>
    Loaded,

    /// <summary>Anything not built in: custom, command line, or a product SoundCharts doesn't include.</summary>
    Changed,

    /// <summary>Products with a catalogue in Error or Missing.</summary>
    Problems,
}

/// <summary>
/// The Catalogues activity (design note <c>catalogue-panels.md</c> D1–D6,
/// D11–D13, §6.1): every product the viewer knows with both its catalogues,
/// filtered by text and pills, a details pane for the selected product, Add
/// catalogue… with its confirmation sheet, Replace, Reload, Revert and Remove
/// product, each applied live with a toast that can Undo it.
/// </summary>
/// <remarks>
/// <para>
/// The activity reads and writes through <see cref="CatalogueRegistry"/>,
/// which applies every change to the managers and has the loaded datasets of
/// the product redrawn (<see cref="CatalogueRedrawCoordinator"/>).
/// </para>
/// <para>
/// Other surfaces open it at a product with <see cref="ShowProduct"/> (a
/// <b>Catalogues…</b> link, D10) or straight into Add catalogue… for a
/// product with <see cref="ChooseCatalogue"/> (a missing-catalogue toast's
/// <b>Choose catalogue…</b>). The badge (D13) counts the products under
/// Problems.
/// </para>
/// </remarks>
internal sealed class CataloguesViewModel : ViewModelBase, IActivityTabBadge, IActivityTabContentSignal, IDisposable
{
    private readonly CatalogueRegistry _registry;
    private readonly ViewerSettings _settings;
    private readonly ICatalogueDialogs _dialogs;
    private readonly INotificationService _notifications;
    private readonly ExaminerLinks _examiner;
    private readonly Func<string, int> _loadedCount;
    private readonly Action<string>? _openInChartReference;
    private readonly Action? _showLogs;
    private readonly Dictionary<string, CatalogueProductRowViewModel> _rowsBySpec = new(StringComparer.Ordinal);
    private List<CatalogueProductRowViewModel> _allRows = [];
    private string _filterText = string.Empty;
    private CataloguesPill _pill;
    private CatalogueProductRowViewModel? _selectedRow;

    /// <summary>Creates the activity.</summary>
    /// <param name="registry">The catalogues in effect.</param>
    /// <param name="settings">Remembers the selected pill.</param>
    /// <param name="dialogs">The picker, the sheet's host and the file manager.</param>
    /// <param name="notifications">Shows the live-apply toast (D6).</param>
    /// <param name="examiner">Opens a product's feature catalogue in the eXaminer (D9).</param>
    /// <param name="loadedCount">How many loaded datasets a change to a product's catalogues redraws.</param>
    /// <param name="openInChartReference">Opens Chart reference at a product (D8); null hides the item.</param>
    /// <param name="showLogs">Opens Logs filtered to the <c>Catalogues</c> area (D11); null hides the links.</param>
    public CataloguesViewModel(
        CatalogueRegistry registry,
        ViewerSettings settings,
        ICatalogueDialogs dialogs,
        INotificationService notifications,
        ExaminerLinks examiner,
        Func<string, int> loadedCount,
        Action<string>? openInChartReference = null,
        Action? showLogs = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(examiner);
        ArgumentNullException.ThrowIfNull(loadedCount);
        _registry = registry;
        _settings = settings;
        _dialogs = dialogs;
        _notifications = notifications;
        _examiner = examiner;
        _loadedCount = loadedCount;
        _openInChartReference = openInChartReference;
        _showLogs = showLogs;
        _pill = Enum.TryParse<CataloguesPill>(settings.CataloguesPill, ignoreCase: true, out var pill) ? pill : CataloguesPill.All;

        AddCommand = new AsyncRelayCommand(() => AddAsync(null));
        ReplaceFeatureCommand = new AsyncRelayCommand<CatalogueProductRowViewModel>(r => r is null ? Task.CompletedTask : AddAsync((r.Spec, CatalogueKind.Feature)));
        ReplacePortrayalCommand = new AsyncRelayCommand<CatalogueProductRowViewModel>(r => r is null ? Task.CompletedTask : AddAsync((r.Spec, CatalogueKind.Portrayal)));
        RevertCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r is not null) Revert(r.Spec); });
        RevertFeatureCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r is not null) Revert(r.Spec, CatalogueKind.Feature); });
        RevertPortrayalCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r is not null) Revert(r.Spec, CatalogueKind.Portrayal); });
        RemoveProductCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r is not null) RemoveProduct(r.Spec); });
        ReloadCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r is not null) Reload(r.Spec); });
        ReloadAllCommand = new RelayCommand(ReloadAll);
        RevealCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r?.RevealPath is { } path) _dialogs.Reveal(path); });
        OpenInChartReferenceCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r is not null) _openInChartReference?.Invoke(r.Spec); });
        OpenInExaminerCommand = new RelayCommand<CatalogueProductRowViewModel>(r => { if (r is not null) _examiner.OpenCatalogue(r.Spec); });
        ShowLogsCommand = new RelayCommand(() => _showLogs?.Invoke());
        ClearFilterCommand = new RelayCommand(() => FilterText = string.Empty);

        _registry.CataloguesChanged += OnCataloguesChanged;
        Refresh();
    }

    // ── List, filter and pills (D3) ──────────────────────────────────────

    /// <summary>The products the filter and pill show: loaded first, then by spec.</summary>
    public ObservableCollection<CatalogueProductRowViewModel> Rows { get; } = [];

    /// <summary>Every product, in list order.</summary>
    public IReadOnlyList<CatalogueProductRowViewModel> AllRows => _allRows;

    /// <summary>Filters by spec, product name or declared name.</summary>
    public string FilterText
    {
        get => _filterText;
        set
        {
            if (SetProperty(ref _filterText, value ?? string.Empty))
                ApplyFilter();
        }
    }

    /// <summary>The selected pill; remembered in the settings.</summary>
    public CataloguesPill Pill
    {
        get => _pill;
        set
        {
            if (value == CataloguesPill.Problems && ProblemsCount == 0)
                value = CataloguesPill.All;
            if (!SetProperty(ref _pill, value))
                return;
            _settings.CataloguesPill = value.ToString();
            _settings.Save();
            OnPropertyChanged(nameof(IsAllPill));
            OnPropertyChanged(nameof(IsLoadedPill));
            OnPropertyChanged(nameof(IsChangedPill));
            OnPropertyChanged(nameof(IsProblemsPill));
            ApplyFilter();
        }
    }

    /// <summary>The All pill is selected.</summary>
    public bool IsAllPill { get => _pill == CataloguesPill.All; set { if (value) Pill = CataloguesPill.All; } }

    /// <summary>The Loaded pill is selected.</summary>
    public bool IsLoadedPill { get => _pill == CataloguesPill.Loaded; set { if (value) Pill = CataloguesPill.Loaded; } }

    /// <summary>The Changed pill is selected.</summary>
    public bool IsChangedPill { get => _pill == CataloguesPill.Changed; set { if (value) Pill = CataloguesPill.Changed; } }

    /// <summary>The Problems pill is selected.</summary>
    public bool IsProblemsPill { get => _pill == CataloguesPill.Problems; set { if (value) Pill = CataloguesPill.Problems; } }

    /// <summary>Every product.</summary>
    public int AllCount => _allRows.Count;

    /// <summary>Products with a dataset open.</summary>
    public int LoadedCount => _allRows.Count(r => r.IsLoaded);

    /// <summary>Products with anything not built in.</summary>
    public int ChangedCount => _allRows.Count(r => r.IsChanged);

    /// <summary>Products with an Error or Missing catalogue.</summary>
    public int ProblemsCount => _allRows.Count(r => r.HasProblem);

    /// <summary>Whether the Problems pill shows: hidden when there are none (D3).</summary>
    public bool HasProblems => ProblemsCount > 0;

    /// <summary>The All pill's accessible name, "All, 17".</summary>
    public string AllPillName => PillName(Strings.Catalogues_Pill_All, AllCount);

    /// <summary>The Loaded pill's accessible name.</summary>
    public string LoadedPillName => PillName(Strings.Catalogues_Pill_Loaded, LoadedCount);

    /// <summary>The Changed pill's accessible name.</summary>
    public string ChangedPillName => PillName(Strings.Catalogues_Pill_Changed, ChangedCount);

    /// <summary>The Problems pill's accessible name.</summary>
    public string ProblemsPillName => PillName(Strings.Catalogues_Pill_Problems, ProblemsCount);

    /// <summary>"3 of 17" in the filter box.</summary>
    public string CountText => string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_CountFormat, Rows.Count, AllCount);

    /// <summary>Whether the filter and pill hide every product.</summary>
    public bool HasNoMatches => Rows.Count == 0;

    /// <summary>The selected product, shown in the details pane.</summary>
    public CatalogueProductRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (SetProperty(ref _selectedRow, value))
                OnPropertyChanged(nameof(HasSelection));
        }
    }

    /// <summary>Whether a product is selected, so the details pane shows.</summary>
    public bool HasSelection => _selectedRow is not null;

    /// <summary>Whether Show in Logs shows (D11).</summary>
    public bool CanShowLogs => _showLogs is not null;

    /// <summary>Whether Open in Chart reference shows (D8).</summary>
    public bool CanOpenInChartReference => _openInChartReference is not null;

    /// <summary>Show in Finder, Explorer or folder, for this platform.</summary>
    public string RevealLabel => _dialogs.RevealLabel;

    // ── Commands ─────────────────────────────────────────────────────────

    /// <summary>Add catalogue… (D5): picks a file and opens the confirmation sheet.</summary>
    public IAsyncRelayCommand AddCommand { get; }

    /// <summary>Replace feature catalogue… for a row: Add catalogue… with the product fixed.</summary>
    public IAsyncRelayCommand<CatalogueProductRowViewModel> ReplaceFeatureCommand { get; }

    /// <summary>Replace (or Add) portrayal catalogue… for a row.</summary>
    public IAsyncRelayCommand<CatalogueProductRowViewModel> ReplacePortrayalCommand { get; }

    /// <summary>Revert to built-in: drops a row's custom catalogues.</summary>
    public ICommand RevertCommand { get; }

    /// <summary>Drops a row's custom feature catalogue.</summary>
    public ICommand RevertFeatureCommand { get; }

    /// <summary>Drops a row's custom portrayal catalogue.</summary>
    public ICommand RevertPortrayalCommand { get; }

    /// <summary>Remove product (D12): drops a custom product's catalogues and its row.</summary>
    public ICommand RemoveProductCommand { get; }

    /// <summary>Reload (D11): reads a row's custom or command-line catalogues again.</summary>
    public ICommand ReloadCommand { get; }

    /// <summary>The header menu's Reload custom catalogues.</summary>
    public ICommand ReloadAllCommand { get; }

    /// <summary>Show in Finder or Explorer for a row's custom catalogue.</summary>
    public ICommand RevealCommand { get; }

    /// <summary>Open in Chart reference (D8).</summary>
    public ICommand OpenInChartReferenceCommand { get; }

    /// <summary>Open in eXaminer, or the published edition when the feature catalogue is custom (D9).</summary>
    public ICommand OpenInExaminerCommand { get; }

    /// <summary>Show in Logs: Logs filtered to the <c>Catalogues</c> area (D11).</summary>
    public ICommand ShowLogsCommand { get; }

    /// <summary>Clears the filter text.</summary>
    public ICommand ClearFilterCommand { get; }

    // ── Badge (D13) and links (D10) ─────────────────────────────────────

    /// <inheritdoc />
    public int BadgeCount => ProblemsCount;

    /// <inheritdoc />
    public string BadgeName => ProblemsCount == 1
        ? Strings.Catalogues_BadgeOne
        : string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_BadgeMany, ProblemsCount);

    /// <summary>Raised by <see cref="ShowProduct"/>: the shell opens the left dock on the activity.</summary>
    public event EventHandler? ContentBecameAvailable;

    /// <summary>
    /// Opens the activity with <paramref name="spec"/>'s row selected, for a
    /// <b>Catalogues…</b> link (D10). If the filter or pill hides the row,
    /// they are cleared.
    /// </summary>
    /// <param name="spec">The product, e.g. <c>"S-101"</c> or <c>"s101"</c>.</param>
    /// <returns>False when the viewer doesn't know the product; the activity opens anyway.</returns>
    public bool ShowProduct(string spec)
    {
        var found = Select(spec);
        ContentBecameAvailable?.Invoke(this, EventArgs.Empty);
        return found;
    }

    /// <summary>
    /// Opens the activity at <paramref name="spec"/> and starts Add catalogue…
    /// with the product and kind fixed, for a missing-catalogue toast's
    /// <b>Choose catalogue…</b> (D10).
    /// </summary>
    /// <param name="spec">The product.</param>
    /// <param name="kind">Which of its catalogues to choose.</param>
    /// <returns>A task that completes when the picker closes and the sheet, if any, opened.</returns>
    public Task ChooseCatalogue(string spec, CatalogueKind kind)
    {
        ShowProduct(spec);
        return SpecName.TryNormalize(spec, out var name) ? AddAsync((name, kind)) : Task.CompletedTask;
    }

    /// <summary>Re-reads the registry and the loaded counts; keeps the selection.</summary>
    public void Refresh()
    {
        var problemsBefore = ProblemsCount;
        var rows = new List<CatalogueProductRowViewModel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in _registry.Products)
        {
            var loaded = _loadedCount(product.Spec);
            var examiner = _examiner.SupportsSpec(product.Spec);
            if (_rowsBySpec.TryGetValue(product.Spec, out var row))
                row.Update(product, loaded, examiner);
            else
                _rowsBySpec[product.Spec] = row = new CatalogueProductRowViewModel(product, loaded, examiner) { Owner = this };
            rows.Add(row);
            seen.Add(product.Spec);
        }

        foreach (var gone in _rowsBySpec.Keys.Where(k => !seen.Contains(k)).ToList())
            _rowsBySpec.Remove(gone);

        // Loaded first, then by spec as the registry orders them (D3); OrderBy is stable.
        _allRows = [.. rows.OrderBy(r => r.IsLoaded ? 0 : 1)];

        if (_pill == CataloguesPill.Problems && ProblemsCount == 0)
        {
            _pill = CataloguesPill.All;
            OnPropertyChanged(nameof(Pill));
            OnPropertyChanged(nameof(IsAllPill));
            OnPropertyChanged(nameof(IsProblemsPill));
        }

        OnPropertyChanged(nameof(AllRows));
        OnPropertyChanged(nameof(AllCount));
        OnPropertyChanged(nameof(LoadedCount));
        OnPropertyChanged(nameof(ChangedCount));
        OnPropertyChanged(nameof(ProblemsCount));
        OnPropertyChanged(nameof(HasProblems));
        OnPropertyChanged(nameof(AllPillName));
        OnPropertyChanged(nameof(LoadedPillName));
        OnPropertyChanged(nameof(ChangedPillName));
        OnPropertyChanged(nameof(ProblemsPillName));
        if (problemsBefore != ProblemsCount || problemsBefore == 0)
        {
            OnPropertyChanged(nameof(BadgeCount));
            OnPropertyChanged(nameof(BadgeName));
        }

        ApplyFilter();
    }

    /// <summary>Re-evaluates the eXaminer item when the eXaminer settings change (issue #442).</summary>
    public void RefreshExaminerAvailability() => Refresh();

    private void ApplyFilter()
    {
        var shown = _allRows.Where(r => PillMatches(r) && r.Matches(_filterText)).ToList();
        if (!shown.SequenceEqual(Rows))
        {
            Rows.Clear();
            foreach (var row in shown)
                Rows.Add(row);
        }

        if (_selectedRow is null || !Rows.Contains(_selectedRow))
            SelectedRow = Rows.FirstOrDefault();
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    private bool PillMatches(CatalogueProductRowViewModel row) => _pill switch
    {
        CataloguesPill.Loaded => row.IsLoaded,
        CataloguesPill.Changed => row.IsChanged,
        CataloguesPill.Problems => row.HasProblem,
        _ => true,
    };

    private bool Select(string spec)
    {
        if (!SpecName.TryNormalize(spec, out var name) || !_rowsBySpec.TryGetValue(name, out var row))
            return false;
        if (!Rows.Contains(row))
        {
            _filterText = string.Empty;
            OnPropertyChanged(nameof(FilterText));
            Pill = CataloguesPill.All;
            ApplyFilter();
        }

        SelectedRow = row;
        return true;
    }

    private static string PillName(string label, int count) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_PillNameFormat, label, count);

    private void OnCataloguesChanged(object? sender, CataloguesChangedEventArgs e) =>
        UiThread.RunOrPost(Refresh);

    // ── Changes, applied live with a toast and Undo (D5, D6) ───────────

    private async Task AddAsync((string Spec, CatalogueKind Kind)? target)
    {
        if (await _dialogs.PickCatalogueAsync().ConfigureAwait(true) is not { Length: > 0 } path)
            return;
        var sheet = new AddCatalogueSheetViewModel(
            _registry,
            _loadedCount,
            target,
            _dialogs.PickCatalogueAsync,
            Use,
            s => _dialogs.Close(s));
        sheet.Load(path);
        _dialogs.Show(sheet);
    }

    /// <summary>Uses <paramref name="catalogue"/> live, selects its product and confirms it with Undo (D6).</summary>
    /// <param name="catalogue">The catalogue from the sheet.</param>
    /// <param name="keepAfterRestart">Whether to keep it after restart.</param>
    internal void Use(DetectedCatalogue catalogue, bool keepAfterRestart)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        var before = Snapshot(catalogue.Spec);
        var redrawn = _loadedCount(catalogue.Spec);
        var state = _registry.Use(catalogue, keepAfterRestart);
        Refresh();
        Select(catalogue.Spec);

        var applied = state[catalogue.Kind];
        if (applied.Status == CatalogueStatus.Error)
        {
            Toast(
                NotificationSeverity.Warning,
                string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Toast_FailedFormat, catalogue.Spec, KindName(catalogue.Kind), applied.Error),
                catalogue.Spec,
                before,
                showLogs: true);
            return;
        }

        var drawnWith = string.Format(
            CultureInfo.CurrentCulture,
            Strings.Catalogues_Sheet_OriginFormat,
            CatalogueText.KindAndVersion(catalogue.Kind, catalogue.Metadata.Version),
            CatalogueText.Lower(CatalogueOrigin.Custom));
        Toast(
            NotificationSeverity.Success,
            WithRedrawn(string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Toast_UsedFormat, catalogue.Spec, drawnWith), redrawn),
            catalogue.Spec,
            before);
    }

    /// <summary>Revert to built-in for both of <paramref name="spec"/>'s custom catalogues, or one <paramref name="kind"/>.</summary>
    internal void Revert(string spec, CatalogueKind? kind = null)
    {
        var before = Snapshot(spec);
        var redrawn = _loadedCount(spec);
        var reverted = false;
        foreach (var k in Kinds(kind))
        {
            if (before.FirstOrDefault(s => s.Kind == k)?.Origin == CatalogueOrigin.Custom)
                reverted |= _registry.Revert(spec, k);
        }

        if (!reverted)
            return;
        Refresh();
        Toast(
            NotificationSeverity.Success,
            WithRedrawn(string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Toast_RevertedFormat, spec), redrawn),
            spec,
            before);
    }

    /// <summary>Remove product (D12): drops a custom product's catalogues, so its row goes.</summary>
    internal void RemoveProduct(string spec)
    {
        var before = Snapshot(spec);
        var removed = false;
        foreach (var kind in Kinds(null))
            removed |= _registry.Revert(spec, kind);
        if (!removed)
            return;
        Refresh();
        Toast(
            NotificationSeverity.Success,
            string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Toast_RemovedFormat, spec),
            spec,
            before);
    }

    /// <summary>Reload (D11): reads <paramref name="spec"/>'s custom and command-line catalogues from disk again.</summary>
    internal void Reload(string spec)
    {
        if (_registry.Find(spec) is not { } product)
            return;
        var redrawn = _loadedCount(product.Spec);
        foreach (var kind in Kinds(null))
        {
            if (product[kind].Origin is CatalogueOrigin.Custom or CatalogueOrigin.CommandLine)
                _registry.Reload(product.Spec, kind);
        }

        Refresh();
        var after = _registry.Find(product.Spec);
        if (after?.Feature.Status == CatalogueStatus.Error || after?.Portrayal.Status == CatalogueStatus.Error)
        {
            var failed = after.Feature.Status == CatalogueStatus.Error ? after.Feature : after.Portrayal;
            Toast(
                NotificationSeverity.Warning,
                string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Toast_FailedFormat, product.Spec, KindName(failed.Kind), failed.Error),
                product.Spec,
                before: null,
                showLogs: true);
            return;
        }

        Toast(
            NotificationSeverity.Success,
            WithRedrawn(string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Toast_ReloadedFormat, product.Spec), redrawn),
            product.Spec,
            before: null);
    }

    private void ReloadAll()
    {
        foreach (var row in _allRows.Where(r => r.CanReload).ToList())
            Reload(row.Spec);
    }

    /// <summary>
    /// Undo (D6): puts back each catalogue's previous source: a custom one by
    /// its path (kept after restart as it was), or the built-in one.
    /// </summary>
    /// <param name="spec">The product.</param>
    /// <param name="before">Its catalogues before the change.</param>
    internal void Restore(string spec, IReadOnlyList<CatalogueState> before)
    {
        ArgumentNullException.ThrowIfNull(before);
        foreach (var previous in before)
        {
            var current = _registry.Find(spec)?[previous.Kind];
            if (previous.Source is { Origin: CatalogueOrigin.Custom, Path: { } path })
            {
                if (current?.Source is { Origin: CatalogueOrigin.Custom } now && now.Path == path && current.KeptAfterRestart == previous.KeptAfterRestart)
                    continue;
                if (CatalogueSpecDetection.Detect(path).Catalogue is { } catalogue)
                    _registry.Use(catalogue, previous.KeptAfterRestart);
            }
            else if (current?.Origin == CatalogueOrigin.Custom)
            {
                _registry.Revert(spec, previous.Kind);
            }
        }

        Refresh();
        Select(spec);
    }

    private IReadOnlyList<CatalogueState> Snapshot(string spec) =>
        _registry.Find(spec) is { } product ? [product.Feature, product.Portrayal] : [];

    private void Toast(NotificationSeverity severity, string message, string spec, IReadOnlyList<CatalogueState>? before, bool showLogs = false)
    {
        var toast = _notifications.Create(Strings.Catalogues_Toast_Title)
            .WithSeverity(severity)
            .WithContent(message);
        if (before is not null)
            toast = toast.WithAction(Strings.Catalogues_Undo, () => Restore(spec, before), isPrimary: true);
        if (showLogs && _showLogs is not null)
            toast = toast.WithAction(Strings.Catalogues_ShowInLogs, _showLogs);
        toast.Show();
    }

    private static string WithRedrawn(string message, int redrawn) => redrawn switch
    {
        0 => message,
        1 => $"{message} {Strings.Catalogues_Toast_RedrawnOne}",
        _ => $"{message} {string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Toast_RedrawnMany, redrawn)}",
    };

    private static IEnumerable<CatalogueKind> Kinds(CatalogueKind? kind) =>
        kind is { } k ? [k] : [CatalogueKind.Feature, CatalogueKind.Portrayal];

    private static string KindName(CatalogueKind kind) =>
        kind == CatalogueKind.Feature ? Strings.Catalogues_Kind_Feature : Strings.Catalogues_Kind_Portrayal;

    /// <inheritdoc />
    public void Dispose() => _registry.CataloguesChanged -= OnCataloguesChanged;
}
