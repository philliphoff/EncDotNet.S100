using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// Reads an XSLT portrayal rule file (S-100 Part 9 §11.2, the GML products)
/// statically: for each template, the feature type its <c>match</c> names
/// and the conditions of its predicate, then the <c>symbol reference</c>s,
/// symbol file paths, colour fills, pens and <c>viewingGroup</c>s inside
/// it, under the template's <c>xsl:if</c> and <c>xsl:choose</c> tests.
/// Named templates are read separately and linked through the
/// <c>xsl:call-template</c>s that reach them.
/// </summary>
internal static partial class XsltRuleReader
{
    private static readonly XNamespace Xsl = "http://www.w3.org/1999/XSL/Transform";

    [GeneratedRegex(@"^@primitive\s*=\s*['""](\w+)['""]$")]
    private static partial Regex PrimitivePattern();

    [GeneratedRegex(@"^([A-Za-z_][\w]*)(?:\[(\d+)\])?\s*(!=|<=|>=|=|<|>)\s*(?:'([^']*)'|""([^""]*)""|(-?[\d.]+))$")]
    private static partial Regex ComparisonPattern();

    [GeneratedRegex(@"^[A-Za-z_][\w]*$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^not\(\s*([A-Za-z_][\w]*)\s*\)$")]
    private static partial Regex AbsentPattern();

    public static RuleFacts Read(
        string name, string source, RuleVocabulary vocabulary, IReadOnlySet<string>? featureCodes)
    {
        var result = new RuleFacts(name);
        XDocument document;
        try
        {
            document = XDocument.Parse(source, LoadOptions.SetLineInfo);
        }
        catch (XmlException)
        {
            return result;
        }

        if (document.Root is not { } root) return result;

        foreach (var include in root.Elements().Where(e => e.Name == Xsl + "include" || e.Name == Xsl + "import"))
        {
            if ((string?)include.Attribute("href") is { Length: > 0 } href)
            {
                result.Includes.Add(href);
                result.Requires.Add(Path.GetFileNameWithoutExtension(href));
            }
        }

        foreach (var template in root.Descendants(Xsl + "template"))
        {
            var line = LineOf(template);
            if ((string?)template.Attribute("match") is { Length: > 0 } match)
            {
                foreach (var (owner, conditions) in ReadMatch(match, line, featureCodes))
                {
                    Visit(template, conditions, owner, vocabulary, result.Facts, result.Calls);
                }
            }
            else if ((string?)template.Attribute("name") is { Length: > 0 } templateName)
            {
                if (!result.NamedTemplates.TryGetValue(templateName, out var named))
                {
                    result.NamedTemplates[templateName] = named = new NamedTemplate();
                }

                Visit(template, [], null, vocabulary, named.Facts, named.Calls);
            }
        }

        return result;
    }

    private static int LineOf(XObject node) => node is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 0;

