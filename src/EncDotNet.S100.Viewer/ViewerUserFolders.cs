namespace EncDotNet.S100.Viewer;

/// <summary>The user's profile folders SoundCharts keeps its files in.</summary>
/// <remarks>
/// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/> returns
/// an empty string for a folder that doesn't exist yet, such as
/// <c>~/.config</c> or <c>~/.local/share</c> on a new Linux account or when
/// <c>XDG_CONFIG_HOME</c> or <c>XDG_DATA_HOME</c> names a new folder. A path
/// built on that is relative, so settings, caches and crash markers would
/// land in whatever folder SoundCharts was started from. These ask without
/// verifying, which gives the real folder; the files' writers create it.
/// </remarks>
internal static class ViewerUserFolders
{
    /// <summary>
    /// The roaming application data folder, whether or not it exists yet:
    /// <c>%APPDATA%</c> on Windows, <c>~/Library/Application Support</c> on
    /// macOS, and <c>$XDG_CONFIG_HOME</c> or <c>~/.config</c> on Linux.
    /// </summary>
    public static string ApplicationData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);

    /// <summary>
    /// The local application data folder, whether or not it exists yet:
    /// <c>%LOCALAPPDATA%</c> on Windows, <c>~/Library/Application Support</c>
    /// on macOS, and <c>$XDG_DATA_HOME</c> or <c>~/.local/share</c> on Linux.
    /// </summary>
    public static string LocalApplicationData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
}
