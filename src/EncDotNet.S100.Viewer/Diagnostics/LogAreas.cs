namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Maps a logger category (usually a type's full name) to a short, stable
/// area id the Logs panel shows instead of the category (#1080): "Library",
/// "SECOM", "Updates", "AIS"… Ids are English and never localized; the panel
/// looks up a display name for each with <c>Strings.LogAreaName</c>.
/// </summary>
internal static class LogAreas
{
    /// <summary>The area of a category no rule matches outside the S-100 libraries (Microsoft.*, System.*).</summary>
    public const string System = "System";

    // First match wins, so the more specific rules come first. Matched
    // case-sensitively (namespaces and type names are PascalCase, so "Ais"
    // does not match "Raise") against the whole category.
    private static readonly (string Fragment, string Area)[] Rules =
    [
        ("Viewer.Command", "Commands"),
        ("Secom", "SECOM"),
        ("Updates", "Updates"),
        ("GitHubRelease", "Updates"),
        ("Ais", "AIS"),
        ("OwnShip", "OwnShip"),
        ("NavigationalWarning", "Warnings"),
        (".Mcp", "MCP"),
        ("McpServer", "MCP"),
        ("Library", "Library"),
        ("Collections", "Library"),
        ("UserCatalogue", "Library"),
        ("PassageCheck", "Routes"),
        ("Route", "Routes"),
        ("LazyLoading", "Datasets"),
        ("ExchangeSet", "Datasets"),
        ("DatasetLoad", "Datasets"),
        (".Datasets.", "Datasets"),
        ("Renderers", "Rendering"),
        ("Render", "Rendering"),
        ("Portrayal", "Rendering"),
        ("DynamicSource", "Rendering"),
        ("DataMaintenance", "Storage"),
        ("Cache", "Storage"),
    ];

    /// <summary>All the area ids <see cref="Resolve"/> can return from a rule, plus <see cref="System"/> and "App".</summary>
    public static IReadOnlyList<string> KnownAreas { get; } =
        [.. Rules.Select(r => r.Area).Append(System).Append("App").Distinct(StringComparer.Ordinal)];

    /// <summary>The area id for <paramref name="category"/>.</summary>
    /// <remarks>
    /// A viewer category no rule matches is "App"; a Microsoft or System
    /// category is <see cref="System"/>; anything else is the category's last
    /// dotted segment (its type name), which is still shorter than the category.
    /// </remarks>
    public static string Resolve(string category)
    {
        if (string.IsNullOrEmpty(category))
            return "App";

        foreach (var (fragment, area) in Rules)
        {
            if (category.Contains(fragment, StringComparison.Ordinal))
                return area;
        }

        if (category.StartsWith("EncDotNet.S100.Viewer", StringComparison.Ordinal))
            return "App";
        if (category.StartsWith("Microsoft.", StringComparison.Ordinal) || category.StartsWith("System.", StringComparison.Ordinal))
            return System;

        var dot = category.LastIndexOf('.');
        return dot >= 0 && dot < category.Length - 1 ? category[(dot + 1)..] : category;
    }
}
