using System.Text;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Replaces the user's home directory with <c>~</c> in text the viewer
/// exports (the feedback report, <c>logs.txt</c>, <c>get_recent_logs</c>),
/// so paths such as <c>/Users/alice/Charts/US5MA1BO.000</c> don't reveal the
/// account name (#1080).
/// </summary>
internal static class HomePathRedactor
{
    private static readonly string? CurrentHome = ResolveHome();

    /// <summary>Redacts the current user's home directory in <paramref name="text"/>.</summary>
    public static string Redact(string text) => Redact(text, CurrentHome);

    /// <summary>
    /// Redacts the current user's home directory in <paramref name="text"/>;
    /// <see langword="null"/> stays <see langword="null"/>.
    /// </summary>
    public static string? RedactOrNull(string? text) => text is null ? null : Redact(text, CurrentHome);

    /// <summary>
    /// Replaces every occurrence of <paramref name="home"/> in
    /// <paramref name="text"/> that is a whole path prefix (followed by a
    /// separator, a quote, whitespace or the end of the text) with <c>~</c>,
    /// so <c>/home/al</c> doesn't turn <c>/home/alice</c> into <c>~ice</c>.
    /// Also matches the home written with the other separator, and on Windows
    /// ignores case, as its file system does.
    /// </summary>
    /// <param name="text">The text to redact.</param>
    /// <param name="home">The home directory, or <see langword="null"/> to leave the text as it is.</param>
    /// <param name="ignoreCase">Whether to match case-insensitively; defaults to the platform's file-system rule.</param>
    public static string Redact(string text, string? home, bool? ignoreCase = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.IsNullOrEmpty(home) || text.Length == 0)
            return text;

        home = home.TrimEnd('/', '\\');
        // A home of "/" or "C:" would redact every path; leave the text alone.
        if (home.Length < 3)
            return text;

        var comparison = (ignoreCase ?? OperatingSystem.IsWindows())
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        text = ReplacePrefix(text, home, comparison);
        var otherSeparator = home.Contains('\\') ? home.Replace('\\', '/') : home.Replace('/', '\\');
        if (!string.Equals(otherSeparator, home, StringComparison.Ordinal))
            text = ReplacePrefix(text, otherSeparator, comparison);
        // JSON and C# string escapes double the backslashes.
        if (home.Contains('\\'))
            text = ReplacePrefix(text, home.Replace("\\", "\\\\"), comparison);
        return text;
    }

    private static string ReplacePrefix(string text, string home, StringComparison comparison)
    {
        var index = text.IndexOf(home, comparison);
        if (index < 0)
            return text;

        var sb = new StringBuilder(text.Length);
        var start = 0;
        while (index >= 0)
        {
            var end = index + home.Length;
            if (IsBoundary(text, end))
            {
                sb.Append(text, start, index - start).Append('~');
                start = end;
            }
            index = text.IndexOf(home, end, comparison);
        }
        sb.Append(text, start, text.Length - start);
        return sb.ToString();
    }

    private static bool IsBoundary(string text, int index)
    {
        if (index >= text.Length)
            return true;
        var c = text[index];
        return c is '/' or '\\' or '"' or '\'' or ')' or ']' or ':' or ',' or ';' || char.IsWhiteSpace(c);
    }

    private static string? ResolveHome()
    {
        try
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? null : home;
        }
        catch
        {
            return null;
        }
    }
}
