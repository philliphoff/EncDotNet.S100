namespace EncDotNet.S100.Datasets.Pipelines.Reference;

/// <summary>The kinds of token <see cref="LuaLexer"/> produces.</summary>
internal enum LuaTokenKind
{
    Name,
    Keyword,
    String,
    Number,
    Symbol,
}

/// <summary>
/// A Lua token. For a <see cref="LuaTokenKind.String"/> token,
/// <see cref="Text"/> is the string's content without quotes or escapes
/// resolved beyond the quote characters.
/// </summary>
internal readonly record struct LuaToken(LuaTokenKind Kind, string Text, int Line)
{
    public bool Is(LuaTokenKind kind, string text) => Kind == kind && Text == text;

    public bool IsKeyword(string text) => Is(LuaTokenKind.Keyword, text);

    public bool IsSymbol(string text) => Is(LuaTokenKind.Symbol, text);
}

/// <summary>
/// Splits Lua 5.2 source into tokens, dropping whitespace and comments.
/// It is just enough for the static rule reader: it doesn't validate the
/// source, and it never throws on malformed text.
/// </summary>
internal static class LuaLexer
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto", "if", "in",
        "local", "nil", "not", "or", "repeat", "return", "then", "true", "until", "while",
    };

    private static readonly string[] MultiCharSymbols = ["...", "..", "==", "~=", "<=", ">=", "::"];

    public static List<LuaToken> Tokenize(string source)
    {
        var tokens = new List<LuaToken>(source.Length / 6);
        var line = 1;
        var i = 0;
        while (i < source.Length)
        {
            var c = source[i];
            if (c == '\n')
            {
                line++;
                i++;
            }
            else if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '-' && At(source, i + 1) == '-')
            {
                i += 2;
                if (TryLongBracketLevel(source, i, out var level))
                {
                    i = SkipLongBracket(source, i, level, ref line, out _);
                }
                else
                {
                    while (i < source.Length && source[i] != '\n') i++;
                }
            }
            else if (c is '\'' or '"')
            {
                var start = line;
                var text = ReadQuoted(source, ref i, ref line);
                tokens.Add(new LuaToken(LuaTokenKind.String, text, start));
            }
            else if (c == '[' && TryLongBracketLevel(source, i, out var level))
            {
                var start = line;
                i = SkipLongBracket(source, i, level, ref line, out var text);
                tokens.Add(new LuaToken(LuaTokenKind.String, text, start));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_')) i++;
                var word = source[start..i];
                tokens.Add(new LuaToken(Keywords.Contains(word) ? LuaTokenKind.Keyword : LuaTokenKind.Name, word, line));
            }
            else if (char.IsDigit(c) || (c == '.' && char.IsDigit(At(source, i + 1))))
            {
                var start = i;
                i++;
                while (i < source.Length)
                {
                    var d = source[i];
                    if (char.IsLetterOrDigit(d) || d == '.')
                    {
                        i++;
                    }
                    else if ((d is '+' or '-') && source[i - 1] is 'e' or 'E' or 'p' or 'P')
                    {
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }
                tokens.Add(new LuaToken(LuaTokenKind.Number, source[start..i], line));
            }
            else
            {
                var symbol = MultiCharSymbols.FirstOrDefault(s => string.CompareOrdinal(source, i, s, 0, s.Length) == 0)
                    ?? c.ToString();
                tokens.Add(new LuaToken(LuaTokenKind.Symbol, symbol, line));
                i += symbol.Length;
            }
        }

        return tokens;
    }

    private static char At(string s, int i) => i < s.Length ? s[i] : '\0';

    private static string ReadQuoted(string source, ref int i, ref int line)
    {
        var quote = source[i++];
        var sb = new System.Text.StringBuilder();
        while (i < source.Length)
        {
            var c = source[i];
            if (c == quote)
            {
                i++;
                break;
            }

            if (c == '\n')
            {
                // An unfinished string: stop at the end of the line.
                break;
            }

            if (c == '\\' && i + 1 < source.Length)
            {
                var next = source[i + 1];
                if (next == '\n') line++;
                sb.Append(next switch { 'n' => '\n', 't' => '\t', _ => next });
                i += 2;
                continue;
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }

    /// <summary>Whether <c>[[</c>, <c>[=[</c> and so on starts at <paramref name="i"/>.</summary>
    private static bool TryLongBracketLevel(string source, int i, out int level)
    {
        level = 0;
        if (At(source, i) != '[') return false;
        var j = i + 1;
        while (At(source, j) == '=')
        {
            level++;
            j++;
        }

        return At(source, j) == '[';
    }

    private static int SkipLongBracket(string source, int i, int level, ref int line, out string text)
    {
        var open = i + level + 2;
        var close = "]" + new string('=', level) + "]";
        var end = source.IndexOf(close, open, StringComparison.Ordinal);
        if (end < 0) end = source.Length;
        text = source[open..end];
        foreach (var ch in text)
        {
            if (ch == '\n') line++;
        }

        return Math.Min(source.Length, end + close.Length);
    }
}
