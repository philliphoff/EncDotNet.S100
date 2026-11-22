using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.Services.Legends;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A feature type's entry (design note §4.4, C1; R7, #1650): its definition,
/// codes, display categories by geometry or case (D6), Chart 1 section,
/// In view with Find, and Drawn with.
/// </summary>
/// <remarks>
/// Until rule probing (R13) gives the variant table, <b>Drawn with</b> stands
/// in for it: each item the feature type's own rule draws, with its cases in
/// words. Items drawn only through a shared rule (a topmark, a light) are
/// links, not rows, and the catalogue's fallback <c>?</c> has its own line.
/// </remarks>
internal sealed class FeatureTypeEntryViewModel : ReferenceEntryViewModel
{
    private const int MaxCases = 6;

    private readonly FeatureTypeEntry _feature;
    private readonly ChartReferenceTarget? _target;
    private int _inViewCount;
    private bool _found;

    /// <summary>Creates the entry view.</summary>
    /// <param name="feature">The feature type.</param>
    /// <param name="context">The host.</param>
    public FeatureTypeEntryViewModel(FeatureTypeEntry feature, ReferenceEntryContext context)
        : base(feature, context, LeadGlyph.For(feature, context.Index, context.SimplifiedSymbols))
    {
        _feature = feature;
        _target = context.Map is null ? null : ReferenceTargets.Of(feature);
        CodeLine = feature.Alias is { Length: > 0 } alias
            ? string.Format(CultureInfo.CurrentCulture, Strings.Reference_CodeWithAcronym, feature.Code, alias)
            : feature.Code;

        var group = feature.PrimaryViewingGroup;
        Categories = CategoriesOf(feature);
        Section = ChartReferenceSection.Of(group)?.Title ?? (group is null ? null : Strings.ChartReference_NoSection);
        ViewingGroup = group is null
            ? null
            : group.Name is { Length: > 0 } name
                ? string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_ViewingGroupFormat, group.Id, name)
                : group.Id;

