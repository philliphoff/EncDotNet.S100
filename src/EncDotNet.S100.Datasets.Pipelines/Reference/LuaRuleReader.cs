namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>
/// Reads a Lua portrayal rule file (S-100 Part 9a) statically: the
/// portrayal items, colour fills, plain lines and viewing groups it names,
/// each with the conditions of its enclosing <c>if</c>/<c>elseif</c>/<c>else</c>
/// blocks, and the rules it <c>require</c>s.
/// </summary>
/// <remarks>
/// The reader tracks Lua's block structure with a stack instead of relying
/// on indentation, and only looks inside string literals for item names, so
/// a call to a shared rule such as <c>SAFCON01(...)</c> isn't mistaken for
/// the symbol of the same name. Conditions are read within the innermost
/// enclosing function.
/// </remarks>
internal static class LuaRuleReader
{
    private const string FeatureObject = "feature";
    private const string ContextObject = "contextParameters";
    private const string PrimitiveTypeAttribute = "PrimitiveType";
    private const string CodeAttribute = "Code";

    private enum FrameKind
    {
        Function,
        If,
        Block,
        Repeat,
    }

    private sealed class Frame(FrameKind kind)
    {
        public FrameKind Kind { get; } = kind;

        /// <summary>For an if block: the conditions of the current branch.</summary>
        public IReadOnlyList<RuleCondition> Current { get; set; } = [];

        /// <summary>For an if block: each branch so far, with its source text.</summary>
        public List<(IReadOnlyList<RuleCondition> Conditions, string Text)> Branches { get; } = [];
    }

    public static RuleFacts Read(string name, string source, RuleVocabulary vocabulary)
    {
        var tokens = LuaLexer.Tokenize(source);
        var result = new RuleFacts(name);
        var stack = new List<Frame>();
        var colourLiterals = new List<RuleFact>();
        var assemblesColourFill = false;
        var pendingLoopDo = false;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            switch (token.Kind)
            {
                case LuaTokenKind.Keyword:
                    switch (token.Text)
                    {
                        case "function":
                            if (IsFeatureEntryPoint(tokens, i, out var functionName))
                            {
                                result.FeatureEntryPoints.Add(functionName);
                            }
                            stack.Add(new Frame(FrameKind.Function));
                            break;
                        case "if":
                            {
                                var end = FindThen(tokens, i + 1);
                                var conditions = ReadCondition(tokens, i + 1, end, token.Line, out var text);
                                var frame = new Frame(FrameKind.If) { Current = conditions };
                                frame.Branches.Add((conditions, text));
                                stack.Add(frame);
                                i = end;
                                break;
                            }
                        case "elseif":
                            {
                                var end = FindThen(tokens, i + 1);
                                var conditions = ReadCondition(tokens, i + 1, end, token.Line, out var text);
                                if (Top(stack) is { Kind: FrameKind.If } frame)
                                {
                                    frame.Current = conditions;
                                    frame.Branches.Add((conditions, text));
                                }
                                i = end;
                                break;
                            }
                        case "else":
                            if (Top(stack) is { Kind: FrameKind.If } ifFrame)
                            {
                                ifFrame.Current = ElseConditions(ifFrame, token.Line);
                            }
                            break;
                        case "for":
                        case "while":
                            stack.Add(new Frame(FrameKind.Block));
                            pendingLoopDo = true;
                            break;
                        case "do":
                            if (pendingLoopDo)
                            {
                                pendingLoopDo = false;
                            }
                            else
                            {
                                stack.Add(new Frame(FrameKind.Block));
                            }
                            break;
                        case "repeat":
                            stack.Add(new Frame(FrameKind.Repeat));
                            break;
                        case "until":
                        case "end":
                            if (stack.Count > 0) stack.RemoveAt(stack.Count - 1);
                            break;
                    }
                    break;

                case LuaTokenKind.Name when token.Text == "require":
                    {
                        var j = i + 1;
                        if (j < tokens.Count && tokens[j].IsSymbol("(")) j++;
                        if (j < tokens.Count && tokens[j].Kind == LuaTokenKind.String)
                        {
                            result.Requires.Add(tokens[j].Text);
                            i = j;
                        }
                        break;
                    }

                case LuaTokenKind.Name when token.Text == "SimpleLineStyle":
                    if (TryReadSimpleLineStyle(tokens, i, out var dash, out var width, out var colour))
                    {
                        result.Facts.Add(RuleText.Pen(dash, width, colour, Conditions(stack), token.Line, null));
                    }
                    break;

                case LuaTokenKind.Name when token.Text == "viewingGroup":
                    if (i + 2 < tokens.Count
                        && tokens[i + 1].IsSymbol("=")
                        && tokens[i + 2].Kind == LuaTokenKind.Number
                        && (i == 0 || !tokens[i - 1].IsSymbol(".")))
                    {
                        result.Facts.Add(new RuleFact(
                            RuleFactKind.ViewingGroup, tokens[i + 2].Text, Conditions(stack), token.Line, null));
                    }
                    break;

                case LuaTokenKind.String when IsDebugArgument(tokens, i):
                    // Debug.StartPerformance('Lua Code - SAFCON01') names a rule, not a symbol.
                    break;

                case LuaTokenKind.String:
                    {
                        var conditions = Conditions(stack);
                        RuleText.Scan(token.Text, vocabulary, conditions, token.Line, null, result.Facts);
                        if (token.Text.EndsWith("ColorFill:", StringComparison.Ordinal))
                        {
                            assemblesColourFill = true;
                        }
                        else if (vocabulary.ColorTokens.Contains(token.Text))
                        {
                            colourLiterals.Add(new RuleFact(RuleFactKind.ColorFill, token.Text, conditions, token.Line, null));
                        }
                        break;
                    }
            }
        }

