using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.DisplayCombinations;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>A product chip on a View menu row: <b>S-104</b>, dimmed when not loaded.</summary>
/// <param name="Product">The product key.</param>
/// <param name="IsLoaded">Whether a dataset of the product is loaded.</param>
internal sealed record DisplayCombinationChipViewModel(string Product, bool IsLoaded)
{
    /// <summary>The chip's opacity: dimmed when the product isn't loaded.</summary>
    public double Opacity => IsLoaded ? 1.0 : 0.45;

    /// <summary>The chip's accessible name and tooltip: "S-111" or "S-111 not loaded".</summary>
    public string Name => IsLoaded
        ? Product
        : string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_NotLoadedChipFormat, Product);
}

/// <summary>One display combination in the View menu and the Display controls' View section.</summary>
internal sealed class DisplayCombinationRowViewModel : ViewModelBase
{
    internal DisplayCombinationRowViewModel(
        DisplayCombination combination,
        bool isActive,
        bool isModified,
        IReadOnlySet<string> loaded,
        ICommand apply,
        ICommand rename,
        ICommand delete)
    {
        Combination = combination;
        IsActive = isActive;
        IsModified = isActive && isModified;
        Chips = [.. combination.Products
            .Order(DisplayCombinationProducts.Order)
            .Select(p => new DisplayCombinationChipViewModel(p, loaded.Contains(p)))];
        var notLoaded = Chips.Count(c => !c.IsLoaded);
        NotLoadedSummary = notLoaded == 0
            ? null
            : string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_NotLoadedCountFormat, notLoaded, Chips.Count);
        ApplyCommand = apply;
        RenameCommand = rename;
        DeleteCommand = delete;
    }

    /// <summary>The combination this row shows.</summary>
    public DisplayCombination Combination { get; }

    /// <summary>The combination's id.</summary>
    public string Id => Combination.Id;

    /// <summary>The combination's name.</summary>
    public string Label => Combination.Label;

    /// <summary>The name with the modified marker when it applies: "Pilotage (modified)".</summary>
    public string DisplayLabel => IsModified
        ? string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_ModifiedFormat, Label)
        : Label;

    /// <summary>The one-line note on what the combination adds, or <see langword="null"/>.</summary>
    public string? Description => Combination.Description;

    /// <summary>True when <see cref="Description"/> has a value.</summary>
    public bool HasDescription => Description is not null;

    /// <summary>"1", "2", "3" for the ladder combinations; <see langword="null"/> otherwise.</summary>
    public string? Step => Combination.LadderStep?.ToString(CultureInfo.CurrentCulture);

    /// <summary>True for a ladder combination.</summary>
    public bool HasStep => Combination.LadderStep is not null;

    /// <summary>True when this is the active combination.</summary>
    public bool IsActive { get; }

    /// <summary>True when this is the active combination and a setting it covers changed.</summary>
    public bool IsModified { get; }

    /// <summary>True for one of the mariner's own combinations, which can be renamed and deleted.</summary>
    public bool IsUser => Combination.Origin == DisplayCombinationOrigin.User;

    /// <summary>The products the combination names; empty for every product.</summary>
    public IReadOnlyList<DisplayCombinationChipViewModel> Chips { get; }

    /// <summary>True when the row shows product chips.</summary>
    public bool HasChips => Chips.Count > 0;

    /// <summary>"3 of 7 products not loaded", or <see langword="null"/> when all are loaded.</summary>
    public string? NotLoadedSummary { get; }

    /// <summary>True when <see cref="NotLoadedSummary"/> has a value.</summary>
    public bool HasNotLoaded => NotLoadedSummary is not null;

    /// <summary>Applies the combination.</summary>
    public ICommand ApplyCommand { get; }

    /// <summary>Starts renaming the combination (user combinations only).</summary>
    public ICommand RenameCommand { get; }

    /// <summary>Deletes the combination (user combinations only).</summary>
    public ICommand DeleteCommand { get; }
}

/// <summary>
/// Backs the map's <b>View ▾</b> pill and the <b>View</b> section of the
/// Display controls panel (design note
/// <c>docs/design/predefined-display-combinations.md</c> §4): the list of
/// display combinations grouped as <b>Build up the picture</b>, <b>More</b>
/// and <b>My views</b>, the modified marker, <b>Reapply</b>, and the save and
/// rename form.
/// </summary>
internal sealed class DisplayCombinationsViewModel : ViewModelBase, IDisposable
{
    private readonly DisplayCombinationService _service;
    private readonly IViewerUiControllerAccessor? _ui;
    private readonly RelayCommand _reapply;
    private string? _renamingId;
    private string _editName = "";
    private bool _isEditing;
    private IReadOnlyList<string> _savedLines = [];

