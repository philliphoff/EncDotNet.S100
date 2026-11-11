using System.Text.RegularExpressions;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// The Logs panel's search box, parsed (#1174, D16): <c>area:Datasets</c>
/// shows only that area, <c>-area:Commands</c> hides it, and the rest is plain
/// text matched as before. A name with a space is quoted (<c>area:"Own ship"</c>).
/// </summary>
/// <remarks>
/// Several <c>area:</c> tokens show any of their areas; several
/// <c>-area:</c> tokens hide each of theirs. An area matches either its
/// localized name or its id, ignoring case. A token with no value yet
/// (<c>area:</c> while typing) is ignored.
/// </remarks>
internal sealed partial class LogSearchQuery
{
    private const string Prefix = "area:";

    private LogSearchQuery(IReadOnlyList<string> included, IReadOnlyList<string> excluded, string text)
    {
        IncludedAreas = included;
        ExcludedAreas = excluded;
        Text = text;
    }

    /// <summary>The query that matches everything.</summary>
    public static LogSearchQuery Empty { get; } = new([], [], string.Empty);

    /// <summary>The areas of the <c>area:</c> tokens: a row must be in one of them, when there are any.</summary>
    public IReadOnlyList<string> IncludedAreas { get; }

    /// <summary>The areas of the <c>-area:</c> tokens: a row in any of them is hidden.</summary>
    public IReadOnlyList<string> ExcludedAreas { get; }

    /// <summary>The search without its tokens, trimmed: a row's message, area, category or exception must contain it.</summary>
    public string Text { get; }

    /// <summary>Whether the query filters anything.</summary>
    public bool IsEmpty => IncludedAreas.Count == 0 && ExcludedAreas.Count == 0 && Text.Length == 0;

    /// <summary>Parses <paramref name="search"/>.</summary>
    public static LogSearchQuery Parse(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return Empty;
        List<string> included = [];
        List<string> excluded = [];
        var text = Tokens().Replace(search, match =>
        {
            var area = Value(match);
            if (area.Length > 0)
                (match.Groups["not"].Success ? excluded : included).Add(area);
            return string.Empty;
        });
        return new LogSearchQuery(included, excluded, text.Trim());
    }

    /// <summary>Whether a row in <paramref name="areaName"/> (or <paramref name="areaId"/>) passes the area tokens.</summary>
    public bool MatchesArea(string areaName, string areaId)
    {
        foreach (var area in ExcludedAreas)
        {
            if (IsArea(area, areaName, areaId))
                return false;
        }
        if (IncludedAreas.Count == 0)
            return true;
        foreach (var area in IncludedAreas)
        {
            if (IsArea(area, areaName, areaId))
                return true;
        }
        return false;
    }

    /// <summary>
    /// The search with a token for <paramref name="area"/> added (the row
    /// menu's Show only / Hide). Showing only an area replaces the other
    /// <c>area:</c> tokens; either way an opposite token for the same area
    /// goes, so the search never asks for both.
    /// </summary>
    /// <param name="search">The current search.</param>
    /// <param name="area">The area's name, as the row shows it.</param>
    /// <param name="hide">Add <c>-area:</c> (Hide) rather than <c>area:</c> (Show only).</param>
    public static string WithAreaToken(string? search, string area, bool hide)
    {
        ArgumentNullException.ThrowIfNull(area);
        var tokens = new List<string>();
        var text = Tokens().Replace(search ?? string.Empty, match =>
        {
            var value = Value(match);
            var isExcluded = match.Groups["not"].Success;
            var drop = value.Length == 0
                || string.Equals(value, area, StringComparison.OrdinalIgnoreCase)
                || (!hide && !isExcluded);
            if (!drop)
                tokens.Add(match.Value.Trim());
            return string.Empty;
        }).Trim();
        tokens.Add(Token(area, hide));
        if (text.Length > 0)
            tokens.Add(text);
        return string.Join(' ', tokens);
    }

    /// <summary>The token for <paramref name="area"/>: <c>area:Datasets</c>, <c>-area:"Own ship"</c>.</summary>
    public static string Token(string area, bool hide)
    {
        var value = area.Any(char.IsWhiteSpace) ? "\"" + area + "\"" : area;
        return (hide ? "-" : string.Empty) + Prefix + value;
    }

    private static bool IsArea(string token, string areaName, string areaId) =>
        string.Equals(token, areaName, StringComparison.OrdinalIgnoreCase)
        || string.Equals(token, areaId, StringComparison.OrdinalIgnoreCase);

    private static string Value(Match match) =>
        (match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["bare"].Value).Trim();

    // A token starts the search or follows a space: "-area:" or "area:", then a
    // quoted name (the closing quote may still be missing while typing) or a
    // word. The spaces after it go too, so removing it leaves no double gap.
    [GeneratedRegex("""(?<=^|\s)(?<not>-)?area:(?:"(?<quoted>[^"]*)"?|(?<bare>\S*))\s*""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Tokens();
}
