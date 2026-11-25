using System.Globalization;
using System.Text.RegularExpressions;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Cli.Reference;

/// <summary>
/// What an exported chart reference page says about an entry (R14, #1723),
/// as the viewer's Reference tab says it: cases in words through
/// <see cref="RuleConditionText"/>, display categories by geometry (D6),
/// Chart 1 sections, and where a symbol came from.
/// </summary>
internal static partial class ReferenceWords
{
    /// <summary>The cases a Drawn with item or a Used by row lists before "and N more cases".</summary>
    public const int MaxCases = 12;

    /// <summary>
    /// The cases of a set of uses in words, each once, up to
    /// <paramref name="max"/>: conditions every case shares are said once,
    /// first, as "When simplified symbols is on:", and cases that differ only
    /// by a display setting being on or off are one (#1650).
    /// </summary>
    /// <param name="uses">The uses.</param>
    /// <param name="index">The index, for attribute and value names.</param>
    /// <param name="geometry">Whether to say the geometry a case tests.</param>
    /// <param name="max">The most cases to list.</param>
    public static List<TermText> CasesOf(IEnumerable<RuleUse> uses, ChartReferenceIndex index, bool geometry, int max = MaxCases)
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
                lead.Add(TermText.Format("When {0}:", shared.LowerFirst()));
                cases =
                [
                    .. parts
                        .Select(p => TermText.Join(RuleConditionText.Separator, p.Where(x => !common.Contains(x))))
                        .Select(c => c.IsEmpty ? TermText.Plain("With no further condition") : c)
                        .Distinct(),
                ];
            }
        }

        cases = [.. cases.Select(c => c.IsEmpty ? TermText.Plain("Always") : c)];
        if (cases.Count > max)
        {
            var more = cases.Count - max;
            cases = [.. cases.Take(max), TermText.Plain(string.Create(CultureInfo.InvariantCulture, $"and {more} more cases"))];
        }

        return [.. lead, .. cases];
    }

    /// <summary>The display categories of a feature type, one line per geometry or case when they differ (D6).</summary>
    /// <param name="feature">The feature type.</param>
    public static List<string> CategoriesOf(FeatureTypeEntry feature)
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
                .Select(g => (g.Key is null ? "Other cases" : Capitalise(RuleConditionText.Geometry(g.Key)))
                    + ": " + string.Join(" / ", g.Select(p => p.Mode).Distinct(StringComparer.Ordinal))),
        ];
    }

    /// <summary>
    /// The Chart 1 section a viewing group's description names, such as
    /// "P, Q, R, S – Buoys and Beacons, Lights, Fog signals, RADAR".
    /// </summary>
    /// <param name="group">The viewing group.</param>
    /// <returns>The section, or <see langword="null"/>.</returns>
    public static string? SectionOf(ViewingGroupInfo? group)
    {
        if (group?.Description is not { Length: > 0 } description)
            return null;
        var match = SectionPattern().Match(description.Trim());
        if (!match.Success)
            return null;
        var letters = string.Join(", ", match.Groups["letters"].Value.Split(',', StringSplitOptions.TrimEntries));
        var name = match.Groups["name"].Value.Trim();
        return name.Length > 0 ? $"{letters} – {name}" : letters;
    }

    /// <summary>Whether a symbol comes from the S-52 Presentation Library.</summary>
    /// <param name="item">The item.</param>
    public static bool IsNew(PortrayalItemEntry item) => item.Source is { Length: > 0 } source && !IsPresentationLibrary(source);

    /// <summary>Where a symbol came from, from its <c>iho:source</c>.</summary>
    /// <param name="item">The item.</param>
    /// <param name="spec">The product, such as <c>S-101</c>.</param>
    /// <returns>The origin, or <see langword="null"/> when the catalogue doesn't say.</returns>
    public static string? OriginOf(PortrayalItemEntry item, string spec) => item.Source switch
    {
        null or "" => null,
        var source when IsPresentationLibrary(source) => $"S-52 Presentation Library ({source})",
        var source when string.Equals(source, "new", StringComparison.OrdinalIgnoreCase) => $"New in {spec}",
        var source => $"New in {spec}, from {source}",
    };

    /// <summary>The kind of entry, as a page heading names it.</summary>
    /// <param name="entry">The entry.</param>
    public static string KindOf(ReferenceEntry entry) => entry switch
    {
        PortrayalItemEntry { IsFamily: true } => "Symbol family",
        _ => KindOf(entry.Key.Kind),
    };

    /// <summary>The kind of entry, as a heading names it.</summary>
    /// <param name="kind">The kind.</param>
    public static string KindOf(ReferenceKind kind) => kind switch
    {
        ReferenceKind.FeatureType => "Feature type",
        ReferenceKind.Symbol => "Symbol",
        ReferenceKind.LineStyle => "Line style",
        ReferenceKind.AreaFill => "Area fill",
        ReferenceKind.Attribute => "Attribute",
        _ => "Display setting",
    };

    /// <summary>The kinds, as the index's group headings name them.</summary>
    /// <param name="kind">The kind.</param>
    public static string GroupOf(ReferenceKind kind) => kind switch
    {
        ReferenceKind.FeatureType => "Feature types",
        ReferenceKind.Symbol => "Symbols",
        ReferenceKind.LineStyle => "Line styles",
        ReferenceKind.AreaFill => "Area fills",
        ReferenceKind.Attribute => "Attributes",
        _ => "Display settings",
    };

    /// <summary>A display setting's value for the mariner: booleans as On and Off.</summary>
    /// <param name="value">The value as the catalogue gives it.</param>
    public static string SettingValue(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true" => "On",
        "false" => "Off",
        null => string.Empty,
        _ => value,
    };

    /// <summary>"point" as "Point".</summary>
    /// <param name="text">The text.</param>
    public static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];

    /// <summary>Whether a use is drawn only when Simplified symbols is off.</summary>
    /// <param name="use">The use.</param>
    public static bool IsPaperChart(RuleUse use) =>
        use.Conditions.Any(c =>
            c.Kind == RuleConditionKind.ContextParameter
            && string.Equals(c.Subject, "SimplifiedSymbols", StringComparison.OrdinalIgnoreCase)
            && (c.Operator == ConditionOperator.IsFalse
                || c.Operator == ConditionOperator.Equal && string.Equals(c.Value, "false", StringComparison.OrdinalIgnoreCase)));

    // Merges cases that differ only by a display setting being on in one and
    // off in the other: the item is drawn either way.
    private static List<TermText> MergeOnOff(List<List<TermText>> cases)
    {
        const string on = " is on";
        const string off = " is off";
        bool merged;
        do
        {
            merged = false;
            for (var i = 0; i < cases.Count && !merged; i++)
            {
                foreach (var part in cases[i])
                {
                    var text = part.Text;
                    if (!text.EndsWith(on, StringComparison.Ordinal))
                        continue;
                    var offText = text[..^on.Length] + off;
                    var other = cases[i].Select(p => p == part ? offText : p.Text).ToList();
                    var j = cases.FindIndex(c => c.Select(p => p.Text).SequenceEqual(other, StringComparer.Ordinal));
                    if (j < 0)
                        continue;
                    cases[i] = [.. cases[i].Where(p => p != part)];
                    cases.RemoveAt(j);
                    merged = true;
                    break;
                }
            }
        }
        while (merged);

        return [.. cases.Select(c => TermText.Join(RuleConditionText.Separator, c)).Distinct()];
    }

    private static bool IsPresentationLibrary(string source) =>
        source.StartsWith("S52", StringComparison.OrdinalIgnoreCase);

    // An optional display category ("Base:", "Standard:", "Other:"), then
    // capital letters separated by commas, then an optional "-" and a name.
    [GeneratedRegex(@"^(?:[A-Za-z ]+:\s*)?(?<letters>[A-Z](?:\s*,\s*[A-Z])*)(?![A-Za-z])\s*-?\s*(?<name>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SectionPattern();
}
