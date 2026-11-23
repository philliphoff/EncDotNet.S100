using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// One entry of the Chart reference list (#1637): the glyph tile, the name,
/// the S-57 acronym or code and the display category tag (design note D6).
/// Selecting the row opens its entry in the Reference tab (R7, #1650).
/// </summary>
internal sealed partial class ChartReferenceRowViewModel : ChartReferenceListItem
{
    private readonly IChartReferenceRowHost _host;
    private IImage? _glyph;
    private bool _glyphRequested;
    private bool _isSelected;

    /// <summary>Creates a row.</summary>
    /// <param name="entry">The feature type or portrayal item.</param>
    /// <param name="lead">The glyph the tile shows.</param>
    /// <param name="host">The list, which draws glyphs.</param>
    public ChartReferenceRowViewModel(ReferenceEntry entry, LeadGlyph lead, IChartReferenceRowHost host)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(lead);
        ArgumentNullException.ThrowIfNull(host);
        Entry = entry;
        Lead = lead;
        _host = host;
        Code = entry is FeatureTypeEntry { Alias: { Length: > 0 } alias } ? alias : entry.Code;
        Name = NameOf(entry);
        CategoryTag = entry switch
        {
            FeatureTypeEntry feature => CategoryTagOf(feature.PrimaryViewingGroup),
            PortrayalItemEntry { IsFamily: true } => Strings.ChartReference_Tag_Family,
            PortrayalItemEntry { Key.Kind: ReferenceKind.LineStyle } => Strings.ChartReference_Tag_LineStyle,
            PortrayalItemEntry { Key.Kind: ReferenceKind.AreaFill } => Strings.ChartReference_Tag_AreaFill,
            _ => null,
        };
    }

    /// <summary>The entry the row shows.</summary>
    public ReferenceEntry Entry { get; }

    /// <summary>The entry's key.</summary>
    public ReferenceKey Key => Entry.Key;

    /// <summary>
    /// The row's title: the entry's name or, for a symbol, line style or
    /// fill whose catalogue name is only its code, the first line of its
    /// description in sentence case (#1657), so the code goes on the
    /// secondary line as a feature type's S-57 acronym does.
    /// </summary>
    public override string Name { get; }

    /// <inheritdoc />
    public override bool IsRow => true;

    /// <summary>The S-57 acronym of a feature type, else the entry's code.</summary>
    public string Code { get; }

    /// <summary>Whether the code differs from the name, so the row shows it.</summary>
    public bool ShowsCode => !string.Equals(Code, Name, StringComparison.Ordinal);

    /// <summary>
    /// The tag after the name: a feature type's most visible display category
    /// ("Base", "Standard" or "Other", D6), or the kind of a line style, area
    /// fill or family; <see langword="null"/> for none.
    /// </summary>
    public string? CategoryTag { get; }

    /// <summary>Whether the row shows a tag.</summary>
    public bool HasCategoryTag => CategoryTag is not null;

    /// <summary>The glyph the tile shows.</summary>
    public LeadGlyph Lead { get; }

    /// <summary>Whether the tile shows a glyph; else it says "No symbol of its own".</summary>
    public bool HasGlyph => Lead.HasGlyph;

    /// <summary>Whether the tile is empty because the entry has no symbol of its own.</summary>
    public bool HasNoGlyph => !Lead.HasGlyph;

    /// <summary>The tile under the glyph: the palette's chart background.</summary>
    public IBrush Tile => _host.Tile;

    /// <summary>
    /// The tile's glyph, drawn off the UI thread on first use and kept in the
    /// glyph cache; <see langword="null"/> until it is drawn, or when there is
    /// nothing to draw.
    /// </summary>
    public IImage? Glyph
    {
        get
        {
            if (!_glyphRequested && Lead.HasGlyph)
            {
                _glyphRequested = true;
                _glyph = _host.RequestGlyph(this);
            }

            return _glyph;
        }
    }

    /// <summary>Whether the study page shows the row's tile as selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Sets the glyph once it is drawn.</summary>
    /// <param name="glyph">The glyph, or <see langword="null"/> when there is nothing to draw.</param>
    internal void SetGlyph(IImage? glyph)
    {
        _glyph = glyph;
        OnPropertyChanged(nameof(Glyph));
    }

    /// <summary>Drops the glyph after a palette change, so it is drawn again when next shown.</summary>
    internal void ResetGlyph()
    {
        _glyphRequested = false;
        _glyph = null;
        OnPropertyChanged(nameof(Tile));
        OnPropertyChanged(nameof(Glyph));
    }

    /// <summary>The short tag of a display category: "Base", "Standard" or "Other".</summary>
    /// <param name="group">The viewing group, or <see langword="null"/>.</param>
    /// <returns>The tag, or <see langword="null"/> when the group is in no display mode.</returns>
    internal static string? CategoryTagOf(ViewingGroupInfo? group) => group?.DisplayModeId switch
    {
        null => group?.DisplayModeName,
        "DisplayBase" => Strings.ChartReference_Tag_Base,
        "StandardDisplay" or "Standard" => Strings.ChartReference_Tag_Standard,
        "OtherInformation" or "All" => Strings.ChartReference_Tag_Other,
        _ => group.DisplayModeName ?? group.DisplayModeId,
    };

    /// <summary>
    /// The title of <paramref name="entry"/>'s row: its name, or for a
    /// portrayal item named only by its code, its description's first line
    /// in sentence case. A portrayal item's title gets a space after each
    /// comma directly followed by a letter, as the catalogues often write
    /// "Beacon in general,lateral-r,paper-chart".
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The title.</returns>
    internal static string NameOf(ReferenceEntry entry)
    {
        if (entry is not PortrayalItemEntry { IsFamily: false } item)
            return entry.Name;
        if (!string.Equals(item.Name, item.Code, StringComparison.OrdinalIgnoreCase)
            || FirstLine(item.Definition) is not { Length: > 0 } line)
        {
            return SpaceAfterCommas(item.Name);
        }

        return SentenceCase(SpaceAfterCommas(line));
    }

    /// <summary>
    /// Adds a space after each comma that is directly followed by a letter:
    /// "general,lateral" reads "general, lateral"; "1,000" is unchanged.
    /// </summary>
    /// <param name="text">The catalogue text.</param>
    /// <returns>The text with its commas spaced.</returns>
    internal static string SpaceAfterCommas(string text) => CommaBeforeLetter().Replace(text, ", ");

    /// <summary>
    /// Whether <paramref name="entry"/>'s description says no more than its
    /// row title, <see cref="NameOf"/>: a portrayal item named by a one-line
    /// description. An entry view then shows it once, as its heading.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Whether the description only repeats the title.</returns>
    internal static bool DescriptionRepeatsName(ReferenceEntry entry) =>
        entry is PortrayalItemEntry { IsFamily: false, Definition: { } definition } item
        && string.Equals(item.Name, item.Code, StringComparison.OrdinalIgnoreCase)
        && definition.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Count(l => l.Length > 0) == 1;

    [GeneratedRegex(@",(?=\p{L})")]
    private static partial Regex CommaBeforeLetter();

    // The first non-empty line, without a closing full stop.
    private static string? FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var line = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(l => l.Length > 0);
        return line?.TrimEnd('.', ' ');
    }

    // "THIS OBJECT HAS…" and "this object has…" both read "This object has…";
    // mixed case keeps its capitals (names, acronyms) after the first letter.
    private static string SentenceCase(string text)
    {
        if (!text.Any(char.IsLower))
            text = text.ToLower(CultureInfo.CurrentCulture);
        return Capitalise(text);
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
