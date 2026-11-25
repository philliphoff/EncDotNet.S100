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
    /// <summary>
    /// The cases a Drawn with item shows before "and N more cases", so a long
    /// entry fits the 360 px dock; the symbol's own entry lists them all.
    /// </summary>
    internal const int DrawnWithCases = 3;

    /// <summary>The cases a symbol entry's Used by lists before "and N more cases".</summary>
    internal const int UsedByCases = 8;

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
        DrawnWith = [.. own.Where(o => !o.PaperChart).Select(o => o.Link)];
        PaperChart = [.. own.Where(o => o.PaperChart).Select(o => o.Link)];
        DrawnByShared = shared;
        Fallback = fallback;
        FindCommand = new RelayCommand(Find, () => CanFind);

        // How it is drawn (R13b, #1721): the variant table, when the rule was probed and draws something.
        if (context.Index.Probes?.Find(feature.Code) is { } probe && probe.Outcomes.Any(o => o.Parts.Count > 0))
            Variants = new VariantTableViewModel(feature, probe, context);
        IsWorkingOutVariants = Variants is null && context.ProbesPending;
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

    /// <summary>
    /// How it is drawn as a table of variants, from the rule probe results
    /// (R13b, #1721); <see langword="null"/> for a product without them (an
    /// XSLT product, or one still being probed), which keeps <see cref="DrawnWith"/>.
    /// </summary>
    public VariantTableViewModel? Variants { get; }

    /// <summary>Whether the entry shows the variant table.</summary>
    public bool HasVariants => Variants is not null;

    /// <summary>Whether the entry shows Drawn with: there is no variant table, and its own rule draws something listed.</summary>
    public bool ShowsDrawnWith => !HasVariants && HasDrawnWith;

    /// <summary>Whether the probe results are being worked out in the background: "Working out variants…".</summary>
    public bool IsWorkingOutVariants { get; }

    /// <summary>The items the feature type's own rule draws, each with its cases in words.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> DrawnWith { get; }

    /// <summary>Whether its own rule draws anything listed.</summary>
    public bool HasDrawnWith => DrawnWith.Count > 0 || PaperChart.Count > 0;

    /// <summary>
    /// The items drawn only when Simplified symbols is off, folded under
    /// <see cref="PaperChartHeader"/> so the simplified and always-drawn
    /// items come first.
    /// </summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> PaperChart { get; }

    /// <summary>Whether there are paper-chart items to fold.</summary>
    public bool HasPaperChart => PaperChart.Count > 0;

    /// <summary>"Paper chart symbols (15)".</summary>
    public string PaperChartHeader => Counted(Strings.Reference_PaperChartSymbols, PaperChart.Count);

    /// <summary>"Drawn by shared rules (30)", the folded header of <see cref="DrawnByShared"/>.</summary>
    public string DrawnBySharedHeader => Counted(Strings.Reference_DrawnByShared, DrawnByShared.Count);

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
    protected override void OnStart() => Variants?.Start();

    /// <inheritdoc />
    protected override void OnReloadGlyphs() => Variants?.ReloadGlyphs();

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
    private (List<(ChartReferenceLinkViewModel Link, bool PaperChart)> Own, List<ChartReferenceLinkViewModel> Shared, ChartReferenceLinkViewModel? Fallback) DrawnWithOf(FeatureTypeEntry feature)
    {
        bool geometry = feature.PermittedPrimitives.Count != 1;
        var own = new List<(ChartReferenceLinkViewModel, bool)>();
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

            own.Add((Link(key, CasesOf(g, geometry), null), g.All(IsPaperChart)));
        }

        return (own, shared, fallback);
    }

    /// <summary>
    /// The cases of a set of uses in words, each once, up to
    /// <paramref name="max"/> and then "and N more cases". Conditions every case shares are said once, first,
    /// as "When simplified symbols is on:", so each case shows only what
    /// differs (#1650).
    /// </summary>
    /// <param name="uses">The uses.</param>
    /// <param name="index">The index, for attribute and value names.</param>
    /// <param name="geometry">Whether to say the geometry a case tests.</param>
    /// <param name="max">The most cases to list.</param>
    internal static List<TermText> CasesOf(IEnumerable<RuleUse> uses, ChartReferenceIndex index, bool geometry, int max)
    {
        var cases = MergeOnOff(uses
            .Select(u => RuleConditionText.DescribeParts(u.Conditions, index, geometry).ToList())
            .DistinctBy(c => TermText.Join(RuleConditionText.Separator, c))
            .ToList());

        var lead = new List<TermText>();
        if (cases.Count > 1)
        {
            var parts = cases.Select(c => c.Split(RuleConditionText.Separator)).ToList();
            var common = parts[0].Where(p => parts.All(other => other.Contains(p))).ToList();
            if (common.Count > 0)
            {
                var shared = TermText.Join(RuleConditionText.Separator, common);
                lead.Add(TermText.Format(Strings.Reference_CasesWhen, shared.LowerFirst()));
                cases =
                [
                    .. parts
                        .Select(p => TermText.Join(RuleConditionText.Separator, p.Where(x => !common.Contains(x))))
                        .Select(c => c.IsEmpty ? Strings.Reference_NoFurtherCondition : c)
                        .Distinct(),
                ];
            }
        }

        cases = [.. cases.Select(c => c.IsEmpty ? Strings.ChartReference_Always : c)];
        if (cases.Count > max)
        {
            var more = cases.Count - max;
            cases = [.. cases.Take(max), string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_MoreCases, more)];
        }

        return [.. lead, .. cases];
    }

    /// <summary>
    /// Merges cases that differ only by a display setting being on in one and
    /// off in the other, as Cairn's "Simplified symbols is on · Beacon shape
    /// is a cairn" and "… is off · …": the symbol is drawn either way, so the
    /// case reads "Beacon shape is a cairn" (#1650).
    /// </summary>
    /// <param name="cases">Each case's conditions in words.</param>
    /// <returns>The cases, each once.</returns>
    internal static List<TermText> MergeOnOff(List<List<TermText>> cases)
    {
        var on = Affix(Strings.ChartReference_Condition_On);
        var off = Affix(Strings.ChartReference_Condition_Off);
        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < cases.Count && !merged; i++)
            {
                foreach (var part in cases[i])
                {
                    var text = part.Text;
                    if (!text.StartsWith(on.Prefix, StringComparison.Ordinal) || !text.EndsWith(on.Suffix, StringComparison.Ordinal))
                        continue;
                    var name = text[on.Prefix.Length..^on.Suffix.Length];
                    var offText = off.Prefix + name + off.Suffix;
                    var other = cases[i].Select(p => p == part ? offText : p.Text).ToList();
                    int j = cases.FindIndex(c => c.Select(p => p.Text).SequenceEqual(other, StringComparer.Ordinal));
                    if (j < 0)
                        continue;
                    var either = cases[i].Where(p => p != part).ToList();
                    cases[i] = either;
                    cases.RemoveAt(j);
                    merged = true;
                    break;
                }
            }
        }
        while (merged);

        return [.. cases.Select(c => TermText.Join(RuleConditionText.Separator, c)).Distinct()];
    }

    // The text around {0} in "{0} is on".
    private static (string Prefix, string Suffix) Affix(string format)
    {
        int at = format.IndexOf("{0}", StringComparison.Ordinal);
        return at < 0 ? (format, string.Empty) : (format[..at], format[(at + 3)..]);
    }

    // "Simplified symbols is on" after "When" reads "simplified symbols is on";
    // an acronym keeps its capitals.
    private static string LowerFirst(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
            ? char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..]
            : text;

    private List<TermText> CasesOf(IEnumerable<SymbolUse> uses, bool geometry) => CasesOf(uses, Index, geometry, DrawnWithCases);

    // An item without an entry of its own (an unknown id) still shows, by its id, as plain text.
    private ChartReferenceLinkViewModel Link(ReferenceKey key, IReadOnlyList<TermText> cases, string? note) =>
        LinkTo(key, cases, note) ?? new ChartReferenceLinkViewModel(key, key.Code, cases, note, Context.Open) { IsKnown = false };

    // A use drawn only when Simplified symbols is off.
    private static bool IsPaperChart(RuleUse use) =>
        use.Conditions.Any(c =>
            c.Kind == RuleConditionKind.ContextParameter
            && string.Equals(c.Subject, "SimplifiedSymbols", StringComparison.OrdinalIgnoreCase)
            && (c.Operator == ConditionOperator.IsFalse
                || c.Operator == ConditionOperator.Equal && string.Equals(c.Value, "false", StringComparison.OrdinalIgnoreCase)));

    private static string Counted(string heading, int count) =>
        string.Format(CultureInfo.CurrentCulture, Strings.Reference_HeadingCount, heading, count);

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
