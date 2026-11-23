using System.Globalization;
using EncDotNet.S100.Viewer.Diagnostics;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// One product in the Catalogues activity (design note
/// <c>catalogue-panels.md</c> D2, D4, §6.1): its two catalogues as in effect,
/// the tags that say what isn't the default, how many of its datasets are
/// loaded, and the details pane's fields.
/// </summary>
/// <remarks>
/// A row keeps its identity (the product spec) across registry changes;
/// <see cref="Update"/> swaps in the new state so the list keeps its
/// selection.
/// </remarks>
internal sealed class CatalogueProductRowViewModel : ViewModelBase
{
    private CatalogueProductState _state;
    private int _loadedCount;
    private bool _canOpenInExaminer;

    /// <summary>Creates the row for <paramref name="state"/>.</summary>
    /// <param name="state">The product's catalogues as in effect.</param>
    /// <param name="loadedCount">How many of its datasets are loaded.</param>
    /// <param name="canOpenInExaminer">Whether the eXaminer hosts the product's feature catalogue.</param>
    public CatalogueProductRowViewModel(CatalogueProductState state, int loadedCount, bool canOpenInExaminer)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        _loadedCount = loadedCount;
        _canOpenInExaminer = canOpenInExaminer;
        Feature = new CatalogueDetailsGroupViewModel(state.Feature);
        Portrayal = new CatalogueDetailsGroupViewModel(state.Portrayal);
    }

    /// <summary>The activity the row is in, for its menu's commands (a flyout can't reach the list's data context).</summary>
    public CataloguesViewModel? Owner { get; internal set; }

    /// <summary>The product spec, e.g. <c>"S-101"</c>.</summary>
    public string Spec => _state.Spec;

    /// <summary>The product's state as in effect.</summary>
    public CatalogueProductState State => _state;

    /// <summary>The product's name: the curated name, else the feature catalogue's declared name; null when neither.</summary>
    public string? Name => Strings.SpecDisplayName(_state.Spec) ?? _state.DeclaredName;

    /// <summary>The primary line: "S-101 Electronic Navigational Chart".</summary>
    public string Title => ComposeTitle(_state.Spec, Name);

    /// <summary>The tags that say what isn't the default; empty for a bundled product as shipped (D4).</summary>
    public IReadOnlyList<CatalogueTagViewModel> Tags
    {
        get
        {
            var tags = new List<CatalogueTagViewModel>(4);
            if (HasOrigin(CatalogueOrigin.Custom))
                tags.Add(new(Strings.Catalogues_Tag_Custom, CatalogueTagKind.Neutral));
            if (HasOrigin(CatalogueOrigin.CommandLine))
                tags.Add(new(Strings.Catalogues_Tag_CommandLine, CatalogueTagKind.Neutral));
            if (!_state.IsBuiltInProduct)
                tags.Add(new(Strings.Catalogues_Tag_NotBuiltIn, CatalogueTagKind.Neutral));
            var errors = ErrorCount;
            if (errors > 0)
            {
                tags.Add(new(
                    errors == 1 ? Strings.Catalogues_Tag_ProblemOne : string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Tag_ProblemMany, errors),
                    CatalogueTagKind.Error));
            }

            if (IsMissing)
                tags.Add(new(Strings.Catalogues_Tag_Missing, CatalogueTagKind.Warning));
            return tags;
        }
    }

    /// <summary>Whether <see cref="Tags"/> has any.</summary>
    public bool HasTags => Tags.Count > 0;

    /// <summary>The second line: "Feature 2.0.0 · Portrayal 2.1.0-draft", or "Drawn by SoundCharts" for the portrayal of S-104.</summary>
    public string CataloguesText => $"{Describe(_state.Feature)} · {Describe(_state.Portrayal)}";

    /// <summary>How many datasets of the product are loaded.</summary>
    public int LoadedCount => _loadedCount;

    /// <summary>Whether any dataset of the product is loaded (the Loaded pill, D3).</summary>
    public bool IsLoaded => _loadedCount > 0;

    /// <summary>"3 loaded"; empty when none.</summary>
    public string LoadedText => _loadedCount > 0
        ? string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Row_LoadedFormat, _loadedCount)
        : string.Empty;

    /// <summary>Whether a catalogue failed to load: the row's red bar.</summary>
    public bool IsError => ErrorCount > 0;

    /// <summary>Whether a catalogue is missing and none failed: the row's amber bar.</summary>
    public bool IsMissingOnly => IsMissing && !IsError;

    /// <summary>Whether a catalogue the product needs is missing.</summary>
    public bool IsMissing => _state.Feature.Status == CatalogueStatus.Missing || _state.Portrayal.Status == CatalogueStatus.Missing;

    /// <summary>Whether the product has an Error or Missing catalogue (the Problems pill).</summary>
    public bool HasProblem => _state.HasProblem;

    /// <summary>Whether anything isn't built in (the Changed pill).</summary>
    public bool IsChanged => _state.IsChanged;

    /// <summary>The row's accessible name: its title, tags, catalogues and loaded count.</summary>
    public string AccessibleName =>
        string.Join(", ", new[] { Title }
            .Concat(Tags.Select(t => t.Text))
            .Append(CataloguesText)
            .Append(LoadedText)
            .Where(s => s.Length > 0));

    // ── Details (§6.1) ────────────────────────────────────────────────────

    /// <summary>The feature catalogue's fields.</summary>
    public CatalogueDetailsGroupViewModel Feature { get; private set; }

    /// <summary>The portrayal catalogue's fields.</summary>
    public CatalogueDetailsGroupViewModel Portrayal { get; private set; }

    /// <summary>"3 datasets loaded · drawn with a custom catalogue".</summary>
    public string DetailsSummary
    {
        get
        {
            var loaded = _loadedCount switch
            {
                0 => Strings.Catalogues_Details_LoadedNone,
                1 => Strings.Catalogues_Details_LoadedOne,
                _ => string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Details_LoadedMany, _loadedCount),
            };
            if (HasOrigin(CatalogueOrigin.CommandLine))
                return $"{loaded} · {Strings.Catalogues_Details_DrawnWithCommandLine}";
            if (HasOrigin(CatalogueOrigin.Custom))
                return $"{loaded} · {Strings.Catalogues_Details_DrawnWithCustom}";
            return loaded;
        }
    }

    /// <summary>Whether either catalogue is custom or from the command line, so Reload applies (D11).</summary>
    public bool CanReload => HasOrigin(CatalogueOrigin.Custom) || HasOrigin(CatalogueOrigin.CommandLine);

    /// <summary>Whether a custom catalogue overrides a built-in one, so Revert to built-in applies.</summary>
    public bool CanRevert => _state.IsBuiltInProduct && HasOrigin(CatalogueOrigin.Custom);

    /// <summary>Whether the feature catalogue is a custom one over a built-in one.</summary>
    public bool CanRevertFeature => _state.IsBuiltInProduct && _state.Feature.Origin == CatalogueOrigin.Custom;

    /// <summary>Whether the portrayal catalogue is a custom one over a built-in one.</summary>
    public bool CanRevertPortrayal => _state.IsBuiltInProduct && _state.Portrayal.Origin == CatalogueOrigin.Custom;

    /// <summary>Whether the product isn't bundled and the user added it, so Remove product applies (D12).</summary>
    public bool CanRemoveProduct => !_state.IsBuiltInProduct && HasOrigin(CatalogueOrigin.Custom);

    /// <summary>The first custom or command-line path, for Show in Finder; null when none.</summary>
    public string? RevealPath =>
        new[] { _state.Feature.Source, _state.Portrayal.Source }
            .FirstOrDefault(s => s is { Origin: not CatalogueOrigin.BuiltIn, Path: not null })?.Path;

    /// <summary>Whether there's a path to show in Finder or Explorer.</summary>
    public bool CanReveal => RevealPath is not null;

    /// <summary>Whether the portrayal catalogue is missing: Add portrayal catalogue… is the main action (D12).</summary>
    public bool IsPortrayalMissing => _state.Portrayal.Status == CatalogueStatus.Missing;

    /// <summary>Whether the feature catalogue is missing: Add feature catalogue… is the main action.</summary>
    public bool IsFeatureMissing => _state.Feature.Status == CatalogueStatus.Missing;

    /// <summary>Whether a portrayal catalogue applies to the product (not S-104), for Replace portrayal catalogue….</summary>
    public bool HasPortrayalCatalogue => _state.Portrayal.Status != CatalogueStatus.NotRequired;

    /// <summary>Whether any action row button shows in the details pane.</summary>
    public bool HasDetailsActions => CanReload || CanRevert || CanReveal || CanRemoveProduct || IsPortrayalMissing || IsFeatureMissing;

    /// <summary>Whether a catalogue failed to load, so the details show the error with Show in Logs.</summary>
    public bool HasError => IsError;

    /// <summary>Whether the eXaminer hosts the product's feature catalogue (D9).</summary>
    public bool CanOpenInExaminer => _canOpenInExaminer;

    /// <summary>
    /// "Open in eXaminer", or "Open published edition in eXaminer" when the
    /// feature catalogue in use is custom (D9, audit A9).
    /// </summary>
    public string ExaminerLabel => _state.Feature.Origin is CatalogueOrigin.Custom or CatalogueOrigin.CommandLine
        ? Strings.Catalogues_Menu_OpenPublishedInExaminer
        : Strings.Catalogues_Menu_OpenInExaminer;

    /// <summary>The row menu's accessible name.</summary>
    public string MenuName => string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_RowMenuNameFormat, Title);

    /// <summary>Swaps in the product's new state; raises a change for every property.</summary>
    /// <param name="state">The product's catalogues as in effect.</param>
    /// <param name="loadedCount">How many of its datasets are loaded.</param>
    /// <param name="canOpenInExaminer">Whether the eXaminer hosts the product's feature catalogue.</param>
    public void Update(CatalogueProductState state, int loadedCount, bool canOpenInExaminer)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (Equals(state, _state) && loadedCount == _loadedCount && canOpenInExaminer == _canOpenInExaminer)
            return;
        _state = state;
        _loadedCount = loadedCount;
        _canOpenInExaminer = canOpenInExaminer;
        Feature = new CatalogueDetailsGroupViewModel(state.Feature);
        Portrayal = new CatalogueDetailsGroupViewModel(state.Portrayal);
        OnPropertyChanged(string.Empty);
    }

    /// <summary>Whether the row matches the filter text: its spec, name or declared name.</summary>
    /// <param name="filter">The filter text; empty matches every row.</param>
    public bool Matches(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        var text = filter.Trim();
        return Spec.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Spec.Replace("-", string.Empty, StringComparison.Ordinal).Contains(text, StringComparison.OrdinalIgnoreCase)
            || (Name?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
            || (_state.DeclaredName?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// Composes the primary list line: the spec code followed by the
    /// product name (e.g. "S-101 Electronic Navigational Chart"), or just
    /// the spec code when no name is available.
    /// </summary>
    internal static string ComposeTitle(string spec, string? name) =>
        string.IsNullOrWhiteSpace(name) || string.Equals(name, spec, StringComparison.OrdinalIgnoreCase) ? spec : $"{spec} {name}";

    private int ErrorCount =>
        (_state.Feature.Status == CatalogueStatus.Error ? 1 : 0) + (_state.Portrayal.Status == CatalogueStatus.Error ? 1 : 0);

    private bool HasOrigin(CatalogueOrigin origin) =>
        _state.Feature.Origin == origin || _state.Portrayal.Origin == origin;

    private static string Describe(CatalogueState state) => state.Status switch
    {
        CatalogueStatus.NotRequired => Strings.Catalogues_Row_DrawnBySoundCharts,
        CatalogueStatus.Missing => state.Kind == CatalogueKind.Feature ? Strings.Catalogues_Row_FeatureMissing : Strings.Catalogues_Row_PortrayalMissing,
        _ => string.Format(
            CultureInfo.CurrentCulture,
            state.Kind == CatalogueKind.Feature ? Strings.Catalogues_Row_FeatureFormat : Strings.Catalogues_Row_PortrayalFormat,
            // The configured catalogue's version; when it can't be read, the one drawing instead.
            CatalogueText.Version(state.Source?.Metadata is not null ? state.Source : state.Drawing ?? state.Source)),
    };
}

/// <summary>How a row's tag is drawn.</summary>
internal enum CatalogueTagKind
{
    /// <summary>Custom, Command line, Not built in.</summary>
    Neutral,

    /// <summary>A problem count: red.</summary>
    Error,

    /// <summary>Missing: amber.</summary>
    Warning,
}

/// <summary>A tag on a Catalogues row (D4).</summary>
/// <param name="Text">The tag's text.</param>
/// <param name="Kind">How it's drawn.</param>
internal sealed record CatalogueTagViewModel(string Text, CatalogueTagKind Kind)
{
    /// <summary>Drawn red.</summary>
    public bool IsError => Kind == CatalogueTagKind.Error;

    /// <summary>Drawn amber.</summary>
    public bool IsWarning => Kind == CatalogueTagKind.Warning;
}

/// <summary>One catalogue's fields in the details pane (§6.1): version, source, path, what it overrides, its error.</summary>
internal sealed class CatalogueDetailsGroupViewModel
{
    private readonly CatalogueState _state;

    /// <summary>Creates the group for <paramref name="state"/>.</summary>
    /// <param name="state">The catalogue as in effect.</param>
    public CatalogueDetailsGroupViewModel(CatalogueState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <summary>FEATURE CATALOGUE or PORTRAYAL CATALOGUE.</summary>
    public string Header => _state.Kind == CatalogueKind.Feature
        ? Strings.Catalogues_Details_FeatureHeader
        : Strings.Catalogues_Details_PortrayalHeader;

    /// <summary>Whether a catalogue is configured, so Version and Source show.</summary>
    public bool HasSource => _state.Source is not null;

    /// <summary>"2.0.0 · 2024-10-16".</summary>
    public string VersionText => CatalogueText.VersionAndDate(_state.Source);

    /// <summary>Built-in, Custom, Custom, this session, or Command line, this session.</summary>
    public string SourceText => _state.Source is null ? string.Empty : CatalogueText.Origin(_state.Source.Origin, _state.KeptAfterRestart);

    /// <summary>The custom or command-line path, with the home folder as <c>~</c>; null for built-in.</summary>
    public string? PathText => HomePathRedactor.RedactOrNull(_state.Source?.Path);

    /// <summary>The full path, for the tooltip; null for built-in.</summary>
    public string? FullPath => _state.Source?.Path;

    /// <summary>Whether <see cref="PathText"/> shows.</summary>
    public bool HasPath => _state.Source?.Path is not null;

    /// <summary>"Built-in 2.0.0", what the catalogue in use overrides; null when nothing.</summary>
    public string? OverridesText => _state.Overrides is { } overridden ? CatalogueText.Describe(overridden, capitalise: true) : null;

    /// <summary>Whether <see cref="OverridesText"/> shows.</summary>
    public bool HasOverrides => _state.Overrides is not null;

    /// <summary>Missing or Drawn by SoundCharts; null otherwise.</summary>
    public string? StatusText => _state.Status switch
    {
        CatalogueStatus.Missing => Strings.Catalogues_Status_Missing,
        CatalogueStatus.NotRequired => Strings.Catalogues_Status_NotRequired,
        _ => null,
    };

    /// <summary>Whether <see cref="StatusText"/> shows.</summary>
    public bool HasStatus => StatusText is not null;

    /// <summary>Whether the status is Missing: drawn amber.</summary>
    public bool IsMissing => _state.Status == CatalogueStatus.Missing;

    /// <summary>The reason it failed and what draws instead (D4); null unless it's in error.</summary>
    public string? ErrorText => _state.Status != CatalogueStatus.Error
        ? null
        : _state.Drawing is { } drawing
            ? string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Details_ErrorUsingFormat, _state.Error, CatalogueText.Describe(drawing, capitalise: false))
            : string.Format(CultureInfo.CurrentCulture, Strings.Catalogues_Details_ErrorNothingFormat, _state.Error);

    /// <summary>Whether <see cref="ErrorText"/> shows.</summary>
    public bool HasError => ErrorText is not null;
}

/// <summary>How catalogue sources read in the Catalogues activity, its sheet and its toasts.</summary>
internal static class CatalogueText
{
    /// <summary>The version of <paramref name="source"/>, or "?" when it can't be read.</summary>
    public static string Version(CatalogueSourceInfo? source) =>
        source?.Metadata?.Version ?? Strings.Catalogues_Row_UnknownVersion;

    /// <summary>"2.0.0 · 2024-10-16", or the version alone.</summary>
    public static string VersionAndDate(CatalogueSourceInfo? source) =>
        source?.Metadata?.VersionDate is { Length: > 0 } date ? $"{Version(source)} · {date}" : Version(source);

    /// <summary>Built-in, Custom, Custom, this session, or Command line, this session.</summary>
    public static string Origin(CatalogueOrigin origin, bool keptAfterRestart) => origin switch
    {
        CatalogueOrigin.BuiltIn => Strings.Catalogues_Origin_BuiltIn,
        CatalogueOrigin.Custom => keptAfterRestart ? Strings.Catalogues_Origin_Custom : Strings.Catalogues_Origin_CustomSession,
        _ => Strings.Catalogues_Origin_CommandLine,
    };

    /// <summary>"built-in 2.0.0", or "Built-in 2.0.0" at the start of a line.</summary>
    public static string Describe(CatalogueSourceInfo source, bool capitalise)
    {
        var origin = Lower(source.Origin);
        if (capitalise && origin.Length > 0)
            origin = char.ToUpper(origin[0], CultureInfo.CurrentCulture) + origin[1..];
        return source.Metadata?.Version is { } version ? $"{origin} {version}" : origin;
    }

    /// <summary>built-in, custom or command line.</summary>
    public static string Lower(CatalogueOrigin origin) => origin switch
    {
        CatalogueOrigin.BuiltIn => Strings.Catalogues_Describe_BuiltIn,
        CatalogueOrigin.Custom => Strings.Catalogues_Describe_Custom,
        _ => Strings.Catalogues_Describe_CommandLine,
    };

    /// <summary>"feature catalogue 2.0.0" or "portrayal catalogue 2.1.0-draft", for the middle of a sentence.</summary>
    public static string KindAndVersion(CatalogueKind kind, string? version) => string.Format(
        CultureInfo.CurrentCulture,
        kind == CatalogueKind.Feature ? Strings.Catalogues_Toast_FeatureFormat : Strings.Catalogues_Toast_PortrayalFormat,
        version ?? Strings.Catalogues_Row_UnknownVersion);

    /// <summary>"Feature catalogue 2.0.0" or "Portrayal catalogue 2.1.0-draft", at the start of a line.</summary>
    public static string KindAndVersionTitle(CatalogueKind kind, string? version) => string.Format(
        CultureInfo.CurrentCulture,
        kind == CatalogueKind.Feature ? Strings.Catalogues_Sheet_FeatureFormat : Strings.Catalogues_Sheet_PortrayalFormat,
        version ?? Strings.Catalogues_Row_UnknownVersion);
}
