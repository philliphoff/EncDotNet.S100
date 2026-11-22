using System.Globalization;
using Avalonia.Media;
using EncDotNet.S100.Datasets.Pipelines.Reference;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services.ChartReference;
using EncDotNet.S100.Viewer.ViewModels.Legends;

namespace EncDotNet.S100.Viewer.ViewModels.ChartReference;

/// <summary>
/// A symbol's, line style's, area fill's or family's entry (design note §4.4,
/// C2; R7, #1650): Day, Dusk and Night side by side, its description, the
/// display settings it depends on, its family or members, where it came
/// from, Used by with each case, and the line styles and fills it is used
/// inside or the symbols a line style or fill places.
/// </summary>
internal sealed class SymbolEntryViewModel : ReferenceEntryViewModel
{
    private readonly PortrayalItemEntry _item;

    /// <summary>Creates the entry view.</summary>
    /// <param name="item">The symbol, line style, area fill or family.</param>
    /// <param name="context">The host.</param>
    public SymbolEntryViewModel(PortrayalItemEntry item, ReferenceEntryContext context)
        : base(item, context, LeadGlyph.For(item))
    {
        _item = item;
        Palettes =
        [
            new ReferencePaletteGlyph(PaletteType.Day, Strings.Palette_Day),
            new ReferencePaletteGlyph(PaletteType.Dusk, Strings.Palette_Dusk),
            new ReferencePaletteGlyph(PaletteType.Night, Strings.Palette_Night),
        ];
        DependsOn = SettingsOf(item);
        Family = item.Family is { } prefix && LinkTo(new ReferenceKey(Index.Spec, ReferenceKind.Symbol, prefix)) is { } family
            ? [family]
            : [];
        Members = [.. item.Members.Select(m => LinkTo(new ReferenceKey(Index.Spec, ReferenceKind.Symbol, m))).OfType<ChartReferenceLinkViewModel>()];
        Origin = OriginOf(item, Index.Spec.Name);
        UsedBy = UsedByOf(item);
        UsedIn = [.. item.UsedIn.Select(key => LinkTo(key)).OfType<ChartReferenceLinkViewModel>()];
        Places =
        [
            .. item.Places
                .Select(id => LinkTo(new ReferenceKey(Index.Spec, ReferenceKind.Symbol, id)))
                .OfType<ChartReferenceLinkViewModel>(),
        ];
    }

    /// <inheritdoc />
    public override string CodeLine => _item.Code;

    /// <inheritdoc />
    public override string KindLabel => _item switch
    {
        { IsFamily: true } => Strings.Reference_Kind_Family,
        { Key.Kind: ReferenceKind.LineStyle } => Strings.Reference_Kind_LineStyle,
        { Key.Kind: ReferenceKind.AreaFill } => Strings.Reference_Kind_AreaFill,
        _ => Strings.Reference_Kind_Symbol,
    };

    /// <summary>The glyph in Day, Dusk and Night, side by side.</summary>
    public IReadOnlyList<ReferencePaletteGlyph> Palettes { get; }

    /// <summary>Whether it shows the palettes: it has a glyph.</summary>
    public bool ShowsPalettes => HasGlyph;

    /// <summary>The display settings whose value chooses it, each with the values in words.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> DependsOn { get; }

    /// <summary>Whether a display setting chooses it.</summary>
    public bool HasDependsOn => DependsOn.Count > 0;

    /// <summary>The family it belongs to, as a one-item list, or empty.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> Family { get; }

    /// <summary>Whether it belongs to a family.</summary>
    public bool HasFamily => Family.Count > 0;

    /// <summary>A family's members.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> Members { get; }

    /// <summary>Whether it is a family with members.</summary>
    public bool HasMembers => Members.Count > 0;

    /// <summary>
    /// Where it came from: "S-52 Presentation Library (S52Preslib4.0)", or
    /// "New in S-101" and its source; <see langword="null"/> when the
    /// catalogue doesn't say.
    /// </summary>
    public string? Origin { get; }

    /// <summary>Whether it says where it came from.</summary>
    public bool HasOrigin => Origin is not null;

    /// <summary>Whether it is new in the product rather than from the S-52 Presentation Library.</summary>
    public bool IsNew => _item.Source is { Length: > 0 } source && !IsPresentationLibrary(source);

    /// <summary>Whether no portrayal rule draws it.</summary>
    public bool IsUnreached => _item.IsUnreached;

    /// <summary>The feature types that use it, each with the cases in which it draws it.</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> UsedBy { get; }

    /// <summary>Whether a feature type uses it.</summary>
    public bool HasUsedBy => UsedBy.Count > 0;

