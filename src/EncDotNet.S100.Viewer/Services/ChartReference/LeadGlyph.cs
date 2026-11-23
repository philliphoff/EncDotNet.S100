using EncDotNet.S100.Datasets.Pipelines.Reference;

namespace EncDotNet.S100.Viewer.Services.ChartReference;

/// <summary>What a Chart reference tile shows.</summary>
internal enum LeadGlyphKind
{
    /// <summary>Nothing: the entry has no symbol of its own.</summary>
    None,

    /// <summary>A point symbol (<see cref="LeadGlyph.ItemId"/>).</summary>
    Symbol,

    /// <summary>
    /// An area patch in the plain colour fills' representative colour
    /// (<see cref="LeadGlyph.AreaToken"/>), as the Legend shows an area (#1650).
    /// </summary>
    Area,

    /// <summary>An area fill pattern (<see cref="LeadGlyph.ItemId"/>).</summary>
    AreaFill,

    /// <summary>A catalogue line style (<see cref="LeadGlyph.ItemId"/>).</summary>
    LineStyle,

    /// <summary>A plain line (<see cref="LeadGlyph.Pen"/>).</summary>
    Pen,
}

/// <summary>
/// The glyph a Chart reference row shows on its tile (#1637): for a
/// feature type, the one <see cref="For(FeatureTypeEntry, ChartReferenceIndex, bool)"/>
/// chooses; for a portrayal item, the item itself.
/// </summary>
/// <param name="Kind">What the tile shows.</param>
/// <param name="Key">
/// The <see cref="ChartReferenceGlyphCache"/> key: the item's own key for a
/// symbol, line style or area fill, so feature types that lead with the same
/// symbol share its glyph; the feature type's key for area patches and plain lines.
/// </param>
/// <param name="ItemId">The symbol, line style or area fill id, or <see langword="null"/>.</param>
/// <param name="Tokens">The colour tokens of <see cref="LeadGlyphKind.Area"/>, in order.</param>
/// <param name="Pen">The plain line of <see cref="LeadGlyphKind.Pen"/>.</param>
internal sealed record LeadGlyph(
    LeadGlyphKind Kind,
    ReferenceKey Key,
    string? ItemId = null,
    IReadOnlyList<string>? Tokens = null,
    PenUse? Pen = null)
{
    /// <summary>The most colour tokens an <see cref="LeadGlyphKind.Area"/> glyph keeps.</summary>
    public const int MaxColours = 6;

    /// <summary>
    /// The colour an <see cref="LeadGlyphKind.Area"/> patch is filled with:
    /// the middle of <see cref="Tokens"/> after leaving out <c>NODTA</c>
    /// ("no data") and <c>DEPDW</c> (deep water, the tile's own colour, so a
    /// patch in it wouldn't show). Depth Area's shades give a blue
    /// (<c>DEPVS</c>); a single token is used as it is.
    /// </summary>
    public string? AreaToken
    {
        get
        {
            if (Tokens is not { Count: > 0 } tokens)
                return null;
            var shades = tokens.Where(t => !string.Equals(t, "NODTA", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(t, "DEPDW", StringComparison.OrdinalIgnoreCase)).ToList();
            return shades.Count == 0 ? tokens[0] : shades[(shades.Count - 1) / 2];
        }
    }

    /// <summary>
    /// The catalogue's default symbols, the magenta <c>?</c> a rule draws when
    /// nothing else fits. A feature type never leads with one.
    /// </summary>
    public static IReadOnlySet<string> DefaultSymbols { get; } =
        new HashSet<string>(["BCNDEF13", "QUESMRK1"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether the tile shows anything.</summary>
    public bool HasGlyph => Kind != LeadGlyphKind.None;

    /// <summary>
    /// The lead glyph of a feature type, in order of preference: its own point
    /// symbol, never the default <c>?</c>; else its colour fills as an area patch;
    /// else its area fill pattern; else its line; else nothing.
    /// </summary>
    /// <remarks>
    /// Uses the feature type's own rule makes come before uses from a shared
    /// rule (<see cref="RuleUse.ViaRule"/>): every step is tried on its own
    /// rule first. Of the shared rule's uses, those that don't depend on an
    /// attribute value (such as Depth Area's depth shades) come before those
    /// that do (such as its restriction symbols). Among symbols, those whose
    /// display setting conditions hold now come first, so Lateral Beacon
    /// leads with <c>BCNLAT21</c> when Simplified symbols is on and with the
    /// paper chart branch's first symbol, <c>BCNTOW30</c>, when it is off.
    /// Otherwise the rule's order decides: the first case wins.
    /// </remarks>
    /// <param name="feature">The feature type.</param>
    /// <param name="index">The index the feature type belongs to, which holds its items.</param>
    /// <param name="simplifiedSymbols">Whether Simplified symbols is on.</param>
    /// <returns>The lead glyph.</returns>
    public static LeadGlyph For(FeatureTypeEntry feature, ChartReferenceIndex index, bool simplifiedSymbols)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(index);

        for (int tier = 0; tier < 3; tier++)
        {
            var symbol = feature.Uses
                .Where(u => u.ItemKind == ReferenceKind.Symbol && Tier(u) == tier)
                .Select((u, order) => (Use: u, Order: order))
                .OrderBy(x => Contradicts(x.Use, simplifiedSymbols) ? 1 : 0)
                .ThenBy(x => x.Order)
                .Select(x => SymbolId(x.Use, index))
                .FirstOrDefault(id => id is not null && !DefaultSymbols.Contains(id));
            if (symbol is not null)
                return new LeadGlyph(LeadGlyphKind.Symbol, new ReferenceKey(index.Spec, ReferenceKind.Symbol, symbol), symbol);

            if (feature.ColorFills.Any(f => Tier(f) == tier))
            {
                // The fills of every tier, this one's first: Depth Area's own
                // NODTA, then the shades its shared rule draws.
                var tokens = feature.ColorFills
                    .OrderBy(f => Tier(f) == tier ? 0 : 1)
                    .Select(f => f.Token)
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxColours)
                    .ToList();
                return new LeadGlyph(LeadGlyphKind.Area, feature.Key, Tokens: tokens);
            }

            var fill = feature.Uses.FirstOrDefault(u => u.ItemKind == ReferenceKind.AreaFill && !u.IsFamily && Tier(u) == tier);
            if (fill is not null)
                return new LeadGlyph(LeadGlyphKind.AreaFill, new ReferenceKey(index.Spec, ReferenceKind.AreaFill, fill.ItemId), fill.ItemId);

            var line = feature.Uses.FirstOrDefault(u => u.ItemKind == ReferenceKind.LineStyle && !u.IsFamily && Tier(u) == tier);
            if (line is not null)
                return new LeadGlyph(LeadGlyphKind.LineStyle, new ReferenceKey(index.Spec, ReferenceKind.LineStyle, line.ItemId), line.ItemId);

            var pen = feature.Pens.FirstOrDefault(p => Tier(p) == tier);
            if (pen is not null)
                return new LeadGlyph(LeadGlyphKind.Pen, feature.Key, Pen: pen);
        }

        return new LeadGlyph(LeadGlyphKind.None, feature.Key);
    }

    /// <summary>
    /// The glyph of a portrayal item: the item itself, or for a family its
    /// first member.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The glyph.</returns>
    public static LeadGlyph For(PortrayalItemEntry item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var spec = item.Key.Spec;
        if (item.IsFamily)
        {
            return item.Members.Count > 0
                ? new LeadGlyph(LeadGlyphKind.Symbol, new ReferenceKey(spec, ReferenceKind.Symbol, item.Members[0]), item.Members[0])
                : new LeadGlyph(LeadGlyphKind.None, item.Key);
        }

        var kind = item.Key.Kind switch
        {
            ReferenceKind.Symbol => LeadGlyphKind.Symbol,
            ReferenceKind.LineStyle => LeadGlyphKind.LineStyle,
            ReferenceKind.AreaFill => LeadGlyphKind.AreaFill,
            _ => LeadGlyphKind.None,
        };
        return new LeadGlyph(kind, item.Key, kind == LeadGlyphKind.None ? null : item.Code);
    }

    // 0: the feature type's own rule; 1: a shared rule, whatever the
    // attributes; 2: a shared rule, for some attribute values only.
    private static int Tier(RuleUse use)
    {
        if (use.ViaRule is null)
            return 0;
        return use.Conditions.Any(c => c.Kind is RuleConditionKind.Attribute or RuleConditionKind.Expression or RuleConditionKind.Otherwise)
            ? 2
            : 1;
    }

    private static bool Contradicts(RuleUse use, bool simplifiedSymbols) =>
        use.Conditions.Any(c =>
            c.Kind == RuleConditionKind.ContextParameter
            && string.Equals(c.Subject, "SimplifiedSymbols", StringComparison.OrdinalIgnoreCase)
            && (c.Operator == ConditionOperator.IsTrue) != simplifiedSymbols
            && c.Operator is ConditionOperator.IsTrue or ConditionOperator.IsFalse);

    private static string? SymbolId(SymbolUse use, ChartReferenceIndex index)
    {
        if (!use.IsFamily)
            return use.ItemId;
        return index.TryGet(new ReferenceKey(index.Spec, ReferenceKind.Symbol, use.ItemId), out var entry)
            && entry is PortrayalItemEntry { Members.Count: > 0 } family
                ? family.Members[0]
                : null;
    }
}
