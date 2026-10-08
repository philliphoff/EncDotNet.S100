using System.Globalization;
using System.Resources;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// The Library's user-visible text (<c>LibraryText.resx</c>): item tags and
/// details, source status lines and names, shared by every host so the viewer's
/// Library panel and an MCP host describe items in the same words (#792).
/// </summary>
/// <remarks>
/// Lookups use <see cref="CultureInfo.CurrentUICulture"/>; a host that keeps
/// its own UI culture passes it to <see cref="Find"/>. Hosts with their own
/// string tables fall back to this one for the Library's keys.
/// </remarks>
public static class LibraryText
{
    private static readonly ResourceManager Resources =
        new("EncDotNet.S100.Collections.Library.LibraryText", typeof(LibraryText).Assembly);

    /// <summary>The string named <paramref name="name"/>, or <see langword="null"/> when there is none.</summary>
    /// <param name="name">The resource key, e.g. <c>Library_Tag_Queued</c>.</param>
    /// <param name="culture">The culture to look up; <see cref="CultureInfo.CurrentUICulture"/> when null.</param>
    public static string? Find(string name, CultureInfo? culture = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        return Resources.GetString(name, culture ?? CultureInfo.CurrentUICulture);
    }

    /// <summary>The string named <paramref name="name"/>, or the name itself when there is none.</summary>
    internal static string Get(string name) => Find(name) ?? name;

    /// <summary>The curated product title for a spec code ("S-101" → "Electronic Navigational Chart"), or null.</summary>
    /// <param name="spec">The product specification code.</param>
    public static string? SpecDisplayName(string spec) =>
        string.IsNullOrEmpty(spec) ? null : Find("SpecName_" + spec.Replace("-", string.Empty, StringComparison.Ordinal));

    /// <summary>The curated label for an item property key ("riverMiles" → "River miles"), or null.</summary>
    /// <param name="key">The property key.</param>
    public static string? PropertyLabel(string key) =>
        string.IsNullOrEmpty(key) ? null : Find("Library_Property_" + key);
}
