namespace EncDotNet.S100.Viewer.Services.Notifications;

/// <summary>
/// Formatting helpers for notification content so user-facing messages stay
/// compact. In particular, full filesystem paths are noisy in a toast — only
/// the final, relevant segment (the file name, or the directory name for a
/// folder-backed exchange set) is shown.
/// </summary>
internal static class NotificationFormat
{
    /// <summary>
    /// Reduces a filesystem path to its final segment: the file name for a
    /// file (e.g. <c>set.zip</c>) or the directory name for a folder
    /// (e.g. <c>MyExchangeSet</c>). Returns the original value unchanged when
    /// it is null/whitespace or has no meaningful trailing segment (such as a
    /// volume root), so callers never lose information they cannot shorten.
    /// </summary>
    public static string ShortenPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path ?? string.Empty;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
