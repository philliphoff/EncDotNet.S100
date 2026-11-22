using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Feature Catalogues tab: one row per product with a feature catalogue,
/// read from <see cref="CatalogueRegistry"/>. A stopgap until the Catalogues
/// activity replaces it (design note <c>catalogue-panels.md</c>, CR2).
/// </summary>
internal sealed class FeatureCataloguesViewModel : ViewModelBase
{
    private readonly CatalogueRegistry _registry;
    private readonly Services.ExaminerLinks _examinerLinks;

    public ObservableCollection<CatalogueEntry> Entries { get; } = new();

    /// <summary>Reverts a custom row to the built-in catalogue.</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>
    /// Opens the row's catalogue in the S-100 Feature Catalogue eXaminer
    /// (issue #442). No-op when the examiner integration is unavailable.
    /// </summary>
    public ICommand OpenInExaminerCommand { get; }

    /// <summary>Creates the Feature Catalogues tab.</summary>
    /// <param name="registry">The catalogues in effect.</param>
    /// <param name="examinerLinks">Opens a catalogue in the eXaminer.</param>
    public FeatureCataloguesViewModel(CatalogueRegistry registry, Services.ExaminerLinks examinerLinks)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(examinerLinks);
        _registry = registry;
        _examinerLinks = examinerLinks;
        RemoveCommand = new RelayCommand<CatalogueEntry>(Remove);
        OpenInExaminerCommand = new RelayCommand<CatalogueEntry>(OpenInExaminer);
        _registry.CataloguesChanged += (_, _) => UiThread.RunOrPost(Reload);
        Reload();
    }

    private void OpenInExaminer(CatalogueEntry? entry)
    {
        if (entry is not null)
            _examinerLinks.OpenCatalogue(entry.ProductSpec);
    }

    /// <summary>Rebuilds the rows from the registry.</summary>
    public void Reload() => CatalogueRows.Fill(
        Entries, _registry.Products, CatalogueKind.Feature, ExaminerSupports);

    /// <summary>
    /// Uses the catalogue at <paramref name="path"/>, whatever its product
    /// (found from its content), and keeps it after restart.
    /// </summary>
    /// <returns>Null when it's in use; otherwise why it can't be used.</returns>
    public string? Add(string path) => CatalogueRows.Add(_registry, path);

    private bool ExaminerSupports(string spec) =>
        _examinerLinks.SupportsSpec(spec);

    /// <summary>
    /// Re-evaluates <see cref="CatalogueEntry.CanOpenInExaminer"/> for every
    /// loaded entry against the current examiner settings. Called when the
    /// user toggles the integration or changes the base URL so the per-row
    /// "open in eXaminer" buttons appear/disappear without reloading the list
    /// (issue #442).
    /// </summary>
    public void RefreshExaminerAvailability()
    {
        foreach (var entry in Entries)
        {
            entry.CanOpenInExaminer = ExaminerSupports(entry.ProductSpec);
        }
    }

    /// <summary>
    /// Composes the primary list line: the spec code followed by the
    /// product name (e.g. "S-101 Electronic Navigational Chart"), or just
    /// the spec code when no name is available.
    /// </summary>
    internal static string ComposeTitle(string spec, string? name) =>
        string.IsNullOrWhiteSpace(name) ? spec : $"{spec} {name}";

    private void Remove(CatalogueEntry? entry)
    {
        if (entry is { CanRemove: true })
            _registry.Revert(entry.ProductSpec, CatalogueKind.Feature);
    }
}

/// <summary>
/// The Portrayal Catalogues tab: one row per product with a portrayal
/// catalogue, read from <see cref="CatalogueRegistry"/>. A stopgap until the
/// Catalogues activity replaces it (CR2).
/// </summary>
internal sealed class PortrayalCataloguesViewModel : ViewModelBase
{
    private readonly CatalogueRegistry _registry;

    public ObservableCollection<CatalogueEntry> Entries { get; } = new();

    /// <summary>Reverts a custom row to the built-in catalogue.</summary>
    public ICommand RemoveCommand { get; }

