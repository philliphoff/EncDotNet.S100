using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Core;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// The Chart reference product header (design note
/// <c>catalogue-panels.md</c> D8, D9, §6.3, CR4): one line naming the
/// selected product's catalogues in use and where they come from, with
/// <b>Catalogues…</b> (the Catalogues activity at the product) and, where it
/// applies, the eXaminer link.
/// </summary>
/// <remarks>
/// The line reads <see cref="CatalogueRegistry"/>, so it says what draws:
/// a custom catalogue that failed to load reads as the built-in one drawing
/// in its place. It follows the activity's product and reads the registry
/// again when <see cref="CatalogueRegistry.CataloguesChanged"/> fires. The
/// activity and its Open-as-page view share it.
/// </remarks>
internal sealed class ChartReferenceProductHeaderViewModel : ViewModelBase, IDisposable
{
    private readonly ChartReferenceViewModel _reference;
    private readonly CatalogueRegistry _registry;
    private readonly ExaminerLinks _examiner;
    private readonly Action<string>? _showCatalogues;
    private readonly Action<Action> _post;
    private CatalogueProductState? _state;
    private bool _canOpenInExaminer;
    private bool _disposed;

    /// <summary>Creates the header for <paramref name="reference"/>'s selected product.</summary>
    /// <param name="reference">The Chart reference activity, whose product the header follows.</param>
    /// <param name="registry">The catalogues in effect.</param>
    /// <param name="examiner">Whether the eXaminer hosts a product, and opens its page.</param>
    /// <param name="showCatalogues">
    /// Opens the Catalogues activity at a product
    /// (<see cref="CataloguesViewModel.ShowProduct"/>); <see langword="null"/>
    /// hides <b>Catalogues…</b>.
    /// </param>
    /// <param name="post">
    /// Runs an action on the UI thread; <see langword="null"/> runs it at
    /// once on the calling thread.
    /// </param>
    public ChartReferenceProductHeaderViewModel(
        ChartReferenceViewModel reference,
        CatalogueRegistry registry,
        ExaminerLinks examiner,
        Action<string>? showCatalogues = null,
        Action<Action>? post = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(examiner);
        _reference = reference;
        _registry = registry;
        _examiner = examiner;
        _showCatalogues = showCatalogues;
        _post = post ?? (a => a());

        ShowCataloguesCommand = new RelayCommand(
            () => { if (Spec is { } spec) _showCatalogues?.Invoke(spec); });
        OpenInExaminerCommand = new RelayCommand(
            () => { if (_canOpenInExaminer) _examiner.OpenCatalogue(Spec); });

        _reference.PropertyChanged += OnReferenceChanged;
        _registry.CataloguesChanged += OnCataloguesChanged;
        Refresh();
    }

    /// <summary>The selected product's spec, e.g. <c>"S-101"</c>; <see langword="null"/> when none.</summary>
    public string? Spec => _reference.SelectedProduct?.Spec.Name;

    /// <summary>Whether the header shows: the selected product is one the registry knows.</summary>
    public bool IsVisible => _state is not null;

    /// <summary>
    /// "S-101 · Feature 2.0.0 · Portrayal 2.1.0-draft (custom)", or
    /// "S-101 · Feature 2.0.0 · Portrayal 2.0.0 · Built-in" when both are
    /// built in; empty when the header is hidden.
    /// </summary>
    public string IdentityText => _state is { } state ? Compose(state) : string.Empty;

    /// <summary>Whether <b>Catalogues…</b> shows.</summary>
    public bool CanShowCatalogues => _showCatalogues is not null && _state is not null;

    /// <summary>Opens the Catalogues activity at the product (D8).</summary>
    public ICommand ShowCataloguesCommand { get; }

    /// <summary>Whether the eXaminer link shows: the eXaminer is on and hosts the product (D9).</summary>
    public bool CanOpenInExaminer => _canOpenInExaminer;

