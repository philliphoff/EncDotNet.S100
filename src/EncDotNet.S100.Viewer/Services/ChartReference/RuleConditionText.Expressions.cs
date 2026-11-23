using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>
/// Reads the rule tests the index keeps as source text, as in
/// <c>feature.depthRangeMinimumValue or feature.depthRangeMaximumValue</c>
/// or <c>contains(restriction, { 1, 2 })</c>, into words with the feature
/// catalogue's names (#1650).
/// </summary>
/// <remarks>
/// Only tests of the feature's own attributes, the display settings and the
/// geometry are read: comparisons, <c>contains</c>, presence and
/// <c>and</c> / <c>or</c> / <c>not</c>. A test of a value the rule works out
/// itself (a local such as <c>hazardSymbol</c>, a count such as
/// <c>#spatialAssociations</c>) can't be said in the catalogue's words, so
/// the caller says "In some cases" instead. A test that fails is read by
/// negating it: "a or b" fails as "a is not given · b is not given".
/// </remarks>
internal static partial class RuleConditionText
{
    /// <summary>The most values a list names before "or N more".</summary>
    private const int MaxListedValues = 4;

    /// <summary>
    /// The test <paramref name="text"/> in words, or <see langword="null"/>
    /// when it tests something other than the feature's attributes, the
    /// display settings and the geometry.
    /// </summary>
    /// <param name="text">The rule's source text of the test.</param>
    /// <param name="fails">Whether the case is the test failing.</param>
    /// <param name="index">The index whose entries name the attributes, values and settings.</param>
    internal static string? Expression(string? text, bool fails, ChartReferenceIndex index)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            var parser = new Parser(Tokenize(text));
            var node = parser.ParseOr();
            if (!parser.AtEnd)
                return null;
            return Say(node, fails, index);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    // ---- Saying it ----

    private static string? Say(Node node, bool negate, ChartReferenceIndex index) => node switch
    {
        NotNode not => Say(not.Operand, !negate, index),
        // De Morgan: "a or b" fails as "not a and not b".
        JoinNode join => SayJoin(join.Parts, join.IsOr != negate, negate, index),
        PresenceNode presence => SayPresence(presence.Path, negate, index),
        CompareNode compare => SayCompare(compare, negate, index),
        ContainsNode contains => SayContains(contains, negate, index),
        _ => null,
    };

    private static string? SayJoin(IReadOnlyList<Node> nodes, bool isOr, bool negate, ChartReferenceIndex index)
    {
        var parts = new List<string>();
        foreach (var node in nodes)
        {
            if (Say(node, negate, index) is not { } part)
                return null;
            parts.Add(part);
        }

        if (!isOr)
            return string.Join(Separator, parts);
        var lower = parts.Select((p, i) => i == 0 ? p : LowerFirst(p)).ToList();
        return lower.Count == 1
            ? lower[0]
            : Format(Strings.ChartReference_Condition_OrJoin, string.Join(", ", lower.Take(lower.Count - 1)), lower[^1]);
    }

    private static string? SayPresence(string path, bool negate, ChartReferenceIndex index)
    {
        if (Subject(path, index) is not { } subject)
            return null;
        if (subject.Setting)
            return Format(negate ? Strings.ChartReference_Condition_Off : Strings.ChartReference_Condition_On, subject.Name);
        if (subject.Attribute is { ValueType: var type } && IsBoolean(type))
            return YesNo(subject.Name, !negate);
        return Format(negate ? Strings.ChartReference_Condition_NotGiven : Strings.ChartReference_Condition_Given, subject.Name);
    }

    private static string? SayCompare(CompareNode compare, bool negate, ChartReferenceIndex index)
    {
        var op = negate ? Negate(compare.Op) : compare.Op;

        // feature.PrimitiveType == PrimitiveType.Surface
        if (Last(compare.Path).Equals("PrimitiveType", StringComparison.OrdinalIgnoreCase))
        {
            var primitive = compare.Value.Split('.')[^1];
            return Compare(op, Strings.ChartReference_Condition_Geometry, Geometry(primitive));
        }

        if (Subject(compare.Path, index) is not { } subject)
            return null;

        if (compare.Value == "nil")
        {
            return op switch
            {
                ConditionOperator.Equal => Format(Strings.ChartReference_Condition_NotGiven, subject.Name),
                ConditionOperator.NotEqual => Format(Strings.ChartReference_Condition_Given, subject.Name),
                _ => null,
            };
        }

        if (compare.Value is "true" or "false")
        {
            bool yes = (compare.Value == "true") == (op == ConditionOperator.Equal);
            if (op is not (ConditionOperator.Equal or ConditionOperator.NotEqual))
                return null;
            return subject.Setting
                ? Format(yes ? Strings.ChartReference_Condition_On : Strings.ChartReference_Condition_Off, subject.Name)
                : YesNo(subject.Name, yes);
        }

        if (Value(compare.Value, subject.Attribute, index) is not { } value)
            return null;
        if (op is ConditionOperator.Equal or ConditionOperator.NotEqual)
            value = WithArticle(subject.Attribute, value);
        return Compare(op, subject.Name, value);
    }

