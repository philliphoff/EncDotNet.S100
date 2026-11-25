using System.Text;
using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Cli.Reference;

/// <summary>
/// Where each page and file of an exported chart reference lives (R14,
/// #1723): one folder per kind of entry, glyphs by palette, and the shared
/// style sheet and script.
/// </summary>
/// <remarks>
/// Paths are relative to the export folder and use <c>/</c>. A code is used
/// as its file name when it is plain letters, digits, <c>.</c>, <c>-</c> and
/// <c>_</c>; any other code is made safe and given a short stable hash, so
/// two codes never share a file and a re-run writes the same names.
/// </remarks>
internal static class ReferenceSite
{
    /// <summary>The index page.</summary>
    public const string IndexPage = "index.html";

    /// <summary>The style sheet every page links.</summary>
    public const string StyleSheet = "assets/reference.css";

    /// <summary>The index page's search script.</summary>
    public const string SearchScript = "assets/search.js";

    /// <summary>
    /// The file that marks a folder as an export, so a re-run may clear it
    /// and a folder holding anything else is never emptied.
    /// </summary>
    public const string Marker = ".chart-reference";

    /// <summary>The folder of an entry kind's pages.</summary>
    /// <param name="kind">The kind.</param>
    public static string Folder(ReferenceKind kind) => kind switch
    {
        ReferenceKind.FeatureType => "feature-types",
        ReferenceKind.Symbol => "symbols",
        ReferenceKind.LineStyle => "line-styles",
        ReferenceKind.AreaFill => "area-fills",
        ReferenceKind.Attribute => "attributes",
        ReferenceKind.DisplaySetting => "display-settings",
        _ => "other",
    };

    /// <summary>An entry's page, such as <c>feature-types/LateralBeacon.html</c>.</summary>
    /// <param name="key">The entry.</param>
    public static string PageOf(ReferenceKey key) => $"{Folder(key.Kind)}/{FileName(key.Code)}.html";

    /// <summary>A glyph file, such as <c>glyphs/day/symbols/BCNLAT21.png</c>.</summary>
    /// <param name="palette">The palette's folder name: <c>day</c>, <c>dusk</c> or <c>night</c>.</param>
    /// <param name="group">The glyph's group, such as <c>symbols</c> or <c>colours</c>.</param>
    /// <param name="name">The glyph's name within the group.</param>
    public static string GlyphOf(string palette, string group, string name) => $"glyphs/{palette}/{group}/{FileName(name)}.png";

    /// <summary>
    /// A path from one page to another file of the export: pages in a
    /// folder reach the root through <c>../</c>.
    /// </summary>
    /// <param name="from">The page the link is on.</param>
    /// <param name="to">The file linked to.</param>
    public static string Href(string from, string to)
    {
        var depth = from.Count(c => c == '/');
        var up = new StringBuilder();
        for (var i = 0; i < depth; i++)
            up.Append("../");
        return up + to;
    }

    /// <summary>A code as a file name: as it is when safe, else made safe with a stable hash.</summary>
    /// <param name="code">The code.</param>
    public static string FileName(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var safe = new StringBuilder(code.Length);
        foreach (var c in code)
            safe.Append(char.IsAsciiLetterOrDigit(c) || c is '-' or '_' || (c == '.' && safe.Length > 0) ? c : '_');
        if (safe.Length > 0 && safe.ToString() == code)
            return code;

        // FNV-1a over the UTF-16 code units: stable across runs and platforms.
        var hash = 2166136261u;
        foreach (var c in code)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return $"{safe}-{hash:x8}";
    }

    /// <summary>An anchor id for a listed value on its attribute's page, such as <c>v-3</c>.</summary>
    /// <param name="code">The listed value's code.</param>
    public static string ValueAnchor(string code) => "v-" + FileName(code);
}