    /// <summary>Creates the view-model over <paramref name="service"/>.</summary>
    /// <param name="service">The combinations.</param>
    /// <param name="ui">Opens the Display controls panel for <b>Manage…</b>; optional.</param>
    public DisplayCombinationsViewModel(DisplayCombinationService service, IViewerUiControllerAccessor? ui = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        _ui = ui;
        _reapply = new RelayCommand(() => _service.Reapply(), () => _service.Active is not null);
        BeginSaveCommand = new RelayCommand(BeginSave);
        ConfirmEditCommand = new RelayCommand(ConfirmEdit, () => !string.IsNullOrWhiteSpace(_editName));
        CancelEditCommand = new RelayCommand(() => IsEditing = false);
        ManageCommand = new RelayCommand(() => _ = _ui?.Current?.SetPanelVisibilityAsync("EcdisDisplay", true));
        _service.Changed += Rebuild;
        Rebuild();
    }

    /// <summary>The <b>Build up the picture</b> rows, in ladder order.</summary>
    public ObservableCollection<DisplayCombinationRowViewModel> LadderRows { get; } = [];

    /// <summary>The other built-in rows.</summary>
    public ObservableCollection<DisplayCombinationRowViewModel> MoreRows { get; } = [];

    /// <summary>The mariner's own rows.</summary>
    public ObservableCollection<DisplayCombinationRowViewModel> UserRows { get; } = [];

    /// <summary>True when the mariner has saved a combination.</summary>
    public bool HasUserRows => UserRows.Count > 0;

    /// <summary>"Pilotage", "Pilotage (modified)" or "Custom".</summary>
    public string ActiveLabel { get; private set; } = "";