    /// <summary>Creates the Portrayal Catalogues tab.</summary>
    /// <param name="registry">The catalogues in effect.</param>
    public PortrayalCataloguesViewModel(CatalogueRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
        RemoveCommand = new RelayCommand<CatalogueEntry>(Remove);
        _registry.CataloguesChanged += (_, _) => UiThread.RunOrPost(Reload);
        Reload();
    }

    /// <summary>Rebuilds the rows from the registry.</summary>
    public void Reload() => CatalogueRows.Fill(
        Entries, _registry.Products, CatalogueKind.Portrayal, static _ => false);

    /// <summary>
    /// Uses the catalogue folder or package at <paramref name="path"/>,
    /// whatever its product (found from its content), and keeps it after
    /// restart.
    /// </summary>
    /// <returns>Null when it's in use; otherwise why it can't be used.</returns>
    public string? Add(string path) => CatalogueRows.Add(_registry, path);

    private void Remove(CatalogueEntry? entry)
    {
        if (entry is { CanRemove: true })
            _registry.Revert(entry.ProductSpec, CatalogueKind.Portrayal);
    }
}

/// <summary>Builds the stopgap panels' rows from the registry's state.</summary>
internal static class CatalogueRows
{
    /// <summary>Replaces <paramref name="entries"/> with one row per product that has a <paramref name="kind"/> catalogue.</summary>
    public static void Fill(
        ObservableCollection<CatalogueEntry> entries,
        IReadOnlyList<CatalogueProductState> products,
        CatalogueKind kind,
        Func<string, bool> examinerSupports)
    {
        entries.Clear();
        foreach (var product in products)
        {
            var state = product[kind];
            if (state.Source is not { } source)
                continue;
            var name = Strings.SpecDisplayName(product.Spec) ?? product.DeclaredName;
            var metadata = source.Metadata;
            entries.Add(new CatalogueEntry(
                product.Spec,
                FeatureCataloguesViewModel.ComposeTitle(product.Spec, name),
                source.Path ?? Strings.Catalogue_BuiltInLabel,
                isBuiltIn: source.Origin == CatalogueOrigin.BuiltIn,
                version: metadata?.Version,
                versionDate: metadata?.VersionDate,
                canOpenInExaminer: kind == CatalogueKind.Feature && examinerSupports(product.Spec),
                stateText: StateText(state),
                hasError: state.Status == CatalogueStatus.Error,
                canRemove: source.Origin == CatalogueOrigin.Custom));
        }
    }

    /// <summary>Detects the catalogue at <paramref name="path"/> and uses it.</summary>
    /// <returns>Null when it's in use; otherwise why it can't be used.</returns>
    public static string? Add(CatalogueRegistry registry, string path)
    {
        var detected = CatalogueSpecDetection.Detect(path);
        if (detected.Catalogue is not { } catalogue)
            return detected.Reason;
        var state = registry.Use(catalogue)[catalogue.Kind];
        return state.Status == CatalogueStatus.Error ? state.Error : null;
    }

    /// <summary>
    /// What's in effect, in one line (audit A8): "Command line, this session ·
    /// overrides built-in 2.0.0", or the error and what's drawing instead.
    /// </summary>
    public static string? StateText(CatalogueState state)
    {
        var parts = new List<string>(2);
        switch (state.Origin)
        {
            case CatalogueOrigin.CommandLine:
                parts.Add(Strings.Catalogues_Origin_CommandLine);
                break;
            case CatalogueOrigin.Custom:
                parts.Add(state.KeptAfterRestart ? Strings.Catalogues_Origin_Custom : Strings.Catalogues_Origin_CustomSession);
                break;
        }

        if (state.Status == CatalogueStatus.Error)
        {
            parts.Add(state.Drawing is { } drawing
                ? string.Format(Strings.Catalogues_State_ErrorUsing, state.Error, Describe(drawing))
                : string.Format(Strings.Catalogues_State_Error, state.Error));
        }
        else if (state.Overrides is { } overridden)
        {
            parts.Add(string.Format(Strings.Catalogues_State_Overrides, Describe(overridden)));
        }

        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    private static string Describe(CatalogueSourceInfo source)
    {
        var origin = source.Origin switch
        {
            CatalogueOrigin.BuiltIn => Strings.Catalogues_Origin_BuiltIn,
            CatalogueOrigin.Custom => Strings.Catalogues_Origin_Custom,
            _ => Strings.Catalogues_Origin_CommandLine,
        };
        return source.Metadata?.Version is { } version ? $"{origin} {version}" : origin;
    }
}

internal sealed class CatalogueEntry : ViewModelBase
{
    public string ProductSpec { get; }

