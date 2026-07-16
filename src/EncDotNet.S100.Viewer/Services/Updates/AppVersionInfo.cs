using System.Globalization;
using System.Reflection;

namespace EncDotNet.S100.Viewer.Services.Updates;

/// <summary>
/// Read-only description of the running application's version, surfaced by
/// the About dialog. Derived from assembly metadata so it always reflects
/// the version injected at build time (<c>-p:Version=…</c>).
/// </summary>
/// <param name="Version">
/// The release version without build metadata (e.g. <c>"2.4.1"</c> or the
/// local <c>"0.0.0-dev"</c> default from <c>Directory.Build.props</c>).
/// </param>
/// <param name="InformationalVersion">
/// The full informational version including any <c>+&lt;sha&gt;</c> source
/// revision suffix appended by the SDK (e.g. <c>"2.4.1+a1f9c20…"</c>).
/// </param>
/// <param name="CommitSha">
/// The short git commit SHA parsed from <paramref name="InformationalVersion"/>,
/// or <see langword="null"/> when no source revision is embedded.
/// </param>
/// <param name="BuildDate">
/// The UTC build date embedded via the <c>BuildDate</c> assembly metadata,
/// or <see langword="null"/> when unavailable.
/// </param>
internal sealed record AppVersionInfo(
    string Version,
    string InformationalVersion,
    string? CommitSha,
    DateOnly? BuildDate)
{
    /// <summary>
    /// True when this is an unversioned local development build (the
    /// <c>0.0.0-dev</c> / <c>0.0.0</c> default). Update checks are skipped
    /// for such builds because there is no meaningful release to compare.
    /// </summary>
    public bool IsDevelopmentBuild =>
        Version.StartsWith("0.0.0", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Supplies the running application's <see cref="AppVersionInfo"/>.
/// </summary>
internal interface IAppVersionProvider
{
    /// <summary>Gets the current application version information.</summary>
    AppVersionInfo Current { get; }
}

/// <summary>
/// Default <see cref="IAppVersionProvider"/> that reads version metadata
/// from a supplied assembly (the entry assembly in the running app).
/// </summary>
internal sealed class AssemblyAppVersionProvider : IAppVersionProvider
{
    /// <inheritdoc />
    public AppVersionInfo Current { get; }

    /// <summary>
    /// Creates a provider reading from <paramref name="assembly"/>, or the
    /// entry/executing assembly when none is supplied.
    /// </summary>
    public AssemblyAppVersionProvider(Assembly? assembly = null)
    {
        var asm = assembly
            ?? Assembly.GetEntryAssembly()
            ?? typeof(AssemblyAppVersionProvider).Assembly;

        Current = FromAssembly(asm);
    }

    /// <summary>
    /// Builds an <see cref="AppVersionInfo"/> from an assembly's
    /// informational version and <c>BuildDate</c> metadata. Exposed for
    /// unit testing the parsing logic.
    /// </summary>
    internal static AppVersionInfo FromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        var (version, sha) = ParseInformationalVersion(informational);

        DateOnly? buildDate = null;
        var buildDateRaw = assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => string.Equals(a.Key, "BuildDate", StringComparison.OrdinalIgnoreCase))
            ?.Value;
        if (!string.IsNullOrWhiteSpace(buildDateRaw)
            && DateOnly.TryParse(buildDateRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            buildDate = parsed;
        }

        return new AppVersionInfo(version, informational, sha, buildDate);
    }

    /// <summary>
    /// Splits an informational version into its release version (the part
    /// before any <c>+</c> build-metadata) and the short commit SHA (the
    /// first 7 characters of the build metadata when present).
    /// </summary>
    internal static (string Version, string? Sha) ParseInformationalVersion(string informational)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return ("0.0.0", null);

        var plus = informational.IndexOf('+');
        if (plus < 0)
            return (informational, null);

        var version = informational[..plus];
        var metadata = informational[(plus + 1)..];
        if (string.IsNullOrWhiteSpace(metadata))
            return (version, null);

        // SourceLink embeds the full 40-char SHA; show a short form.
        var sha = metadata.Length > 7 ? metadata[..7] : metadata;
        return (version, sha);
    }
}
