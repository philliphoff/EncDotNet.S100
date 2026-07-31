using System.Reflection;

namespace EncDotNet.S100.Cli.Infrastructure;

/// <summary>
/// Describes the running CLI assembly version.
/// </summary>
/// <param name="Version">The version used for release comparisons.</param>
/// <param name="InformationalVersion">The complete informational version.</param>
internal sealed record CliVersionInfo(string Version, string InformationalVersion)
{
    /// <summary>
    /// Gets whether this is the default unversioned development build.
    /// </summary>
    public bool IsDevelopmentBuild =>
        Version.StartsWith("0.0.0", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads version information from an assembly.
    /// </summary>
    public static CliVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var separator = informationalVersion.IndexOf('+');
        var version = separator >= 0
            ? informationalVersion[..separator]
            : informationalVersion;

        return new CliVersionInfo(version, informationalVersion);
    }
}