    /// <summary>The line styles and area fills that place the symbol along a line or as a pattern (#1654).</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> UsedIn { get; }

    /// <summary>Whether a line style or area fill places the symbol.</summary>
    public bool HasUsedIn => UsedIn.Count > 0;

    /// <summary>The symbols a line style places along its line or an area fill tiles as its pattern (#1654).</summary>
    public IReadOnlyList<ChartReferenceLinkViewModel> Places { get; }

    /// <summary>Whether the line style or fill places symbols.</summary>
    public bool HasPlaces => Places.Count > 0;

    /// <inheritdoc />
    protected override bool DrawsPalettes => true;

    /// <inheritdoc />
    protected override void OnPaletteGlyph(PaletteType palette, IImage? glyph)
    {
        if (Palettes.FirstOrDefault(p => p.Palette == palette) is { } tile)
            tile.Glyph = glyph;
    }

    private static bool IsPresentationLibrary(string source) =>
        source.StartsWith("S52", StringComparison.OrdinalIgnoreCase);

    private static string? OriginOf(PortrayalItemEntry item, string spec) => item.Source switch
    {
        null or "" => null,
        var source when IsPresentationLibrary(source) => string.Format(CultureInfo.CurrentCulture, Strings.Reference_FromPresLib, source),
        var source when string.Equals(source, "new", StringComparison.OrdinalIgnoreCase) => string.Format(CultureInfo.CurrentCulture, Strings.Reference_NewIn, spec),
        var source => string.Format(CultureInfo.CurrentCulture, Strings.Reference_NewInFrom, spec, source),
    };

    // The uses of the item, or of a family's members, by each feature type.
    private IEnumerable<SymbolUse> UsesIn(FeatureTypeEntry feature) =>
        feature.Uses.Where(u => string.Equals(u.ItemId, _item.Code, StringComparison.OrdinalIgnoreCase)
            || _item.IsFamily && _item.Members.Contains(u.ItemId, StringComparer.OrdinalIgnoreCase));

    private List<ChartReferenceLinkViewModel> UsedByOf(PortrayalItemEntry item) =>
    [
        .. item.UsedBy
            .Select(code => Index.TryGet(new ReferenceKey(Index.Spec, ReferenceKind.FeatureType, code), out var e) ? e as FeatureTypeEntry : null)
            .OfType<FeatureTypeEntry>()
            .OrderBy(f => f.Name, StringComparer.CurrentCulture)
            .Select(f => new ChartReferenceLinkViewModel(
                f.Key,
                f.Name,
                FeatureTypeEntryViewModel.CasesOf(UsesIn(f), Index, f.PermittedPrimitives.Count != 1),
                null,
                Context.Open)),
    ];

    // The context parameters the uses of the item test, with the values they test for.
    private List<ChartReferenceLinkViewModel> SettingsOf(PortrayalItemEntry item)
    {
        var conditions = Index.FeatureTypes
            .Where(f => item.UsedBy.Contains(f.Code, StringComparer.OrdinalIgnoreCase))
            .SelectMany(UsesIn)
            .SelectMany(u => u.Conditions)
            .Where(c => c is { Kind: RuleConditionKind.ContextParameter, Subject: not null });
        return
        [
            .. conditions
                .GroupBy(c => c.Subject!, StringComparer.OrdinalIgnoreCase)
                .Select(g => LinkTo(
                    new ReferenceKey(Index.Spec, ReferenceKind.DisplaySetting, g.Key),
                    [.. g.Select(c => RuleConditionText.Describe(c, Index)).Distinct(StringComparer.Ordinal)]))
                .OfType<ChartReferenceLinkViewModel>(),
        ];
    }
}

/// <summary>A symbol entry's glyph in one palette, on that palette's chart background.</summary>
internal sealed class ReferencePaletteGlyph : ViewModelBase
{
    private IImage? _glyph;

    /// <summary>Creates the tile.</summary>
    /// <param name="palette">The palette.</param>
    /// <param name="label">Its name: "Day", "Dusk" or "Night".</param>
    public ReferencePaletteGlyph(PaletteType palette, string label)
    {
        Palette = palette;
        Label = label;
        Tile = LegendViewModel.TileOf(palette);
    }

    /// <summary>The palette.</summary>
    public PaletteType Palette { get; }

    /// <summary>Its name: "Day", "Dusk" or "Night".</summary>
    public string Label { get; }

    /// <summary>The palette's chart background.</summary>
    public IBrush Tile { get; }

    /// <summary>The glyph in the palette, or <see langword="null"/> until it is drawn.</summary>
    public IImage? Glyph
    {
        get => _glyph;
        set => SetProperty(ref _glyph, value);
    }
}
