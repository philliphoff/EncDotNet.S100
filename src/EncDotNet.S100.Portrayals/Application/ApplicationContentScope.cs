using System.Reflection;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// The portrayal part of a tile disk-cache content scope for a portrayal
/// defined in code, which has no catalogue files to hash.
/// </summary>
/// <remarks>
/// <para>
/// The warm tile disk cache keys tiles by a dataset's content scope and a
/// hash of its drawing instructions. A catalogue-backed product folds in the
/// catalogue's content, so an edited symbol invalidates its tiles. For a
/// portrayal defined in code the equivalent is the code itself: its symbols,
/// palette and drawing rules ship in assemblies. The scope is therefore the
/// style's name and version constant plus the toolkit assembly's version:
/// </para>
/// <code>app:S-104@1|toolkit:1.2.0+abc123</code>
/// <para>
/// Bump the style's version whenever its symbols, palette or anything else
/// the instruction hash doesn't cover changes; that drops every warm tile
/// drawn with the old style. A new toolkit release drops them all.
/// </para>
/// <para>
/// A processor combines it with its dataset's content hash, as the vector
/// products do: <c>ds:{hash}|{scope}</c>.
/// </para>
/// </remarks>
public static class ApplicationContentScope
{
    /// <summary>
    /// The toolkit assembly's version: its informational version (which
    /// carries the source revision in a CI build) or, when that is absent,
    /// its assembly version.
    /// </summary>
    public static string ToolkitVersion { get; } = ReadToolkitVersion();

    /// <summary>The content scope of the code-defined style <paramref name="style"/> at <paramref name="version"/>.</summary>
    /// <param name="style">The style's name, e.g. <c>"S-104"</c>.</param>
    /// <param name="version">The style's version constant, at least 1.</param>
    /// <returns><c>app:{style}@{version}|toolkit:{<see cref="ToolkitVersion"/>}</c>.</returns>
    public static string For(string style, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(style);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(version);
        return $"app:{style}@{version}|toolkit:{ToolkitVersion}";
    }

    private static string ReadToolkitVersion()
    {
        var assembly = typeof(ApplicationContentScope).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
    }
}
