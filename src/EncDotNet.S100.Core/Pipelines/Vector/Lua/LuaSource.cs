using System.Text;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Helpers for emitting Lua source from C#. Centralised so that string values
/// flowing from catalogue declarations into generated Lua (e.g. context
/// parameter ids, types, and defaults) are escaped consistently and safely.
/// </summary>
public static class LuaSource
{
    /// <summary>
    /// Escapes a string for inclusion inside a single-quoted Lua string
    /// literal. Handles the backslash and quote delimiters as well as
    /// newline, carriage-return, tab, and other control characters so that
    /// arbitrary catalogue values cannot break out of the literal or inject
    /// Lua syntax.
    /// </summary>
    public static string EscapeLiteral(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        // Decimal escape; pad to 3 digits so a following digit
                        // cannot be misread as part of the escape.
                        sb.Append('\\').Append(((int)c).ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }

        return sb.ToString();
    }
}