    private static string? SayContains(ContainsNode contains, bool negate, ChartReferenceIndex index)
    {
        if (Subject(contains.Path, index) is not { Setting: false } subject)
            return null;
        var labels = new List<string>();
        foreach (var value in contains.Values)
        {
            if (Value(value, subject.Attribute, index) is not { } label)
                return null;
            labels.Add(label);
        }

        if (labels.Count == 0)
            return null;
        string list;
        if (labels.Count > MaxListedValues)
        {
            list = Format(Strings.ChartReference_Condition_OrMore, string.Join(", ", labels.Take(MaxListedValues - 1)), labels.Count - MaxListedValues + 1);
        }
        else
        {
            list = labels.Count == 1
                ? labels[0]
                : Format(Strings.ChartReference_Condition_OrJoin, string.Join(", ", labels.Take(labels.Count - 1)), labels[^1]);
        }

        return Format(negate ? Strings.ChartReference_Condition_Excludes : Strings.ChartReference_Condition_Includes, subject.Name, list);
    }

    // What a path names: a display setting, or an attribute of the feature
    // the catalogue knows. A rule's own locals name neither.
    private static (string Name, AttributeEntry? Attribute, bool Setting)? Subject(string path, ChartReferenceIndex index)
    {
        if (path.StartsWith("contextParameters.", StringComparison.Ordinal))
        {
            var id = path["contextParameters.".Length..];
            return index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, id), out _)
                ? (SettingName(index, id), null, true)
                : null;
        }

        var code = Last(path);
        return Attribute(index, code) is { } attribute ? (AttributeName(attribute, code), attribute, false) : null;
    }

    // A compared value: a listed value's label, a number, or a setting.
    private static string? Value(string token, AttributeEntry? attribute, ChartReferenceIndex index)
    {
        if (token == "scaledDecimalZero")
            return "0";
        if (token.StartsWith("contextParameters.", StringComparison.Ordinal))
        {
            var id = token["contextParameters.".Length..];
            return index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.DisplaySetting, id), out _)
                ? LowerFirst(SettingName(index, id))
                : null;
        }

        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return null;
        return attribute is { ListedValues.Count: > 0 }
            ? ValueLabel(attribute, token)
            : number.ToString(CultureInfo.CurrentCulture);
    }

    private static string YesNo(string name, bool yes) =>
        yes ? Format(Strings.ChartReference_Condition_Yes, name) : Format(Strings.ChartReference_Condition_No, LowerFirst(name));

    private static bool IsBoolean(string valueType) =>
        valueType.Equals("boolean", StringComparison.OrdinalIgnoreCase);

    private static ConditionOperator Negate(ConditionOperator op) => op switch
    {
        ConditionOperator.Equal => ConditionOperator.NotEqual,
        ConditionOperator.NotEqual => ConditionOperator.Equal,
        ConditionOperator.LessThan => ConditionOperator.GreaterThanOrEqual,
        ConditionOperator.LessThanOrEqual => ConditionOperator.GreaterThan,
        ConditionOperator.GreaterThan => ConditionOperator.LessThanOrEqual,
        ConditionOperator.GreaterThanOrEqual => ConditionOperator.LessThan,
        _ => op,
    };

    // "feature.surfaceCharacteristics[1].natureOfSurface" names natureOfSurface.
    private static string Last(string path) => path.Split('.')[^1];

    // "Beacon shape is given" inside a list reads "beacon shape is given";
    // an acronym keeps its capitals.
    private static string LowerFirst(string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1])
            ? char.ToLower(text[0], CultureInfo.CurrentCulture) + text[1..]
            : text;

    // ---- Reading it ----

    private abstract record Node;

    private sealed record NotNode(Node Operand) : Node;

    private sealed record JoinNode(bool IsOr, IReadOnlyList<Node> Parts) : Node;

    private sealed record PresenceNode(string Path) : Node;

    private sealed record CompareNode(string Path, ConditionOperator Op, string Value) : Node;

    private sealed record ContainsNode(string Path, IReadOnlyList<string> Values) : Node;

    // Identifiers keep their dots; "feature." and "feature['x']" are dropped
    // and "[1]" indices go, so a path is attribute codes joined by dots.
    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var sb = new System.Text.StringBuilder();
                while (i < text.Length)
                {
                    char d = text[i];
                    if (char.IsLetterOrDigit(d) || d == '_' || d == '.')
                    {
                        sb.Append(d);
                        i++;
                    }
                    else if (d == '[')
                    {
                        int close = text.IndexOf(']', i);
                        if (close < 0)
                            throw new FormatException();
                        var inner = text[(i + 1)..close].Trim().Trim('\'', '"');
                        if (!int.TryParse(inner, out _))
                            sb.Append('.').Append(inner);
                        i = close + 1;
                    }
                    else
                    {
                        break;
                    }
                }

                var word = sb.ToString().Replace("..", ".", StringComparison.Ordinal);
                if (word.StartsWith("feature.", StringComparison.Ordinal))
                    word = word["feature.".Length..];
                tokens.Add(word);
            }
            else if (char.IsDigit(c))
            {
                int start = i;
                while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.'))
                    i++;
                tokens.Add(text[start..i]);
            }
            else if (i + 1 < text.Length && text.Substring(i, 2) is "==" or "~=" or "<=" or ">=")
            {
                tokens.Add(text.Substring(i, 2));
                i += 2;
            }
            else if (c is '<' or '>' or '(' or ')' or '{' or '}' or ',')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else
            {
                // '#' counts, strings and anything else: not the catalogue's words.
                throw new FormatException();
            }
        }

        return tokens;
    }

    private sealed class Parser(List<string> tokens)
    {
        private int _at;

        public bool AtEnd => _at == tokens.Count;

        public Node ParseOr()
        {
            var parts = new List<Node> { ParseAnd() };
            while (Accept("or"))
                parts.Add(ParseAnd());
            return parts.Count == 1 ? parts[0] : new JoinNode(true, parts);
        }

        private Node ParseAnd()
        {
            var parts = new List<Node> { ParseUnary() };
            while (Accept("and"))
                parts.Add(ParseUnary());
            return parts.Count == 1 ? parts[0] : new JoinNode(false, parts);
        }

        private Node ParseUnary()
        {
            if (Accept("not"))
                return new NotNode(ParseUnary());
            if (Accept("("))
            {
                var inner = ParseOr();
                Expect(")");
                return inner;
            }

            if (Accept("contains"))
                return ParseContains();

            var path = Identifier();
            if (Peek() is { } op && Operator(op) is { } cmp)
            {
                _at++;
                return new CompareNode(path, cmp, Operand());
            }

            return new PresenceNode(path);
        }

        // contains(attribute, { 1, 2 }) or contains(1, attribute).
        private ContainsNode ParseContains()
        {
            Expect("(");
            string path;
            List<string> values;
            if (Peek() is { } first && char.IsDigit(first[0]))
            {
                values = [Next()];
                Expect(",");
                path = Identifier();
            }
            else
            {
                path = Identifier();
                Expect(",");
                values = [];
                if (Accept("{"))
                {
                    do
                    {
                        values.Add(Operand());
                    }
                    while (Accept(","));
                    Expect("}");
                }
                else
                {
                    values.Add(Operand());
                }
            }

            Expect(")");
            return new ContainsNode(path, values);
        }

        private string Identifier()
        {
            var token = Next();
            if (!char.IsLetter(token[0]) || token is "and" or "or" or "not" or "contains")
                throw new FormatException();
            return token;
        }

        private string Operand() => Next();

        private static ConditionOperator? Operator(string token) => token switch
        {
            "==" => ConditionOperator.Equal,
            "~=" => ConditionOperator.NotEqual,
            "<" => ConditionOperator.LessThan,
            "<=" => ConditionOperator.LessThanOrEqual,
            ">" => ConditionOperator.GreaterThan,
            ">=" => ConditionOperator.GreaterThanOrEqual,
            _ => null,
        };

        private string? Peek() => _at < tokens.Count ? tokens[_at] : null;

        private string Next() => _at < tokens.Count ? tokens[_at++] : throw new FormatException();

        private bool Accept(string token)
        {
            if (Peek() != token)
                return false;
            _at++;
            return true;
        }

        private void Expect(string token)
        {
            if (!Accept(token))
                throw new FormatException();
        }
    }
}
