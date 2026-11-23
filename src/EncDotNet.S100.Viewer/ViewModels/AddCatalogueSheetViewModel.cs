using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Add catalogue… confirmation sheet (design note
/// <c>catalogue-panels.md</c> D5, §6.2): what the chosen file is, which
/// product and kind it is for, what it replaces and how many loaded datasets
/// are redrawn, with <b>Keep using it after restart</b>. A path that can't be
/// used shows why, with <b>Choose another…</b> and Cancel.
/// </summary>
/// <remarks>
/// Replace… in a row's menu opens the same sheet with the product and kind
/// fixed (<see cref="Target"/>): a catalogue for another product or of the
/// other kind is refused.
/// </remarks>
internal sealed class AddCatalogueSheetViewModel : ViewModelBase
{
    private readonly CatalogueRegistry _registry;
    private readonly Func<string, int> _loadedCount;
    private readonly Func<Task<string?>> _pick;
    private readonly Action<DetectedCatalogue, bool> _use;
    private readonly Action<AddCatalogueSheetViewModel> _close;
    private DetectedCatalogue? _catalogue;
    private string? _path;
    private string? _reason;
    private bool _keepAfterRestart = true;

    /// <summary>Creates the sheet.</summary>
    /// <param name="registry">The catalogues in effect, for what the catalogue replaces.</param>
    /// <param name="loadedCount">How many loaded datasets a product's change redraws.</param>
    /// <param name="target">The product and kind Replace… fixed; null for Add catalogue….</param>
    /// <param name="pick">Picks another path, for Choose another….</param>
    /// <param name="use">Uses the catalogue, kept after restart or not.</param>
    /// <param name="close">Closes the sheet.</param>
    public AddCatalogueSheetViewModel(
        CatalogueRegistry registry,
        Func<string, int> loadedCount,
        (string Spec, CatalogueKind Kind)? target,
        Func<Task<string?>> pick,
        Action<DetectedCatalogue, bool> use,
        Action<AddCatalogueSheetViewModel> close)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(loadedCount);
        ArgumentNullException.ThrowIfNull(pick);
        ArgumentNullException.ThrowIfNull(use);
        ArgumentNullException.ThrowIfNull(close);
        _registry = registry;
        _loadedCount = loadedCount;
        _pick = pick;
        _use = use;
        _close = close;
        Target = target;
        ConfirmCommand = new RelayCommand(Confirm, () => _catalogue is not null);
        CancelCommand = new RelayCommand(() => _close(this));
        ChooseAnotherCommand = new AsyncRelayCommand(ChooseAnotherAsync);
    }

    /// <summary>The product and kind Replace… fixed; null for Add catalogue….</summary>
    public (string Spec, CatalogueKind Kind)? Target { get; }

    /// <summary>Add catalogue, or Replace S-101 portrayal catalogue.</summary>
    public string Title => Target is { } target
        ? string.Format(
            CultureInfo.CurrentCulture,
            target.Kind == CatalogueKind.Feature ? Strings.Catalogues_Sheet_ReplaceFeatureTitleFormat : Strings.Catalogues_Sheet_ReplacePortrayalTitleFormat,
            target.Spec)
        : Strings.Catalogues_Sheet_Title;

    /// <summary>The chosen path, with the home folder as <c>~</c>.</summary>
    public string PathText => HomePathRedactor.RedactOrNull(_path) ?? string.Empty;

    /// <summary>The catalogue the path holds; null when refused.</summary>
    public DetectedCatalogue? Catalogue => _catalogue;

    /// <summary>Whether the path was refused, so the reason and Choose another… show.</summary>
    public bool IsRefused => _reason is not null;

    /// <summary>Whether a catalogue was found, so the confirmation fields show.</summary>
    public bool IsFound => _catalogue is not null;

    /// <summary>Why the path can't be used; null when found.</summary>
    public string? Reason => _reason;

    /// <summary>The product, e.g. <c>"S-101"</c>.</summary>
    public string SpecText => _catalogue?.Spec ?? string.Empty;

    /// <summary>"Portrayal catalogue 2.1.0-draft".</summary>
    public string KindText => _catalogue is { } c ? CatalogueText.KindAndVersionTitle(c.Kind, c.Metadata.Version) : string.Empty;

    /// <summary>The product's name: the curated name, else the declared one.</summary>
    public string NameText => _catalogue is { } c
        ? Strings.SpecDisplayName(c.Spec) ?? _registry.Find(c.Spec)?.DeclaredName ?? c.Metadata.Name ?? string.Empty
        : string.Empty;

    /// <summary>Whether the catalogue is for a product the viewer doesn't know yet (D12).</summary>
    public bool IsNewProduct => _catalogue is { } c && _registry.Find(c.Spec) is null;

    /// <summary>"Portrayal catalogue 2.0.0 (built-in)", "No catalogue yet", or "Adds a new product".</summary>
    public string ReplacesText
    {
        get
        {
            if (_catalogue is not { } c)
                return string.Empty;
            if (_registry.Find(c.Spec) is not { } product)
                return Strings.Catalogues_Sheet_NewProduct;
            if (product[c.Kind].Source is not { } current)
                return Strings.Catalogues_Sheet_ReplacesNothing;
            return string.Format(
                CultureInfo.CurrentCulture,
                Strings.Catalogues_Sheet_OriginFormat,
                CatalogueText.KindAndVersionTitle(c.Kind, current.Metadata?.Version),
                CatalogueText.Lower(current.Origin));
        }
    }

    /// <summary>How many loaded datasets of the product it redraws.</summary>
    public int RedrawCount => _catalogue is { } c ? _loadedCount(c.Spec) : 0;

    /// <summary>"3 loaded S-101 datasets".</summary>
    public string RedrawsText => RedrawCount switch
    {
        0 => Strings.Catalogues_Sheet_RedrawsNone,
        1 => string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Sheet_RedrawsOne, SpecText),
        var n => string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Sheet_RedrawsMany, n, SpecText),
    };

    /// <summary>A note when a command-line catalogue still wins for this session; null otherwise.</summary>
    public string? CommandLineNote => _catalogue is { } c && _registry.Find(c.Spec)?[c.Kind].Origin == CatalogueOrigin.CommandLine
        ? string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Sheet_CommandLineWins, c.Spec)
        : null;

    /// <summary>Whether <see cref="CommandLineNote"/> shows.</summary>
    public bool HasCommandLineNote => CommandLineNote is not null;

    /// <summary>Keep using it after restart; cleared, it lasts for this session, like the CLI flags.</summary>
    public bool KeepAfterRestart
    {
        get => _keepAfterRestart;
        set => SetProperty(ref _keepAfterRestart, value);
    }

    /// <summary>Use it, or Add product for a new product.</summary>
    public string ConfirmLabel => IsNewProduct ? Strings.Catalogues_Sheet_AddProduct : Strings.Catalogues_Sheet_Use;

    /// <summary>Uses the catalogue and closes the sheet.</summary>
    public ICommand ConfirmCommand { get; }

    /// <summary>Closes the sheet without a change.</summary>
    public ICommand CancelCommand { get; }

    /// <summary>Picks another path and reads it.</summary>
    public IAsyncRelayCommand ChooseAnotherCommand { get; }

    /// <summary>Reads <paramref name="path"/>: what it is, or why it's refused.</summary>
    /// <param name="path">The file or folder the user chose.</param>
    public void Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        _path = path;
        var detected = CatalogueSpecDetection.Detect(path);
        _catalogue = detected.Catalogue;
        _reason = detected.Reason;
        if (_catalogue is { } c && Target is { } target && (c.Spec != target.Spec || c.Kind != target.Kind))
        {
            _reason = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Catalogues_Refused_WrongTarget,
                KindName(c.Kind), c.Spec, KindName(target.Kind), target.Spec);
            _catalogue = null;
        }

        OnPropertyChanged(string.Empty);
        ((RelayCommand)ConfirmCommand).NotifyCanExecuteChanged();
    }

    private async Task ChooseAnotherAsync()
    {
        if (await _pick().ConfigureAwait(true) is { Length: > 0 } path)
            Load(path);
    }

    private void Confirm()
    {
        if (_catalogue is not { } catalogue)
            return;
        _close(this);
        _use(catalogue, _keepAfterRestart);
    }

    private static string KindName(CatalogueKind kind) =>
        kind == CatalogueKind.Feature ? Strings.Catalogues_Kind_Feature : Strings.Catalogues_Kind_Portrayal;
}
