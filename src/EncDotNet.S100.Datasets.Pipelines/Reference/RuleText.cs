using System.Globalization;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// Finds the drawing-instruction vocabulary in a piece of rule text: a Lua
/// string literal or an XSLT text node. Shared by both readers.
/// </summary>
internal static partial class RuleText
{
    [GeneratedRegex(@"[A-Za-z0-9_]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"ViewingGroup:(\d+)")]
    private static partial Regex ViewingGroupPattern();

    [GeneratedRegex(@"ColorFill:([A-Za-z]+)")]
    private static partial Regex ColorFillPattern();

    [GeneratedRegex(@"LineStyle:_simple_,(\w*),([\d.]+),([A-Za-z]+)")]
    private static partial Regex SimpleLinePattern();

    [GeneratedRegex(@"(?:Symbols|LineStyles|AreaFills)/([\w-]+)\.(?:svg|xml)")]
    private static partial Regex ItemPathPattern();

    /// <summary>
    /// Adds the facts <paramref name="text"/> names, under
    /// <paramref name="conditions"/>, to <paramref name="facts"/>.
    /// </summary>
    /// <param name="text">The rule text.</param>
    /// <param name="vocabulary">The catalogue vocabulary to match.</param>
    /// <param name="conditions">The conditions that lead to the text.</param>
    /// <param name="line">The 1-based line of the text.</param>
    /// <param name="owner">The feature type an XSLT template matches, or <see langword="null"/>.</param>
    /// <param name="facts">Receives the facts.</param>
    /// <param name="kindHint">
    /// The item kind the surrounding markup implies, such as
    /// <see cref="ReferenceKind.LineStyle"/> for an XSLT
    /// <c>lineStyleReference</c>, or <see langword="null"/>.
    /// </param>
    public static void Scan(
        string text,
        RuleVocabulary vocabulary,
        IReadOnlyList<RuleCondition> conditions,
        int line,
        string? owner,
        List<RuleFact> facts,
        ReferenceKind? kindHint = null)
    {
        if (text.Length == 0) return;

        foreach (Match m in WordPattern().Matches(text))
        {
            if (!vocabulary.Items.TryGetValue(m.Value, out var kinds)) continue;
            var kind = InstructionKind(text, m.Index) ?? kindHint;
            if (kind is { } k && kinds.Contains(k))
            {
                facts.Add(new RuleFact(RuleFactKind.Item, m.Value, conditions, line, owner) { ItemKind = k });
            }
            else
            {
                foreach (var each in kinds)
                {
                    facts.Add(new RuleFact(RuleFactKind.Item, m.Value, conditions, line, owner) { ItemKind = each });
                }
            }
        }

        foreach (Match m in ItemPathPattern().Matches(text))
        {
            if (vocabulary.ItemsByFileStem.TryGetValue(m.Groups[1].Value, out var item))
            {
                facts.Add(new RuleFact(RuleFactKind.Item, item.Id, conditions, line, owner) { ItemKind = item.Kind });
            }
        }

        // A literal that ends with a family prefix, such as 'SOUNDG' or
        // 'PointInstruction:SAFCON', is the start of an assembled id.
        var colon = text.LastIndexOf(':');
        var tail = colon >= 0 ? text[(colon + 1)..] : text;
        if (vocabulary.FamilyPrefixes.Contains(tail))
        {
            facts.Add(new RuleFact(RuleFactKind.Family, tail, conditions, line, owner));
        }

        if (text.Contains("ViewingGroup:", StringComparison.Ordinal))
        {
            foreach (Match m in ViewingGroupPattern().Matches(text))
            {
                facts.Add(new RuleFact(RuleFactKind.ViewingGroup, m.Groups[1].Value, conditions, line, owner));
            }
        }

        if (text.Contains("ColorFill:", StringComparison.Ordinal))
        {
            foreach (Match m in ColorFillPattern().Matches(text))
            {
                facts.Add(new RuleFact(RuleFactKind.ColorFill, m.Groups[1].Value, conditions, line, owner));
            }
        }

        if (text.Contains("_simple_,", StringComparison.Ordinal))
        {
            foreach (Match m in SimpleLinePattern().Matches(text))
            {
                facts.Add(Pen(m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value, conditions, line, owner));
            }
        }
    }

    /// <summary>
    /// The item kind a Part 9a instruction before <paramref name="index"/>
    /// implies: <c>PointInstruction:</c> a symbol, <c>LineInstruction:</c>
    /// a line style, <c>AreaFillReference:</c> an area fill.
    /// </summary>
    private static ReferenceKind? InstructionKind(string text, int index)
    {
        var colon = text.LastIndexOf(':', Math.Max(0, index - 1));
        if (colon < 0 || colon >= index) return null;
        var semicolon = colon > 0 ? text.LastIndexOf(';', colon - 1) : -1;
        var instruction = text.AsSpan(semicolon + 1, colon - semicolon - 1);
        if (instruction.StartsWith("Point", StringComparison.Ordinal)) return ReferenceKind.Symbol;
        if (instruction.StartsWith("Line", StringComparison.Ordinal)) return ReferenceKind.LineStyle;
        if (instruction.StartsWith("AreaFill", StringComparison.Ordinal)) return ReferenceKind.AreaFill;
        return null;
    }

    /// <summary>Creates a <see cref="RuleFactKind.Pen"/> fact.</summary>
    public static RuleFact Pen(
        string dash,
        string width,
        string token,
        IReadOnlyList<RuleCondition> conditions,
        int line,
        string? owner) =>
        new(RuleFactKind.Pen, token, conditions, line, owner)
        {
            Dash = string.IsNullOrEmpty(dash) ? "solid" : dash,
            Width = double.TryParse(width, NumberStyles.Float, CultureInfo.InvariantCulture, out var w) ? w : 0,
        };
}