    private static void Visit(
        XElement element,
        IReadOnlyList<RuleCondition> conditions,
        string? owner,
        RuleVocabulary vocabulary,
        List<RuleFact> facts,
        List<TemplateCall> calls)
    {
        var line = LineOf(element);
        foreach (var attribute in element.Attributes())
        {
            if (attribute.Name.LocalName is "match" or "mode" or "name" or "priority") continue;
            RuleText.Scan(attribute.Value, vocabulary, conditions, line, owner, facts, KindOf(element));
        }

        if (element.Name == Xsl + "call-template" && (string?)element.Attribute("name") is { Length: > 0 } called)
        {
            calls.Add(new TemplateCall(owner, called, conditions));
        }
        else if (element.Name == Xsl + "with-param"
            && (string?)element.Attribute("name") == "viewingGroup"
            && int.TryParse(element.Value.Trim(), out _))
        {
            facts.Add(new RuleFact(RuleFactKind.ViewingGroup, element.Value.Trim(), conditions, line, owner));
        }

        if (element.Name.Namespace != Xsl)
        {
            switch (element.Name.LocalName)
            {
                case "viewingGroup" when int.TryParse(element.Value.Trim(), out _):
                    facts.Add(new RuleFact(RuleFactKind.ViewingGroup, element.Value.Trim(), conditions, line, owner));
                    break;
                case "colorFill":
                    if (element.Elements().FirstOrDefault(e => e.Name.LocalName == "color") is { } color
                        && NamePattern().IsMatch(color.Value.Trim()))
                    {
                        facts.Add(new RuleFact(RuleFactKind.ColorFill, color.Value.Trim(), conditions, line, owner));
                    }
                    break;
                case "lineStyle" when element.Attribute("reference") is null:
                    if (element.Elements().FirstOrDefault(e => e.Name.LocalName == "pen") is { } pen
                        && pen.Elements().FirstOrDefault(e => e.Name.LocalName == "color") is { } penColor)
                    {
                        var dash = element.Elements().Any(e => e.Name.LocalName == "dash") ? "dash" : "solid";
                        facts.Add(RuleText.Pen(
                            dash, (string?)pen.Attribute("width") ?? "0", penColor.Value.Trim(), conditions, line, owner));
                    }
                    break;
            }
        }

        foreach (var node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    RuleText.Scan(text.Value, vocabulary, conditions, LineOf(text) is > 0 and var l ? l : line, owner, facts);
                    break;
                case XElement child:
                    Visit(child, ChildConditions(child, conditions), owner, vocabulary, facts, calls);
                    break;
            }
        }
    }

    private static ReferenceKind? KindOf(XElement element) => element.Name.LocalName switch
    {
        "symbol" => ReferenceKind.Symbol,
        "lineStyleReference" => ReferenceKind.LineStyle,
        "areaFillReference" => ReferenceKind.AreaFill,
        _ => null,
    };

    private static IReadOnlyList<RuleCondition> ChildConditions(XElement child, IReadOnlyList<RuleCondition> conditions)
    {
        if (child.Name.Namespace != Xsl) return conditions;
        RuleCondition? added = child.Name.LocalName switch
        {
            "if" or "when" when (string?)child.Attribute("test") is { Length: > 0 } test =>
                new RuleCondition
                {
                    Kind = RuleConditionKind.Expression,
                    Operator = ConditionOperator.Holds,
                    Text = test,
                    Line = LineOf(child),
                },
            "otherwise" => new RuleCondition
            {
                Kind = RuleConditionKind.Otherwise,
                Operator = ConditionOperator.Holds,
                Line = LineOf(child),
            },
            _ => null,
        };

        return added is null ? conditions : [.. conditions, added];
    }

    /// <summary>
    /// Splits a template's <c>match</c> into the feature types it names,
    /// each with the conditions of its predicate.
    /// </summary>
    private static List<(string? Owner, IReadOnlyList<RuleCondition> Conditions)> ReadMatch(
        string match, int line, IReadOnlySet<string>? featureCodes)
    {
        var result = new List<(string?, IReadOnlyList<RuleCondition>)>();
        foreach (var alternative in SplitTopLevel(match, "|"))
        {
            var pattern = alternative.Trim().TrimStart('/');
            var bracket = pattern.IndexOf('[');
            var step = bracket >= 0 ? pattern[..bracket] : pattern;
            var colon = step.LastIndexOf(':');
            var code = (colon >= 0 ? step[(colon + 1)..] : step).Trim();
            var owner = NamePattern().IsMatch(code) && (featureCodes is null || featureCodes.Contains(code)) ? code : null;

            var conditions = new List<RuleCondition>();
            if (bracket >= 0)
            {
                foreach (var predicate in Predicates(pattern[bracket..]))
                {
                    conditions.AddRange(ReadPredicate(predicate, line));
                }
            }

            result.Add((owner, conditions));
        }

        return result;
    }

    /// <summary>The contents of each top-level <c>[...]</c> group.</summary>
    private static IEnumerable<string> Predicates(string text)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '[')
            {
                if (depth++ == 0) start = i + 1;
            }
            else if (text[i] == ']' && --depth == 0)
            {
                yield return text[start..i];
            }
        }
    }

    private static IEnumerable<RuleCondition> ReadPredicate(string predicate, int line)
    {
        if (SplitTopLevel(predicate, " or ").Count > 1)
        {
            yield return Expression(predicate.Trim(), line);
            yield break;
        }

        foreach (var raw in SplitTopLevel(predicate, " and "))
        {
            var atom = raw.Trim();
            while (atom.StartsWith('(') && atom.EndsWith(')')) atom = atom[1..^1].Trim();

            if (PrimitivePattern().Match(atom) is { Success: true } primitive)
            {
                yield return new RuleCondition
                {
                    Kind = RuleConditionKind.Primitive,
                    Operator = ConditionOperator.Equal,
                    Value = primitive.Groups[1].Value,
                    Line = line,
                };
            }
            else if (ComparisonPattern().Match(atom) is { Success: true } comparison)
            {
                var value = comparison.Groups[4].Success ? comparison.Groups[4].Value
                    : comparison.Groups[5].Success ? comparison.Groups[5].Value
                    : comparison.Groups[6].Value;
                yield return new RuleCondition
                {
                    Kind = RuleConditionKind.Attribute,
                    Operator = comparison.Groups[3].Value switch
                    {
                        "=" => ConditionOperator.Equal,
                        "!=" => ConditionOperator.NotEqual,
                        "<" => ConditionOperator.LessThan,
                        "<=" => ConditionOperator.LessThanOrEqual,
                        ">" => ConditionOperator.GreaterThan,
                        _ => ConditionOperator.GreaterThanOrEqual,
                    },
                    Subject = comparison.Groups[1].Value,
                    Index = comparison.Groups[2].Success ? int.Parse(comparison.Groups[2].Value) : null,
                    Value = value,
                    Line = line,
                };
            }
            else if (NamePattern().IsMatch(atom))
            {
                yield return new RuleCondition
                {
                    Kind = RuleConditionKind.Attribute,
                    Operator = ConditionOperator.Present,
                    Subject = atom,
                    Line = line,
                };
            }
            else if (AbsentPattern().Match(atom) is { Success: true } absent)
            {
                yield return new RuleCondition
                {
                    Kind = RuleConditionKind.Attribute,
                    Operator = ConditionOperator.Absent,
                    Subject = absent.Groups[1].Value,
                    Line = line,
                };
            }
            else if (atom.Length > 0)
            {
                yield return Expression(atom, line);
            }
        }
    }

    private static RuleCondition Expression(string text, int line) => new()
    {
        Kind = RuleConditionKind.Expression,
        Operator = ConditionOperator.Holds,
        Text = text,
        Line = line,
    };

    /// <summary>Splits on <paramref name="separator"/> outside brackets, parentheses and quotes.</summary>
    private static List<string> SplitTopLevel(string text, string separator)
    {
        var parts = new List<string>();
        var depth = 0;
        char? quote = null;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quote is { } q)
            {
                if (c == q) quote = null;
            }
            else if (c is '\'' or '"')
            {
                quote = c;
            }
            else if (c is '[' or '(')
            {
                depth++;
            }
            else if (c is ']' or ')')
            {
                depth--;
            }
            else if (depth == 0 && string.CompareOrdinal(text, i, separator, 0, separator.Length) == 0)
            {
                parts.Add(text[start..i]);
                i += separator.Length - 1;
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }
}
