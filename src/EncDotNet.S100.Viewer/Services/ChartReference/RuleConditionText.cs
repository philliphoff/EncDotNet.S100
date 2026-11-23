using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Features;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// Puts a rule's conditions into words with the feature catalogue's labels,
/// such as "Simplified symbols is on · Colour is red · Beacon shape is a
/// stake, pole, perch or post" (symbol dictionary design note §4.3, §4.5).
/// </summary>
/// <remarks>
/// Chart reference's Drawn with uses it now; the Reference tab and Object
/// information's Why this symbol will reuse it. The names and values come
/// from the <see cref="ChartReferenceIndex"/>'s attribute and display
/// setting entries, so they follow the catalogue; a code the index doesn't
/// know is shown as it is.
/// </remarks>
internal static partial class RuleConditionText
{
    /// <summary>What separates conditions that must all hold.</summary>
    public const string Separator = " · ";

    /// <summary>
    /// The conditions in words, joined by <see cref="Separator"/>. Equality
    /// tests of one attribute's consecutive indices, as in
    /// <c>colour[1] == 3 and colour[2] == 4</c>, read as one: "Colour is red, green".
    /// </summary>
    /// <param name="conditions">The conditions, outermost first.</param>
    /// <param name="index">The index whose entries name the attributes, values and settings.</param>
    /// <param name="includeGeometry">Whether to include tests of the feature's geometry.</param>
    /// <returns>The sentence, or an empty string when there is nothing to say.</returns>
    public static string Describe(IEnumerable<RuleCondition> conditions, ChartReferenceIndex index, bool includeGeometry = true)
    {
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(index);

        var parts = new List<string>();
        var list = conditions.Where(c => includeGeometry || c.Kind != RuleConditionKind.Primitive).ToList();
        for (int i = 0; i < list.Count; i++)
        {
            var condition = list[i];
            if (IsIndexedEquality(condition))
            {
                // colour[1] == 3, colour[2] == 4, colour[3] == 3: the bands in order.
                var run = new List<RuleCondition> { condition };
                while (i + 1 < list.Count
                    && IsIndexedEquality(list[i + 1])
                    && string.Equals(list[i + 1].Subject, condition.Subject, StringComparison.OrdinalIgnoreCase)
                    && list[i + 1].Index == run[^1].Index + 1)
                {
                    run.Add(list[++i]);
                }

                if (run.Count > 1)
                {
                    var attribute = Attribute(index, condition.Subject);
                    parts.Add(Format(
                        Strings.ChartReference_Condition_Is,
                        AttributeName(attribute, condition.Subject),
                        string.Join(", ", run.Select(c => ValueLabel(attribute, c.Value)))));
                    continue;
                }
            }

            parts.Add(Describe(condition, index));
        }

        return string.Join(Separator, parts.Where(p => p.Length > 0));
    }

    /// <summary>One condition in words.</summary>
    /// <param name="condition">The condition.</param>
    /// <param name="index">The index whose entries name the attributes, values and settings.</param>
    /// <returns>The condition as a short sentence without a full stop.</returns>
    public static string Describe(RuleCondition condition, ChartReferenceIndex index)
    {
        ArgumentNullException.ThrowIfNull(condition);
        ArgumentNullException.ThrowIfNull(index);

        switch (condition.Kind)
        {
            case RuleConditionKind.Attribute:
                {
                    var attribute = Attribute(index, condition.Subject);
                    var name = AttributeName(attribute, condition.Subject);
                    var value = WithArticle(attribute, ValueLabel(attribute, condition.Value));
                    return Compare(condition.Operator, name, value);
                }

            case RuleConditionKind.ContextParameter:
                {
                    var name = SettingName(index, condition.Subject);
                    return Compare(condition.Operator, name, condition.Value ?? string.Empty);
                }

            case RuleConditionKind.FeatureType:
                {
                    var name = index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.FeatureType, condition.Value ?? string.Empty), out var entry)
                        ? entry.Name
                        : condition.Value ?? string.Empty;
                    return Compare(condition.Operator, Strings.ChartReference_Condition_FeatureType, name);
                }

            case RuleConditionKind.Primitive:
                return Compare(condition.Operator, Strings.ChartReference_Condition_Geometry, Geometry(condition.Value));

            case RuleConditionKind.Otherwise:
                return Strings.ChartReference_Condition_Otherwise;