    /// <summary>
    /// Whether the feature catalogue in use is custom or from the command
    /// line, so the eXaminer has only the published edition (D9, audit A9).
    /// </summary>
    public bool IsFeatureCatalogueCustom =>
        InUse(_state?.Feature)?.Origin is CatalogueOrigin.Custom or CatalogueOrigin.CommandLine;

    /// <summary>"eXaminer", or "Published edition in eXaminer" for a custom feature catalogue.</summary>
    public string ExaminerLabel => IsFeatureCatalogueCustom
        ? Strings.ChartReference_Header_ExaminerPublished
        : Strings.ChartReference_Header_Examiner;

    /// <summary>The eXaminer link's tooltip, which says when it isn't the catalogue in use.</summary>
    public string ExaminerTooltip => IsFeatureCatalogueCustom
        ? Strings.Tooltip_ChartReferenceHeaderExaminerPublished
        : Strings.Tooltip_ChartReferenceHeaderExaminer;

    /// <summary>Opens the product's feature catalogue in the eXaminer.</summary>
    public ICommand OpenInExaminerCommand { get; }

    /// <summary>
    /// Reads the registry and the eXaminer settings again. Called for a
    /// catalogue change, a new product, and when the eXaminer settings
    /// change.
    /// </summary>
    public void Refresh()
    {
        if (_disposed)
            return;
        var spec = Spec;
        _state = spec is null ? null : _registry.Find(spec);
        _canOpenInExaminer = _state is not null && _examiner.SupportsSpec(spec);
        OnPropertyChanged(string.Empty);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _reference.PropertyChanged -= OnReferenceChanged;
        _registry.CataloguesChanged -= OnCataloguesChanged;
    }

    private static string Compose(CatalogueProductState state)
    {
        var parts = new List<string>(4) { state.Spec, Describe(state.Feature), Describe(state.Portrayal) };
        var origins = new[] { InUse(state.Feature), InUse(state.Portrayal) }
            .OfType<CatalogueSourceInfo>()
            .Select(s => s.Origin)
            .ToList();
        if (origins.Count > 0 && origins.All(o => o == CatalogueOrigin.BuiltIn))
            parts.Add(Strings.Catalogues_Origin_BuiltIn);
        return string.Join(" · ", parts);
    }

    // "Feature 2.0.0", "Portrayal 2.1.0-draft (custom)", "No portrayal
    // catalogue" or "Drawn by SoundCharts": what draws, and where it's from
    // when that isn't built in.
    private static string Describe(CatalogueState state)
    {
        if (state.Status == CatalogueStatus.NotRequired)
            return Strings.Catalogues_Row_DrawnBySoundCharts;
        var source = InUse(state);
        if (source is null)
        {
            return state.Kind == CatalogueKind.Feature
                ? Strings.Catalogues_Row_FeatureMissing
                : Strings.Catalogues_Row_PortrayalMissing;
        }

        var text = string.Format(
            CultureInfo.CurrentCulture,
            state.Kind == CatalogueKind.Feature ? Strings.Catalogues_Row_FeatureFormat : Strings.Catalogues_Row_PortrayalFormat,
            CatalogueText.Version(source));
        return source.Origin == CatalogueOrigin.BuiltIn
            ? text
            : string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_Header_SourceFormat, text, CatalogueText.Lower(source.Origin));
    }

    // The catalogue that draws: the configured one, or what draws in its place when it failed.
    private static CatalogueSourceInfo? InUse(CatalogueState? state) => state?.Status switch
    {
        null => null,
        CatalogueStatus.Error => state.Drawing,
        _ => state.Drawing ?? state.Source,
    };

    private void OnReferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ChartReferenceViewModel.SelectedProduct) or null or "")
            Refresh();
    }

    private void OnCataloguesChanged(object? sender, CataloguesChangedEventArgs e) => _post(Refresh);
}
