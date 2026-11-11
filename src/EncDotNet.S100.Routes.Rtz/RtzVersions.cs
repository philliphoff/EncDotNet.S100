using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace EncDotNet.S100.Routes.Rtz;

/// <summary>The RTZ versions this library reads and writes, and their namespaces.</summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>1.0</b> (IEC 61174 Ed. 4.0:2015 Annex S): schedule elements are
/// spelled <c>sheduleElement</c>, windows and stay are <c>xsd:time</c>, and
/// <c>extensions</c> holds any elements.</item>
/// <item><b>1.1</b> (the STM extended Annex S) and <b>1.2</b> (IEC PAS
/// 61174-1:2021): <c>scheduleElement</c>, <c>xsd:duration</c> windows,
/// <c>extensions</c> holds <c>extension</c> elements with a manufacturer and
/// name. 1.2 lets legs carry extensions too and makes the waypoint
/// <c>revision</c> mandatory.</item>
/// </list>
/// </remarks>
public static partial class RtzVersions
{
    /// <summary>RTZ 1.0.</summary>
    public const string V1_0 = "1.0";

    /// <summary>RTZ 1.1.</summary>
    public const string V1_1 = "1.1";

    /// <summary>RTZ 1.2.</summary>
    public const string V1_2 = "1.2";

    /// <summary>The newest version, which the writer uses by default.</summary>
    public const string Latest = V1_2;

    /// <summary>The supported versions, newest first.</summary>
    public static IReadOnlyList<string> Supported { get; } = [V1_2, V1_1, V1_0];

    /// <summary>Whether <paramref name="version"/> is one of <see cref="Supported"/>.</summary>
    /// <param name="version">A version, e.g. <c>"1.1"</c>.</param>
    /// <returns><c>true</c> when it's supported.</returns>
    public static bool IsSupported(string? version) => version is not null && Supported.Contains(version);

    /// <summary>The RTZ namespace of <paramref name="version"/>, e.g. <c>http://www.cirm.org/RTZ/1/0</c>.</summary>
    /// <param name="version">A version as <c>"major.minor"</c>.</param>
    /// <returns>The namespace.</returns>
    public static XNamespace NamespaceOf(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var parts = version.Split('.');
        if (parts.Length != 2 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
            throw new ArgumentException($"'{version}' isn't an RTZ version (major.minor).", nameof(version));
        return XNamespace.Get($"http://www.cirm.org/RTZ/{parts[0]}/{parts[1]}");
    }

    /// <summary>The version an RTZ namespace names, or <c>null</c> when it isn't one.</summary>
    /// <param name="ns">A namespace.</param>
    /// <returns>The version, e.g. <c>"1.2"</c>.</returns>
    public static string? VersionOf(XNamespace ns)
    {
        ArgumentNullException.ThrowIfNull(ns);
        var match = NamespacePattern().Match(ns.NamespaceName);
        return match.Success && match.Length == ns.NamespaceName.Length
            ? $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}"
            : null;
    }

    /// <summary>Whether <paramref name="ns"/> is an RTZ namespace of any version.</summary>
    /// <param name="ns">A namespace.</param>
    /// <returns><c>true</c> for <c>http://www.cirm.org/RTZ/m/n</c>.</returns>
    public static bool IsRtzNamespace(XNamespace ns) => VersionOf(ns) is not null;

    /// <summary>
    /// Recognises an RTZ file from its first few kilobytes: a <c>route</c>
    /// root in an RTZ namespace. The version comes from the namespace.
    /// </summary>
    /// <param name="head">The start of the file, as text.</param>
    /// <returns>The version the file declares, or <c>null</c> when it isn't RTZ.</returns>
    public static string? Sniff(string head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (!RootPattern().IsMatch(head))
            return null;
        var match = NamespacePattern().Match(head);
        return match.Success ? $"{match.Groups["major"].Value}.{match.Groups["minor"].Value}" : null;
    }

    [GeneratedRegex(@"http://www\.cirm\.org/RTZ/(?<major>\d+)/(?<minor>\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex NamespacePattern();

    [GeneratedRegex(@"<(?:[A-Za-z_][\w.-]*:)?route[\s>]")]
    private static partial Regex RootPattern();
}
