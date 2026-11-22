using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// One entry of the Chart reference list (#1637): the glyph tile, the name,
/// the S-57 acronym or code and the display category tag (design note D6).
/// Selecting the row expands it in place to the entry's summary.
/// </summary>
internal sealed class ChartReferenceRowViewModel : ChartReferenceListItem
{
    private const int MaxCases = 6;

    private readonly ChartReferenceIndex _index;
    private readonly IChartReferenceRowHost _host;
    private IImage? _glyph;
    private bool _glyphRequested;
    private bool _isExpanded;
    private ChartReferenceRowDetails? _details;

    /// <summary>Creates a row.</summary>
    /// <param name="entry">The feature type or portrayal item.</param>
    /// <param name="index">The index the entry belongs to.</param>
    /// <param name="lead">The glyph the tile shows.</param>
    /// <param name="host">The list, which draws glyphs and opens links.</param>
    public ChartReferenceRowViewModel(ReferenceEntry entry, ChartReferenceIndex index, LeadGlyph lead, IChartReferenceRowHost host)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(lead);
        ArgumentNullException.ThrowIfNull(host);
        Entry = entry;
        _index = index;
        Lead = lead;
        _host = host;
        Code = entry is FeatureTypeEntry { Alias: { Length: > 0 } alias } ? alias : entry.Code;
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

    /// <inheritdoc />
    public override string Name => Entry.Name;

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

    /// <summary>Whether the row is selected and shows its summary.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (SetProperty(ref _isExpanded, value) && value)
                OnPropertyChanged(nameof(Details));
        }
    }

    /// <summary>The summary an expanded row shows, made the first time the row expands.</summary>
    public ChartReferenceRowDetails? Details => _isExpanded ? _details ??= MakeDetails() : _details;

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

    private ChartReferenceRowDetails MakeDetails() => Entry switch
    {
        FeatureTypeEntry feature => FeatureDetails(feature),
        PortrayalItemEntry item => ItemDetails(item),
        _ => new ChartReferenceRowDetails { Definition = Entry.Definition },
    };

    private ChartReferenceRowDetails FeatureDetails(FeatureTypeEntry feature)
    {
        var group = feature.PrimaryViewingGroup;
        var section = ChartReferenceSection.Of(group);
        return new ChartReferenceRowDetails
        {
            Definition = feature.Definition,
            Categories = Categories(feature),
            Section = section?.Title ?? (group is null ? null : Strings.ChartReference_NoSection),
            ViewingGroup = group is null
                ? null
                : group.Name is { Length: > 0 } name
                    ? string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_ViewingGroupFormat, group.Id, name)
                    : group.Id,
            DrawnWith = DrawnWith(feature),
            NoSymbol = !Lead.HasGlyph,
        };
    }

    private ChartReferenceRowDetails ItemDetails(PortrayalItemEntry item)
    {
        var notes = new List<string>();
        if (item.IsFamily)
            notes.Add(string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_FamilyNote, item.Members.Count));
        if (item.IsUnreached)
            notes.Add(Strings.ChartReference_UnreachedNote);
        if (item.Source is { Length: > 0 } source)
            notes.Add(string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_SourceFormat, source));

        var usedBy = item.UsedBy
            .Select(code => new ReferenceKey(_index.Spec, ReferenceKind.FeatureType, code))
            .Select(key => _index.TryGet(key, out var e) ? e : null)
            .OfType<ReferenceEntry>()
            .OrderBy(e => e.Name, StringComparer.CurrentCulture)
            .Select(e => new ChartReferenceLinkViewModel(e.Key, e.Name, [], null, _host.Open))
            .ToList();

        var family = item.Family is { } prefix && _index.TryGet(new ReferenceKey(_index.Spec, ReferenceKind.Symbol, prefix), out var familyEntry)
            ? new ChartReferenceLinkViewModel(familyEntry.Key, familyEntry.Name, [], null, _host.Open)
            : null;

        return new ChartReferenceRowDetails
        {
            Definition = item.Definition,
            Notes = notes,
            UsedBy = usedBy,
            Family = family is null ? [] : [family],
        };
    }

    // D6: one line per display category, by geometry or case.
    private static IReadOnlyList<string> Categories(FeatureTypeEntry feature)
    {
        var own = feature.ViewingGroups.Where(u => u.ViaRule is null).ToList();
        var uses = own.Count > 0 ? own : feature.ViewingGroups;
        var pairs = uses
            .Select(u => (
                Geometry: u.Conditions.FirstOrDefault(c => c is { Kind: RuleConditionKind.Primitive, Operator: ConditionOperator.Equal })?.Value,
                Mode: u.ViewingGroup.DisplayModeName ?? u.ViewingGroup.DisplayModeId))
            .Where(p => p.Mode is not null)
            .Distinct()
            .ToList();
        var modes = pairs.Select(p => p.Mode!).Distinct(StringComparer.Ordinal).ToList();
        if (modes.Count <= 1)
            return modes;

        return pairs
            .GroupBy(p => p.Geometry, StringComparer.OrdinalIgnoreCase)
            .Select(g => string.Format(
                CultureInfo.CurrentCulture,
                Strings.ChartReference_CategoryByGeometry,
                g.Key is null ? Strings.ChartReference_OtherCases : Capitalise(RuleConditionText.Geometry(g.Key)),
                string.Join(" / ", g.Select(p => p.Mode).Distinct(StringComparer.Ordinal))))
            .ToList();
    }

    // Drawn with: each item the rule draws, once, with its cases in words.
    private IReadOnlyList<ChartReferenceLinkViewModel> DrawnWith(FeatureTypeEntry feature)
    {
        bool geometry = feature.PermittedPrimitives.Count != 1;
        return feature.Uses
            .GroupBy(u => (u.ItemKind, u.ItemId))
            .Select(g =>
            {
                var key = new ReferenceKey(_index.Spec, g.Key.ItemKind, g.Key.ItemId);
                var name = _index.TryGet(key, out var entry) && !string.Equals(entry.Name, entry.Code, StringComparison.Ordinal)
                    ? $"{entry.Code} · {entry.Name}"
                    : g.Key.ItemId;
                var cases = g
                    .Select(u => RuleConditionText.Describe(u.Conditions, _index, geometry))
                    .Select(text => text.Length == 0 ? Strings.ChartReference_Always : text)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                if (cases.Count > MaxCases)
                {
                    var more = cases.Count - MaxCases;
                    cases = [.. cases.Take(MaxCases), string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_MoreCases, more)];
                }

                var via = g.All(u => u.ViaRule is not null) ? g.First().ViaRule : null;
                var note = LeadGlyph.DefaultSymbols.Contains(g.Key.ItemId)
                    ? Strings.ChartReference_DefaultSymbolNote
                    : via is null ? null : string.Format(CultureInfo.CurrentCulture, Strings.ChartReference_ViaRule, via);
                return new ChartReferenceLinkViewModel(key, name, cases, note, _host.Open);
            })
            .ToList();
    }

    private static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
}