    /// <summary>The pill's label: "View: Pilotage ▾".</summary>
    public string PillLabel => string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_PillFormat, ActiveLabel);

    /// <summary>True while a combination is active.</summary>
    public bool HasActive => _service.Active is not null;

    /// <summary>True when the active combination is modified.</summary>
    public bool IsModified => _service.IsModified;

    /// <summary>"Changed: Display category Standard → All.", or <see langword="null"/>.</summary>
    public string? ChangesText { get; private set; }

    /// <summary>True when <see cref="ChangesText"/> has a value.</summary>
    public bool HasChanges => ChangesText is not null;

    /// <summary>Applies the active combination again.</summary>
    public ICommand ReapplyCommand => _reapply;

    /// <summary>Opens the save form.</summary>
    public ICommand BeginSaveCommand { get; }

    /// <summary>Saves or renames, and closes the form.</summary>
    public ICommand ConfirmEditCommand { get; }

    /// <summary>Closes the form without saving.</summary>
    public ICommand CancelEditCommand { get; }

    /// <summary>Opens the Display controls panel.</summary>
    public ICommand ManageCommand { get; }

    /// <summary>True while the save or rename form is open.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set
        {
            if (SetProperty(ref _isEditing, value))
            {
                OnPropertyChanged(nameof(IsSaving));
                OnPropertyChanged(nameof(EditTitle));
            }
        }
    }

    /// <summary>True while the form saves a new view (not a rename), so it lists what is and isn't saved.</summary>
    public bool IsSaving => _isEditing && _renamingId is null;

    /// <summary>"Save current view" or "Rename view".</summary>
    public string EditTitle => _renamingId is null ? Strings.DisplayCombination_SaveTitle : Strings.DisplayCombination_RenameTitle;

    /// <summary>The name in the form.</summary>
    public string EditName
    {
        get => _editName;
        set
        {
            if (SetProperty(ref _editName, value ?? ""))
                ((RelayCommand)ConfirmEditCommand).NotifyCanExecuteChanged();
        }
    }

    /// <summary>What a saved view captures, line by line, for the save form.</summary>
    public IReadOnlyList<string> SavedLines
    {
        get => _savedLines;
        private set => SetProperty(ref _savedLines, value);
    }

    private void BeginSave()
    {
        _renamingId = null;
        EditName = string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_DefaultNameFormat, _service.UserCombinations.Count + 1);
        SavedLines = DescribeSaved(_service.CaptureCurrent("preview", EditName));
        IsEditing = true;
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(EditTitle));
    }

    private void BeginRename(string id)
    {
        if (_service.UserCombinations.FirstOrDefault(c => c.Id == id) is not { } combination)
            return;
        _renamingId = id;
        EditName = combination.Label;
        IsEditing = true;
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(EditTitle));
    }

    private void ConfirmEdit()
    {
        if (string.IsNullOrWhiteSpace(_editName)) return;
        if (_renamingId is { } id)
            _service.Rename(id, _editName);
        else
            _service.SaveCurrent(_editName);
        _renamingId = null;
        IsEditing = false;
    }

    private void Rebuild()
    {
        var loaded = _service.LoadedProducts;
        var active = _service.Active;
        DisplayCombinationRowViewModel Row(DisplayCombination c) => new(
            c,
            active is not null && active.Id == c.Id,
            _service.IsModified,
            loaded,
            new RelayCommand(() => _service.Apply(c.Id)),
            new RelayCommand(() => BeginRename(c.Id)),
            new RelayCommand(() => _service.Delete(c.Id)));

        Replace(LadderRows, _service.BuiltIns.Where(c => c.LadderStep is not null).OrderBy(c => c.LadderStep).Select(Row));
        Replace(MoreRows, _service.BuiltIns.Where(c => c.LadderStep is null).Select(Row));
        Replace(UserRows, _service.UserCombinations.Select(Row));

        ActiveLabel = active is null
            ? Strings.DisplayCombination_Custom
            : _service.IsModified
                ? string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_ModifiedFormat, active.Label)
                : active.Label;
        ChangesText = _service.IsModified ? DescribeChanges(_service.Changes) : null;

        OnPropertyChanged(nameof(HasUserRows));
        OnPropertyChanged(nameof(ActiveLabel));
        OnPropertyChanged(nameof(PillLabel));
        OnPropertyChanged(nameof(HasActive));
        OnPropertyChanged(nameof(IsModified));
        OnPropertyChanged(nameof(ChangesText));
        OnPropertyChanged(nameof(HasChanges));
        _reapply.NotifyCanExecuteChanged();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
            target.Add(item);
    }

    /// <summary>"Changed: Display category Standard → All, S-102 made active."</summary>
    internal static string DescribeChanges(IReadOnlyList<DisplayCombinationChange> changes)
    {
        var parts = changes.Select(Describe);
        return string.Format(
            CultureInfo.CurrentCulture,
            Strings.DisplayCombination_ChangedFormat,
            string.Join(Strings.DisplayCombination_ChangeSeparator, parts));
    }

    private static string Describe(DisplayCombinationChange change) => change.Kind switch
    {
        DisplayCombinationChangeKind.Category => string.Format(
            CultureInfo.CurrentCulture,
            Strings.DisplayCombination_Change_CategoryFormat,
            DisplayToolbarViewModel.CategoryDisplayName(change.Expected ?? EcdisDisplayCategory.Standard),
            DisplayToolbarViewModel.CategoryDisplayName(change.Actual ?? EcdisDisplayCategory.Standard)),
        DisplayCombinationChangeKind.ProductMadeActive => Format(Strings.DisplayCombination_Change_ProductOnFormat, change.Subject),
        DisplayCombinationChangeKind.ProductMadeInactive => Format(Strings.DisplayCombination_Change_ProductOffFormat, change.Subject),
        DisplayCombinationChangeKind.ViewingGroups => Format(Strings.DisplayCombination_Change_ViewingGroupsFormat, change.Subject),
        DisplayCombinationChangeKind.DisplayMode => Format(Strings.DisplayCombination_Change_DisplayModeFormat, change.Subject),
        DisplayCombinationChangeKind.Layer => DatasetLoaderService.ResolveSubLayerDisplayName(change.Subject ?? ""),
        DisplayCombinationChangeKind.SubLayer => Format(
            Strings.DisplayCombination_Change_SubLayerFormat,
            DisplayCombinationProducts.OfLayerKey(change.Subject ?? "")),
        _ => change.Subject ?? "",
    };

    private static string Format(string format, string? subject) =>
        string.Format(CultureInfo.CurrentCulture, format, subject ?? "");

    /// <summary>The save form's lines for what <paramref name="combination"/> captures.</summary>
    internal static IReadOnlyList<string> DescribeSaved(DisplayCombination combination)
    {
        var lines = new List<string>
        {
            combination.IncludesEveryProduct
                ? Strings.DisplayCombination_SavedProductsAll
                : string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_SavedProductsFormat,
                    string.Join(", ", combination.Products.Order(DisplayCombinationProducts.Order))),
        };

        combination.ShownViewingGroups.TryGetValue(DisplayCombinationProducts.Chart, out var shown);
        var text = new List<string>();
        if (shown is not null && BuiltInDisplayCombinations.S101ImportantText.All(shown.Contains))
            text.Add(Strings.Segment_TextImportant);
        if (shown is not null && BuiltInDisplayCombinations.S101OtherText.All(shown.Contains))
            text.Add(Strings.Segment_TextOther);
        lines.Add(string.Format(
            CultureInfo.CurrentCulture,
            Strings.DisplayCombination_SavedDisplayFormat,
            DisplayToolbarViewModel.CategoryDisplayName(combination.Category ?? EcdisDisplayCategory.Standard),
            text.Count == 0 ? Strings.DisplayCombination_SavedTextNone : string.Join(", ", text)));

        var hiddenCount = combination.HiddenViewingGroups.Values.Sum(s => s.Count);
        lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_SavedViewingGroupsFormat, hiddenCount));

        foreach (var product in combination.SubLayerVisibility
            .GroupBy(kv => DisplayCombinationProducts.OfLayerKey(kv.Key), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, DisplayCombinationProducts.Order))
        {
            var parts = product
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => string.Format(
                    CultureInfo.CurrentCulture,
                    kv.Value ? Strings.DisplayCombination_SavedSubLayerOnFormat : Strings.DisplayCombination_SavedSubLayerOffFormat,
                    DatasetLoaderService.ResolveSubLayerDisplayName(kv.Key)));
            lines.Add(string.Format(CultureInfo.CurrentCulture, Strings.DisplayCombination_SavedSubLayersFormat, product.Key, string.Join(", ", parts)));
        }
        return lines;
    }

    /// <inheritdoc />
    public void Dispose() => _service.Changed -= Rebuild;
}
