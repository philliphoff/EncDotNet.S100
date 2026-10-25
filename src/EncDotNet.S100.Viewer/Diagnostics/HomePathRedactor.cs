namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Replaces the user's home directory with <c>~</c> in text that leaves the
/// machine in an exported report (log tails, crash logs), so paths do not
/// reveal the user's account name.
/// </summary>
internal static class HomePathRedactor
{
    /// <summary>
    /// Returns <paramref name="text"/> with every occurrence of the home
    /// directory replaced by <c>~</c>. Matching ignores case on Windows and
    /// also covers the forward-slash spelling of a Windows path.
    /// </summary>
    /// <param name="text">Text to redact; <see langword="null"/> is returned
    /// unchanged.</param>
    /// <param name="homeDirectory">Home directory to replace; defaults to the
    /// current user's profile directory.</param>
    public static string? Redact(string? text, string? homeDirectory = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var home = homeDirectory ?? SafeUserProfile();
        if (string.IsNullOrEmpty(home))
        {
            return text;
        }

        home = home.TrimEnd('/', '\\');

        // A root home ("/" or "C:\") would turn every path into "~…".
        if (home.Length <= 3)
        {
            return text;
        }

        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var result = text.Replace(home, "~", comparison);
        var altHome = home.Replace('\\', '/');
        if (!string.Equals(altHome, home, StringComparison.Ordinal))
        {
            result = result.Replace(altHome, "~", comparison);
        }

        return result;
    }

    private static string? SafeUserProfile()
    {
        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }
        catch
        {
            return null;
        }
    }
}