        // 'ColorFill:' .. Colour: the colour tokens the file assigns are the
        // fills it can draw, as in SEABED01.
        if (assemblesColourFill)
        {
            result.Facts.AddRange(colourLiterals);
        }

        return result;
    }

    /// <summary>Whether the string at <paramref name="i"/> is an argument of a <c>Debug.*</c> or <c>error</c> call.</summary>
    private static bool IsDebugArgument(List<LuaToken> tokens, int i)
    {
        if (i < 2 || !tokens[i - 1].IsSymbol("(")) return false;
        if (tokens[i - 2].Is(LuaTokenKind.Name, "error")) return true;
        return i >= 4 && tokens[i - 3].IsSymbol(".") && tokens[i - 4].Is(LuaTokenKind.Name, "Debug");
    }

    private static Frame? Top(List<Frame> stack) => stack.Count > 0 ? stack[^1] : null;

    /// <summary>The conditions of the enclosing if blocks within the innermost function, outermost first.</summary>
    private static IReadOnlyList<RuleCondition> Conditions(List<Frame> stack)
    {
        List<RuleCondition>? result = null;
        var start = stack.FindLastIndex(f => f.Kind == FrameKind.Function) + 1;
        for (var k = start; k < stack.Count; k++)
        {
            if (stack[k].Kind == FrameKind.If && stack[k].Current.Count > 0)
            {
                (result ??= []).AddRange(stack[k].Current);
            }
        }

        return result is null ? [] : result;
    }

    private static IReadOnlyList<RuleCondition> ElseConditions(Frame frame, int line)
    {
        if (frame.Branches.Count == 1)
        {
            var (only, text) = frame.Branches[0];
            if (only.Count == 1) return [only[0].Negate() with { Line = line }];
            return
            [
                new RuleCondition
                {
                    Kind = RuleConditionKind.Expression,
                    Operator = ConditionOperator.DoesNotHold,
                    Text = text,
                    Line = line,
                },
            ];
        }

        return [new RuleCondition { Kind = RuleConditionKind.Otherwise, Operator = ConditionOperator.Holds, Line = line }];
    }

    private static int FindThen(List<LuaToken> tokens, int start)
    {
        for (var j = start; j < tokens.Count; j++)
        {
            if (tokens[j].IsKeyword("then")) return j;
        }

        return tokens.Count;
    }

    private static bool IsFeatureEntryPoint(List<LuaToken> tokens, int i, out string name)
    {
        name = string.Empty;
        if (i > 0 && tokens[i - 1].IsKeyword("local")) return false;
        if (i + 3 >= tokens.Count) return false;
        if (tokens[i + 1].Kind != LuaTokenKind.Name || !tokens[i + 2].IsSymbol("(")) return false;
        if (!tokens[i + 3].Is(LuaTokenKind.Name, FeatureObject)) return false;
        name = tokens[i + 1].Text;
        return true;
    }

    private static bool TryReadSimpleLineStyle(
        List<LuaToken> tokens, int i, out string dash, out string width, out string colour)
    {
        dash = width = colour = string.Empty;
        if (i + 7 >= tokens.Count) return false;
        if (!tokens[i + 1].IsSymbol("(")
            || tokens[i + 2].Kind != LuaTokenKind.String
            || !tokens[i + 3].IsSymbol(",")
            || tokens[i + 4].Kind != LuaTokenKind.Number
            || !tokens[i + 5].IsSymbol(",")
            || tokens[i + 6].Kind != LuaTokenKind.String
            || !tokens[i + 7].IsSymbol(")"))
        {
            return false;
        }

        dash = tokens[i + 2].Text;
        width = tokens[i + 4].Text;
        colour = tokens[i + 6].Text;
        return true;
    }

    /// <summary>Reads the condition between <c>if</c>/<c>elseif</c> and <c>then</c> as a conjunction.</summary>
    private static IReadOnlyList<RuleCondition> ReadCondition(
        List<LuaToken> tokens, int start, int end, int line, out string text)
    {
        var span = tokens.GetRange(start, Math.Max(0, Math.Min(end, tokens.Count) - start));
        text = ToText(span);
        if (span.Count == 0) return [];

        var atoms = new List<List<LuaToken>>();
        var current = new List<LuaToken>();
        var depth = 0;
        foreach (var t in span)
        {
            if (t.IsSymbol("(") || t.IsSymbol("[") || t.IsSymbol("{")) depth++;
            else if (t.IsSymbol(")") || t.IsSymbol("]") || t.IsSymbol("}")) depth--;

            if (depth == 0 && t.IsKeyword("or"))
            {
                return [Expression(text, negated: false, line)];
            }

            if (depth == 0 && t.IsKeyword("and"))
            {
                atoms.Add(current);
                current = [];
            }
            else
            {
                current.Add(t);
            }
        }

        atoms.Add(current);
        return atoms.Where(a => a.Count > 0).Select(a => ReadAtom(a, line)).ToList();
    }

    private static RuleCondition ReadAtom(List<LuaToken> atom, int line)
    {
        atom = StripParentheses(atom);
        var negated = false;
        if (atom.Count > 0 && atom[0].IsKeyword("not"))
        {
            negated = true;
            atom = StripParentheses(atom.GetRange(1, atom.Count - 1));
        }

        var text = ToText(atom);
        if (atom.Count >= 3
            && atom[0].Kind == LuaTokenKind.Name
            && atom[1].IsSymbol(".")
            && atom[2].Kind == LuaTokenKind.Name)
        {
            var target = atom[0].Text;
            var subject = atom[2].Text;
            var p = 3;
            int? index = null;
            if (target == FeatureObject
                && p + 2 < atom.Count
                && atom[p].IsSymbol("[")
                && atom[p + 1].Kind == LuaTokenKind.Number
                && atom[p + 2].IsSymbol("]")
                && int.TryParse(atom[p + 1].Text, out var parsed))
            {
                index = parsed;
                p += 3;
            }

            if (target is FeatureObject or ContextObject)
            {
                var condition = ReadComparison(target, subject, index, atom, p, negated, line);
                if (condition is not null) return condition;
            }
        }

        return Expression(text, negated, line);
    }

    private static RuleCondition? ReadComparison(
        string target, string subject, int? index, List<LuaToken> atom, int p, bool negated, int line)
    {
        var kind = target == ContextObject ? RuleConditionKind.ContextParameter : RuleConditionKind.Attribute;
        if (p == atom.Count)
        {
            var op = kind == RuleConditionKind.ContextParameter
                ? (negated ? ConditionOperator.IsFalse : ConditionOperator.IsTrue)
                : (negated ? ConditionOperator.Absent : ConditionOperator.Present);
            return new RuleCondition { Kind = kind, Operator = op, Subject = subject, Index = index, Line = line };
        }

        if (negated || atom[p].Kind != LuaTokenKind.Symbol) return null;
        ConditionOperator? comparison = atom[p].Text switch
        {
            "==" => ConditionOperator.Equal,
            "~=" => ConditionOperator.NotEqual,
            "<" => ConditionOperator.LessThan,
            "<=" => ConditionOperator.LessThanOrEqual,
            ">" => ConditionOperator.GreaterThan,
            ">=" => ConditionOperator.GreaterThanOrEqual,
            _ => null,
        };
        if (comparison is not { } compare) return null;
        var rest = atom.GetRange(p + 1, atom.Count - p - 1);

        if (kind == RuleConditionKind.Attribute && subject == CodeAttribute && index is null)
        {
            if (rest.Count == 1 && rest[0].Kind == LuaTokenKind.String
                && compare is ConditionOperator.Equal or ConditionOperator.NotEqual)
            {
                return new RuleCondition
                {
                    Kind = RuleConditionKind.FeatureType,
                    Operator = compare,
                    Value = rest[0].Text,
                    Line = line,
                };
            }

            return null;
        }

        if (kind == RuleConditionKind.Attribute && subject == PrimitiveTypeAttribute && index is null)
        {
            if (rest.Count == 3 && rest[0].Is(LuaTokenKind.Name, PrimitiveTypeAttribute) && rest[1].IsSymbol(".")
                && rest[2].Kind == LuaTokenKind.Name && compare is ConditionOperator.Equal or ConditionOperator.NotEqual)
            {
                return new RuleCondition
                {
                    Kind = RuleConditionKind.Primitive,
                    Operator = compare,
                    Value = rest[2].Text,
                    Line = line,
                };
            }

            return null;
        }

        if (rest.Count == 2 && rest[0].IsSymbol("-") && rest[1].Kind == LuaTokenKind.Number)
        {
            rest = [new LuaToken(LuaTokenKind.Number, "-" + rest[1].Text, rest[1].Line)];
        }

        if (rest.Count != 1) return null;
        var value = rest[0];
        if (value.IsKeyword("nil"))
        {
            return compare switch
            {
                ConditionOperator.Equal => new RuleCondition { Kind = kind, Operator = Absent(kind), Subject = subject, Index = index, Line = line },
                ConditionOperator.NotEqual => new RuleCondition { Kind = kind, Operator = Present(kind), Subject = subject, Index = index, Line = line },
                _ => null,
            };
        }

        if (kind == RuleConditionKind.ContextParameter && (value.IsKeyword("true") || value.IsKeyword("false"))
            && compare is ConditionOperator.Equal or ConditionOperator.NotEqual)
        {
            var on = value.IsKeyword("true") == (compare == ConditionOperator.Equal);
            return new RuleCondition
            {
                Kind = kind,
                Operator = on ? ConditionOperator.IsTrue : ConditionOperator.IsFalse,
                Subject = subject,
                Line = line,
            };
        }

        if (value.Kind is not (LuaTokenKind.Number or LuaTokenKind.String)) return null;
        return new RuleCondition
        {
            Kind = kind,
            Operator = compare,
            Subject = subject,
            Index = index,
            Value = value.Text,
            Line = line,
        };

        static ConditionOperator Absent(RuleConditionKind k) =>
            k == RuleConditionKind.ContextParameter ? ConditionOperator.IsFalse : ConditionOperator.Absent;

        static ConditionOperator Present(RuleConditionKind k) =>
            k == RuleConditionKind.ContextParameter ? ConditionOperator.IsTrue : ConditionOperator.Present;
    }

    private static RuleCondition Expression(string text, bool negated, int line) => new()
    {
        Kind = RuleConditionKind.Expression,
        Operator = negated ? ConditionOperator.DoesNotHold : ConditionOperator.Holds,
        Text = text,
        Line = line,
    };

    /// <summary>Removes parentheses that wrap the whole of <paramref name="tokens"/>.</summary>
    private static List<LuaToken> StripParentheses(List<LuaToken> tokens)
    {
        while (tokens.Count >= 2 && tokens[0].IsSymbol("(") && tokens[^1].IsSymbol(")"))
        {
            var depth = 0;
            var wraps = true;
            for (var k = 0; k < tokens.Count - 1; k++)
            {
                if (tokens[k].IsSymbol("(")) depth++;
                else if (tokens[k].IsSymbol(")")) depth--;
                if (depth == 0)
                {
                    wraps = false;
                    break;
                }
            }

            if (!wraps) break;
            tokens = tokens.GetRange(1, tokens.Count - 2);
        }

        return tokens;
    }

    private static string ToText(List<LuaToken> tokens)
    {
        var sb = new System.Text.StringBuilder();
        for (var k = 0; k < tokens.Count; k++)
        {
            var t = tokens[k];
            var tight = t.IsSymbol(".") || t.IsSymbol("[") || t.IsSymbol("]") || t.IsSymbol(")") || t.IsSymbol(",") || t.IsSymbol(":")
                || (k > 0 && (tokens[k - 1].IsSymbol(".") || tokens[k - 1].IsSymbol("[") || tokens[k - 1].IsSymbol("(") || tokens[k - 1].IsSymbol(":") || tokens[k - 1].IsSymbol("#")))
                || (t.IsSymbol("(") && k > 0 && tokens[k - 1].Kind == LuaTokenKind.Name);
            if (k > 0 && !tight) sb.Append(' ');
            sb.Append(t.Kind == LuaTokenKind.String ? $"'{t.Text}'" : t.Text);
        }

        return sb.ToString();
    }
}