        var (own, shared, fallback) = DrawnWithOf(feature);
        DrawnWith = own;
        DrawnByShared = shared;
        Fallback = fallback;
        FindCommand = new RelayCommand(Find, () => CanFind);
    }

    /// <inheritdoc />
    public override string CodeLine { get; }

    /// <inheritdoc />
    public override string KindLabel => Strings.Reference_Kind_FeatureType;

    /// <summary>The display categories, one line per geometry or case when they differ (D6).</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>Whether there are display categories.</summary>
    public bool HasCategories => Categories.Count > 0;

    /// <summary>The Chart 1 section, or <see langword="null"/>.</summary>
    public string? Section { get; }

    /// <summary>Whether there is a Chart 1 section.</summary>
    public bool HasSection => Section is not null;

    /// <summary>The viewing group's id and name, or <see langword="null"/>.</summary>
    public string? ViewingGroup { get; }

    /// <summary>Whether there is a viewing group.</summary>
    public bool HasViewingGroup => ViewingGroup is not null;

    /// <summary>The items the feature type's own rule draws, each with its cases in words.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> DrawnWith { get; }

    /// <summary>Whether its own rule draws anything listed.</summary>
    public bool HasDrawnWith => DrawnWith.Count > 0;

    /// <summary>Items drawn only through a shared rule (a topmark, a light): links, without cases.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> DrawnByShared { get; }

    /// <summary>Whether a shared rule draws anything for it.</summary>
    public bool HasDrawnByShared => DrawnByShared.Count > 0;

    /// <summary>The catalogue's fallback symbol, drawn when no case fits, or <see langword="null"/>.</summary>
    public ChartReferenceLinkViewModel? Fallback { get; }

    /// <summary>Whether the rule can fall back to the catalogue's <c>?</c>.</summary>
    public bool HasFallback => Fallback is not null;

    /// <summary>Whether the entry shows In view: there is a map.</summary>
    public bool CanShowInView => _target is not null;

    /// <summary>How many features of the type are drawn in the map view.</summary>
    public int InViewCount
    {
        get => _inViewCount;
        private set
        {
            if (!SetProperty(ref _inViewCount, value))
                return;
            OnPropertyChanged(nameof(InViewText));
            OnPropertyChanged(nameof(CanFind));
            (FindCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }

    /// <summary>"3 in view", "1 in view" or "None in view".</summary>
    public string InViewText => _inViewCount switch
    {
        0 => Strings.Reference_NoneInView,
        1 => Strings.Reference_InViewOne,
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Reference_InViewCount, _inViewCount),
    };

    /// <summary>Whether Find has features to ring.</summary>
    public bool CanFind => _inViewCount > 0;

    /// <summary>Rings the features in view on the map.</summary>
    public ICommand FindCommand { get; }

    /// <inheritdoc />
    public override void RefreshInView()
    {
        if (_target is null || Context.Map is not { } map)
            return;
        try
        {
            InViewCount = map.CountInView(_target);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // The stack changed under the read: the count waits for the next map change.
        }
    }

    /// <inheritdoc />
    public override void Close()
    {
        if (_found && _target is not null)
            Context.Map?.Leave(_target);
        _found = false;
    }

    private void Find()
    {
        if (_target is null || Context.Map is not { } map)
            return;
        map.Find(_target);
        _found = true;
    }

    // D6: one line per display category, by geometry or case.
    private static List<string> CategoriesOf(FeatureTypeEntry feature)
    {
        var own = feature.ViewingGroups.Where(u => u.ViaRule is null).ToList();
        var uses = own.Count > 0 ? own : feature.ViewingGroups;
        var pairs = uses
            .Select(u => (
                Geometry: u.Conditions.FirstOrDefault(c => c is { Kind: RuleConditionKind.Primitive, Operator: ConditionOperator.Equal })?.Value,
                Mode: u.ViewingGroup.DisplayModeName ?? u.ViewingGroup.DisplayModeId))
            .Where(p => p.Mode is not null)
            .Distinct()
            .ToList();
        var modes = pairs.Select(p => p.Mode!).Distinct(StringComparer.Ordinal).ToList();
        if (modes.Count <= 1)
            return modes;

        return
        [
            .. pairs
                .GroupBy(p => p.Geometry, StringComparer.OrdinalIgnoreCase)
                .Select(g => string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.ChartReference_CategoryByGeometry,
                    g.Key is null ? Strings.ChartReference_OtherCases : Capitalise(RuleConditionText.Geometry(g.Key)),
                    string.Join(" / ", g.Select(p => p.Mode).Distinct(StringComparer.Ordinal)))),
        ];
    }

    // Drawn with: each item the rule draws, once, with its cases in words;
    // shared-rule items and the fallback apart.
    private (List<ChartReferenceLinkViewModel> Own, List<ChartReferenceLinkViewModel> Shared, ChartReferenceLinkViewModel? Fallback) DrawnWithOf(FeatureTypeEntry feature)
    {
        bool geometry = feature.PermittedPrimitives.Count != 1;
        var own = new List<ChartReferenceLinkViewModel>();
        var shared = new List<ChartReferenceLinkViewModel>();
        ChartReferenceLinkViewModel? fallback = null;
        foreach (var g in feature.Uses.GroupBy(u => (u.ItemKind, u.ItemId)))
        {
            var key = new ReferenceKey(Index.Spec, g.Key.ItemKind, g.Key.ItemId);
            if (LeadGlyph.DefaultSymbols.Contains(g.Key.ItemId))
            {
                fallback ??= Link(key, [], Strings.ChartReference_DefaultSymbolNote);
                continue;
            }

            if (g.All(u => u.ViaRule is not null))
            {
                var via = string.Join(", ", g.Select(u => u.ViaRule).OfType<string>().Distinct(StringComparer.Ordinal));
                shared.Add(Link(key, [], string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_ViaRule, via)));
                continue;
            }

            own.Add(Link(key, CasesOf(g, geometry), null));
        }

        return (own, shared, fallback);
    }

    /// <summary>The cases of a set of uses in words, each once, up to six and then "and N more cases".</summary>
    /// <param name="uses">The uses.</param>
    /// <param name="index">The index, for attribute and value names.</param>
    /// <param name="geometry">Whether to say the geometry a case tests.</param>
    internal static List<string> CasesOf(IEnumerable<RuleUse> uses, ChartReferenceIndex index, bool geometry)
    {
        var cases = uses
            .Select(u => RuleConditionText.Describe(u.Conditions, index, geometry))
            .Select(text => text.Length == 0 ? Strings.ChartReference_Always : text)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (cases.Count <= MaxCases)
            return cases;
        var more = cases.Count - MaxCases;
        return [.. cases.Take(MaxCases), string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_MoreCases, more)];
    }

    private List<string> CasesOf(IEnumerable<SymbolUse> uses, bool geometry) => CasesOf(uses, Index, geometry);

    // An item without an entry of its own (an unknown id) still shows, by its id.
    private ChartReferenceLinkViewModel Link(ReferenceKey key, IReadOnlyList<string> cases, string? note) =>
        LinkTo(key, cases, note) ?? new ChartReferenceLinkViewModel(key, key.Code, cases, note, Context.Open);

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
