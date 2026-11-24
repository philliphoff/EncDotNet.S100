namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>Where the CLI keeps its files in the user's profile.</summary>
internal static class CliPaths
{
    /// <summary>
    /// The user's local application data folder, whether or not it exists
    /// yet: <c>%LOCALAPPDATA%</c> on Windows, <c>~/Library/Application Support</c>
    /// on macOS, and <c>$XDG_DATA_HOME</c> or <c>~/.local/share</c> on Linux.
    /// </summary>
    /// <remarks>
    /// <see cref="Environment.GetFolderPath(Environment.SpecialFolder)"/>
    /// returns an empty string for a folder that doesn't exist, such as
    /// <c>~/.local/share</c> for a new Linux user or in a container, and a
    /// path built on that is relative: the CLI's files then landed in
    /// whatever folder it was run from. Not verifying gives the real folder,
    /// which the files' writers create.
    /// </remarks>
    public static string LocalApplicationData { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
}