            default:
                // A test the reader couldn't break down: read the common Lua
                // forms with the catalogue's names; never show the code (#1650).
                return Expression(condition.Text, condition.Operator == ConditionOperator.DoesNotHold, index)
                    ?? (condition.Operator == ConditionOperator.DoesNotHold
                        ? Strings.ChartReference_Condition_ExpressionFails
                        : Strings.ChartReference_Condition_Expression);
        }
    }

    /// <summary>
    /// A geometric primitive's name for the mariner: <c>Point</c> as
    /// "point", <c>Curve</c> as "line", <c>Surface</c> as "area".
    /// </summary>
    /// <param name="primitive">The primitive as the rules name it.</param>
    /// <returns>The name, or the primitive as given when it isn't one of these.</returns>
    public static string Geometry(string? primitive) => primitive?.ToLowerInvariant() switch
    {
        "point" => Strings.ChartReference_Geometry_Point,
        "multipoint" => Strings.ChartReference_Geometry_MultiPoint,
        "curve" or "line" => Strings.ChartReference_Geometry_Curve,
        "surface" or "area" => Strings.ChartReference_Geometry_Surface,
        _ => primitive ?? string.Empty,
    };

    private static string Compare(ConditionOperator op, string subject, string value) => op switch
    {
        ConditionOperator.Equal => Format(Strings.ChartReference_Condition_Is, subject, value),
        ConditionOperator.NotEqual => Format(Strings.ChartReference_Condition_IsNot, subject, value),
        ConditionOperator.LessThan => Format(Strings.ChartReference_Condition_LessThan, subject, value),
        ConditionOperator.LessThanOrEqual => Format(Strings.ChartReference_Condition_AtMost, subject, value),
        ConditionOperator.GreaterThan => Format(Strings.ChartReference_Condition_GreaterThan, subject, value),
        ConditionOperator.GreaterThanOrEqual => Format(Strings.ChartReference_Condition_AtLeast, subject, value),
        ConditionOperator.Present => Format(Strings.ChartReference_Condition_Given, subject),
        ConditionOperator.Absent => Format(Strings.ChartReference_Condition_NotGiven, subject),
        ConditionOperator.IsTrue => Format(Strings.ChartReference_Condition_On, subject),
        ConditionOperator.IsFalse => Format(Strings.ChartReference_Condition_Off, subject),
        ConditionOperator.DoesNotHold => Format(Strings.ChartReference_Condition_IsNot, subject, value),
        _ => Format(Strings.ChartReference_Condition_Is, subject, value),
    };

    private static bool IsIndexedEquality(RuleCondition c) =>
        c is { Kind: RuleConditionKind.Attribute, Operator: ConditionOperator.Equal, Index: not null };

    private static AttributeEntry? Attribute(ChartReferenceIndex index, string? code) =>
        code is { Length: > 0 } && index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.Attribute, code), out var entry)
            ? entry as AttributeEntry
            : null;

    private static string AttributeName(AttributeEntry? attribute, string? code) =>
        attribute is not null ? SentenceCase(attribute.Name) : Words(code);

    // A code the catalogue doesn't name, in words: "depthRangeMinimumValue"
    // reads "Depth range minimum value".
    private static string Words(string? code)
    {
        if (string.IsNullOrEmpty(code))
            return string.Empty;
        var words = System.Text.RegularExpressions.Regex.Replace(code, "(?<=[a-z0-9])(?=[A-Z])", " ").Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    private static string SettingName(ChartReferenceIndex index, string? id) =>
        id is { Length: > 0 } && index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, id), out var entry)
            ? SentenceCase(entry.Name)
            : id ?? string.Empty;

    // A listed value's label in running text: "Red" as "red", and
    // "Stake, Pole, Perch, Post" as "stake, pole, perch or post".
    private static string ValueLabel(AttributeEntry? attribute, string? value)
    {
        if (value is null)
            return string.Empty;
        ListedValue? listed = attribute?.ListedValues.FirstOrDefault(v => v.Code == value);
        if (listed is null)
            return value;

        var words = listed.Label.Split(' ');
        for (int w = 0; w < words.Length; w++)
            words[w] = LowerUnlessAcronym(words[w]);
        var label = string.Join(' ', words);
        int last = label.LastIndexOf(", ", StringComparison.Ordinal);
        return last < 0
            ? label
            : Format(Strings.ChartReference_Condition_OrJoin, label[..last], label[(last + 2)..]);
    }

    // "Beacon shape is a withy", "Category of landmark is a tower": a
    // listed value that names a thing takes an article; a colour or a
    // number doesn't.
    private static string WithArticle(AttributeEntry? attribute, string value)
    {
        if (attribute is null || attribute.ListedValues.Count == 0 || value.Length == 0)
            return value;
        var name = attribute.Name.ToLowerInvariant();
        bool names = name.EndsWith("shape", StringComparison.Ordinal)
            || name.StartsWith("category of", StringComparison.Ordinal)
            || name.StartsWith("type of", StringComparison.Ordinal);
        if (!names)
            return value;
        return Format(
            "aeiou".Contains(char.ToLowerInvariant(value[0]), StringComparison.Ordinal)
                ? Strings.ChartReference_Condition_ArticleAn
                : Strings.ChartReference_Condition_ArticleA,
            value);
    }

    /// <summary>"Beacon Shape" as "Beacon shape"; acronyms such as "RADAR" keep their capitals.</summary>
    /// <param name="name">A catalogue name.</param>
    /// <returns>The name in sentence case.</returns>
    public static string SentenceCase(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var words = name.Split(' ');
        for (int w = 1; w < words.Length; w++)
            words[w] = LowerUnlessAcronym(words[w]);
        return string.Join(' ', words);
    }

    private static string LowerUnlessAcronym(string word) =>
        word.Count(char.IsUpper) > 1 ? word : word.ToLowerInvariant();

    private static string Format(string format, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