    /// <summary>
    /// Human-readable product title shown as the primary line in the list
    /// (e.g. "Electronic Navigational Chart"). Resolved from the curated
    /// spec-name map, the catalogue's declared name, or the spec code.
    /// </summary>
    public string DisplayTitle { get; }

    public string Path { get; }
    public bool IsBuiltIn { get; }
    public string? Version { get; }
    public string? VersionDate { get; }

    /// <summary>
    /// Secondary identity line: version number, version date, and a
    /// "built-in" marker for bundled catalogues. The spec code is omitted
    /// here because it already leads <see cref="DisplayTitle"/>.
    /// </summary>
    public string Subtitle
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(Version))
            {
                parts.Add($"v{Version}");
            }
            if (!string.IsNullOrEmpty(VersionDate))
            {
                parts.Add(VersionDate!);
            }
            if (IsBuiltIn)
            {
                parts.Add(Strings.Catalogue_BuiltInLabel);
            }
            return string.Join(" · ", parts);
        }
    }

    /// <summary>True when <see cref="Subtitle"/> has content to display.</summary>
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>
    /// True when the file-system path should be shown. Built-in catalogues
    /// carry no meaningful path (their provenance is shown in the subtitle).
    /// </summary>
    public bool ShowPath => !IsBuiltIn && !string.IsNullOrEmpty(Path);

    /// <summary>
    /// True when this catalogue can be opened in the S-100 Feature Catalogue
    /// eXaminer (issue #442): the examiner integration is enabled and it
    /// hosts a Feature Catalogue for <see cref="ProductSpec"/>. Gates the
    /// per-row "open in eXaminer" button. Always <c>false</c> for portrayal
    /// catalogue entries. Live-refreshed by
    /// <see cref="FeatureCataloguesViewModel.RefreshExaminerAvailability"/>
    /// when the examiner settings change.
    /// </summary>
    public bool CanOpenInExaminer
    {
        get => _canOpenInExaminer;
        internal set => SetProperty(ref _canOpenInExaminer, value);
    }

    private bool _canOpenInExaminer;

    /// <summary>
    /// What's in effect, in one line (audit A8): where the catalogue comes
    /// from, what it overrides, or why it failed and what's used instead;
    /// null for a built-in catalogue in use.
    /// </summary>
    public string? StateText { get; }

    /// <summary>True when <see cref="StateText"/> has content to display.</summary>
    public bool HasStateText => !string.IsNullOrEmpty(StateText);

    /// <summary>True when the catalogue failed to load.</summary>
    public bool HasError { get; }

    /// <summary>True for a custom catalogue the user can revert; false for built-in and command-line ones.</summary>
    public bool CanRemove { get; }

    public CatalogueEntry(
        string productSpec,
        string displayTitle,
        string path,
        bool isBuiltIn = false,
        string? version = null,
        string? versionDate = null,
        bool canOpenInExaminer = false,
        string? stateText = null,
        bool hasError = false,
        bool canRemove = false)
    {
        StateText = stateText;
        HasError = hasError;
        CanRemove = canRemove;
        ProductSpec = productSpec;
        DisplayTitle = displayTitle;
        Path = path;
        IsBuiltIn = isBuiltIn;
        Version = version;
        VersionDate = versionDate;
        CanOpenInExaminer = canOpenInExaminer;
    }
}
