namespace EncDotNet.S100.Viewer;

/// <summary>
/// Writes a keyboard shortcut the way the platform does: <c>⌘E</c> and
/// <c>⇧⌘Z</c> on macOS, <c>Ctrl+E</c> and <c>Ctrl+Shift+Z</c> elsewhere.
/// </summary>
internal static class ShortcutText
{
    /// <summary>
    /// The shortcut for the platform's command modifier (⌘ on macOS, Ctrl
    /// elsewhere) plus <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The key, e.g. <c>"E"</c>.</param>
    /// <param name="shift">Whether Shift is held too.</param>
    public static string Command(string key, bool shift = false)
        => Command(key, shift, OperatingSystem.IsMacOS());

    /// <summary>Test seam for <see cref="Command(string, bool)"/> on a given platform.</summary>
    internal static string Command(string key, bool shift, bool macOS)
        => macOS
            ? (shift ? "⇧⌘" : "⌘") + key
            : (shift ? "Ctrl+Shift+" : "Ctrl+") + key;
}
