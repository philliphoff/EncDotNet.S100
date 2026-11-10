using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S57;

namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>A kind of seabed sediment, each drawn with its own pattern mark (<see cref="EsbSediment"/>).</summary>
public enum AmlSedimentKind
{
    /// <summary>Sand: dots.</summary>
    Sand,

    /// <summary>Mud, silt and clay: short dashes.</summary>
    Mud,

    /// <summary>Gravel, pebbles and stones: small rings.</summary>
    Gravel,

    /// <summary>Cobbles and boulders: large rings.</summary>
    Boulders,

    /// <summary>Rock, from bedrock to chalk, sandstone and lava: small crosses.</summary>
    Rock,

    /// <summary>Shells and coral: small cups.</summary>
    Shells,

    /// <summary>Till and glaciofluvial drift: small open triangles.</summary>
    Till,
}

/// <summary>How much of a sediment mixture one kind is, which sets how densely its mark is drawn.</summary>
public enum AmlSedimentShare
{
    /// <summary>The main sediment, the noun of a Folk class ("sandy <em>gravel</em>"): every mark.</summary>
    Main,

    /// <summary>A qualifier ("<em>sandy</em> gravel"): every other mark.</summary>
    Minor,

    /// <summary>A trace ("<em>slightly gravelly</em> sand"): every fourth mark.</summary>
    Trace,
}

/// <summary>One kind of sediment in a mixture, and its share.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Share">Its share.</param>
public readonly record struct AmlSedimentPart(AmlSedimentKind Kind, AmlSedimentShare Share);

/// <summary>
/// The sediment of the AML Environment, Seabed and Beach (ESB) geological
/// layer (<c>sedlay</c>), and of the AML 3.0 SED cells: a pattern over a 7 %
/// wash in the environmental ink, never a colour ramp, so it never competes
/// with the chart's depth shading (AML design handoff decision 21, frame
/// B2d).
/// </summary>
/// <remarks>
/// <para>
/// <b>Kinds.</b> Each value of <c>natsed</c> (nature of geological layer) is
/// one or more <see cref="AmlSedimentKind"/> values. The handoff fixes sand
/// (dots), mud (dashes) and gravel (rings); the rest are SoundCharts' own:
/// </para>
/// <list type="table">
/// <listheader><term>Kind</term><description>Mark, and the <c>natsed</c> values drawn with it</description></listheader>
/// <item><term>Sand</term><description>Dots: sand (523).</description></item>
/// <item><term>Mud</term><description>Short dashes: mud (521), silt (522), clays (514).</description></item>
/// <item><term>Gravel</term><description>Small rings: gravel (525), pebbles (526), stone (524).</description></item>
/// <item><term>Boulders</term><description>Large rings: cobbles (527), boulder (531).</description></item>
/// <item><term>Rock</term><description>Small crosses: rock (528), every bedrock type (501–512, 515–520), lava (529); in AML 3.0 also crystalline (544) and sedimentary rock (545).</description></item>
/// <item><term>Shells</term><description>Small cups: shells (530), coral (513).</description></item>
/// <item><term>Till</term><description>Small open triangles: till (546) and glaciofluvial drift (547), AML 3.0 only.</description></item>
/// </list>
/// <para>
/// <b>Mixtures.</b> A Folk class (532–544) draws the marks of each of its
/// parts in one pattern: the main sediment, the noun, with every mark; a
/// qualifier ("sandy") with every other mark; a trace ("slightly gravelly")
/// with every fourth. So sandy gravel is rings with sparse dots, and gravelly
/// sand dots with sparse rings. A list of values, such as <c>525,523,522</c>,
/// is drawn the same way: the first is the main sediment, the next two
/// qualifiers. Code 544 is "gravelly muddy sand" in AML 1.0 but "crystalline
/// rock" in AML 3.0, so the cell's catalogue decides. Unknown and "other"
/// values draw the wash alone.
/// </para>
/// <para>
/// <b>Patterns.</b> Every pattern is one 9.6 mm square tile of marks on a
/// brick lattice, made on demand and named
/// <c>AML_ENVIRONMENTAL_SED_{main}_{qualifiers}_{traces}</c> with one letter
/// per kind (<see cref="Letter"/>), such as <c>AML_ENVIRONMENTAL_SED_G_S_</c>
/// for sandy gravel. A mixture is one fill, not one per part: the renderer
/// clips overlapping pattern fills against each other.
/// </para>
/// <para>
/// <b>Legend (the "small sediment legend on the map").</b> The renderer has
/// no on-map legend box, so the sediment legend is the geological layer's
/// legend entries (<see cref="BuildLegend"/>), one swatch per kind and one
/// for a mixture, which the AML legend (#1374) shows while ESB is drawn.
/// </para>
/// </remarks>
public static class EsbSediment
{
    /// <summary>The geological layer class.</summary>
    public const string Acronym = "sedlay";

    /// <summary>The sediment attribute: nature of geological layer.</summary>
    public const string NatureAttribute = "natsed";

    /// <summary>The wash under the pattern: 93 % transparent, so 7 % of the environmental ink.</summary>
    public const double WashTransparency = 0.93;

    /// <summary>The side of a pattern tile, in millimetres.</summary>
    public const double TileSize = 9.6;

    /// <summary>The drawing priority of the wash, under the pattern.</summary>
    public const int WashPriority = 4;

    /// <summary>The drawing priority of the pattern, under the boundary.</summary>
    public const int PatternPriority = 6;

    // The sediment boundary: the dash-dot, 1 px, no halo and no ticks, so the
    // patterns, not the boundaries, carry the layer.
    private const double BoundaryWidth = AmlSignature.PixelMm;

    private const string Stem = "AML_ENVIRONMENTAL_SED_";

    private static readonly string Ink = AmlFamily.Environmental.ColorToken;

    private static readonly ConcurrentDictionary<string, string?> Tiles = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, AreaFill?> Fills = new(StringComparer.Ordinal);

    /// <summary>The kinds in legend order.</summary>
    public static IReadOnlyList<AmlSedimentKind> Kinds { get; } = Enum.GetValues<AmlSedimentKind>();

    /// <summary>The kind's letter in pattern names: S, M, G, B, R, H (shells) or T.</summary>
    /// <param name="kind">The kind.</param>
    public static char Letter(AmlSedimentKind kind) => kind switch
    {
        AmlSedimentKind.Sand => 'S',
        AmlSedimentKind.Mud => 'M',
        AmlSedimentKind.Gravel => 'G',
        AmlSedimentKind.Boulders => 'B',
        AmlSedimentKind.Rock => 'R',
        AmlSedimentKind.Shells => 'H',
        AmlSedimentKind.Till => 'T',
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>The kind's name for people, such as "Sand".</summary>
    /// <param name="kind">The kind.</param>
    public static string Name(AmlSedimentKind kind) => kind switch
    {
        AmlSedimentKind.Sand => "Sand",
        AmlSedimentKind.Mud => "Mud, silt, clay",
        AmlSedimentKind.Gravel => "Gravel, pebbles",
        AmlSedimentKind.Boulders => "Cobbles, boulders",
        AmlSedimentKind.Rock => "Rock",
        AmlSedimentKind.Shells => "Shells, coral",
        AmlSedimentKind.Till => "Till, glacial drift",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>
    /// The parts of one <c>natsed</c> value: the kinds of a plain sediment, or
    /// a Folk mixture's main sediment, qualifiers and trace. Empty for an
    /// unknown or "other" value.
    /// </summary>
    /// <param name="value">The <c>natsed</c> code, such as 543 (sandy gravel).</param>
    /// <param name="aml30">Whether the cell is AML 3.0, where 544 is crystalline rock rather than gravelly muddy sand.</param>
    public static IReadOnlyList<AmlSedimentPart> Classify(int value, bool aml30)
    {
        const AmlSedimentShare Main = AmlSedimentShare.Main, Minor = AmlSedimentShare.Minor, Trace = AmlSedimentShare.Trace;
        static AmlSedimentPart P(AmlSedimentKind kind, AmlSedimentShare share) => new(kind, share);
        return value switch
        {
            513 or 530 => [P(AmlSedimentKind.Shells, Main)],
            514 or 521 or 522 => [P(AmlSedimentKind.Mud, Main)],
            523 => [P(AmlSedimentKind.Sand, Main)],
            524 or 525 or 526 => [P(AmlSedimentKind.Gravel, Main)],
            527 or 531 => [P(AmlSedimentKind.Boulders, Main)],
            (>= 501 and <= 512) or (>= 515 and <= 520) or 528 or 529 or 545 => [P(AmlSedimentKind.Rock, Main)],
            546 or 547 => [P(AmlSedimentKind.Till, Main)],
            544 when aml30 => [P(AmlSedimentKind.Rock, Main)],
            // Folk classes: the noun is the main sediment.
            532 => [P(AmlSedimentKind.Mud, Main), P(AmlSedimentKind.Sand, Minor)],
            533 => [P(AmlSedimentKind.Mud, Main), P(AmlSedimentKind.Gravel, Trace)],
            534 => [P(AmlSedimentKind.Mud, Main), P(AmlSedimentKind.Sand, Minor), P(AmlSedimentKind.Gravel, Trace)],
            535 => [P(AmlSedimentKind.Mud, Main), P(AmlSedimentKind.Gravel, Minor)],
            536 => [P(AmlSedimentKind.Sand, Main), P(AmlSedimentKind.Mud, Minor)],
            537 => [P(AmlSedimentKind.Sand, Main), P(AmlSedimentKind.Gravel, Trace)],
            538 => [P(AmlSedimentKind.Sand, Main), P(AmlSedimentKind.Mud, Minor), P(AmlSedimentKind.Gravel, Trace)],
            539 or 544 => [P(AmlSedimentKind.Sand, Main), P(AmlSedimentKind.Mud, Minor), P(AmlSedimentKind.Gravel, Minor)],
            540 => [P(AmlSedimentKind.Sand, Main), P(AmlSedimentKind.Gravel, Minor)],
            541 => [P(AmlSedimentKind.Gravel, Main), P(AmlSedimentKind.Mud, Minor)],
            542 => [P(AmlSedimentKind.Gravel, Main), P(AmlSedimentKind.Sand, Minor), P(AmlSedimentKind.Mud, Minor)],
            543 => [P(AmlSedimentKind.Gravel, Main), P(AmlSedimentKind.Sand, Minor)],
            _ => [],
        };
    }

    /// <summary>
    /// The parts of a feature's sediment: its <c>natsed</c> value, or list of
    /// values, read with its cell's catalogue. Empty when it has none that is
    /// known.
    /// </summary>
    /// <param name="feature">The feature.</param>
    public static IReadOnlyList<AmlSedimentPart> Classify(AmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.Attribute(NatureAttribute) is not { Length: > 0 } text)
            return [];

        bool aml30 = IsAml30(feature);
        var parts = new Dictionary<AmlSedimentKind, AmlSedimentShare>();
        bool first = true;
        int listed = 0;
        foreach (var item in text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(item, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                continue;
            var classified = Classify(code, aml30);
            if (classified.Count == 0)
                continue;

            // The first value keeps its own shares; later ones are qualifiers at most.
            foreach (var part in classified)
            {
                var share = first ? part.Share : (AmlSedimentShare)Math.Max((int)part.Share, (int)AmlSedimentShare.Minor);
                if (!parts.TryGetValue(part.Kind, out var existing) || share < existing)
                    parts[part.Kind] = share;
            }

            first = false;
            if (++listed == 3)
                break;
        }

        return [.. parts.OrderBy(p => p.Value).ThenBy(p => p.Key).Select(p => new AmlSedimentPart(p.Key, p.Value))];
    }

    /// <summary>
    /// The name of the pattern fill (and its tile symbol) of
    /// <paramref name="parts"/>, such as <c>AML_ENVIRONMENTAL_SED_G_S_</c> for
    /// sandy gravel; <see langword="null"/> without a main sediment.
    /// </summary>
    /// <param name="parts">The parts of a sediment.</param>
    public static string? FillName(IReadOnlyList<AmlSedimentPart> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (!parts.Any(p => p.Share == AmlSedimentShare.Main))
            return null;

        string Letters(AmlSedimentShare share) =>
            new([.. parts.Where(p => p.Share == share).Select(p => p.Kind).Distinct().Order().Select(Letter)]);
        return $"{Stem}{Letters(AmlSedimentShare.Main)}_{Letters(AmlSedimentShare.Minor)}_{Letters(AmlSedimentShare.Trace)}";
    }

    /// <summary>The pattern fill of one kind alone, such as <c>AML_ENVIRONMENTAL_SED_S__</c> for sand.</summary>
    /// <param name="kind">The kind.</param>
    public static string FillName(AmlSedimentKind kind) => FillName([new AmlSedimentPart(kind, AmlSedimentShare.Main)])!;

    /// <summary>The module resources: the pattern tiles and fills, made on demand from their names.</summary>
    public static AmlStyleResources Resources { get; } = new()
    {
        SymbolResolver = ResolveTile,
        AreaFillResolver = ResolveFill,
    };

    /// <summary>The tile symbol of a pattern name, or <see langword="null"/> for any other name.</summary>
    /// <param name="name">The name, such as <c>AML_ENVIRONMENTAL_SED_G_S_</c>.</param>
    public static string? ResolveTile(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(Stem, StringComparison.Ordinal) ? Tiles.GetOrAdd(name, n => Parse(n) is { } parts ? Tile(n, parts) : null) : null;
    }

    /// <summary>The area fill of a pattern name: its tile on a 9.6 mm square lattice; <see langword="null"/> for any other name.</summary>
    /// <param name="name">The name, such as <c>AML_ENVIRONMENTAL_SED_G_S_</c>.</param>
    public static AreaFill? ResolveFill(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name.StartsWith(Stem, StringComparison.Ordinal)
            ? Fills.GetOrAdd(name, n => Parse(n) is null ? null : new AreaFill { Name = n, PatternSymbol = n, V1X = TileSize, V2Y = TileSize })
            : null;
    }

    /// <summary>
    /// Draws a geological layer: an area is the wash, its pattern and a thin
    /// dash-dot boundary, with no label; a line is that boundary; a point is
    /// the framed sediment glyph and the sediment's name.
    /// </summary>
    /// <param name="feature">The feature.</param>
    /// <param name="context">What to draw it with.</param>
    public static IEnumerable<DrawingInstruction> Build(AmlFeature feature, AmlDrawingContext context)
    {
        ArgumentNullException.ThrowIfNull(feature);
        ArgumentNullException.ThrowIfNull(context);
        switch (feature.GeometryType)
        {
            case EncDotNet.S100.Features.S100GeometryType.Surface:
                yield return context.At(WashPriority).Area(feature.Id, context.Family.ColorToken, WashTransparency);
                if (FillName(Classify(feature)) is { } fill)
                    yield return context.At(PatternPriority).AreaPattern(feature.Id, fill);
                yield return Boundary(feature, context);
                break;

            case EncDotNet.S100.Features.S100GeometryType.Curve:
                yield return Boundary(feature, context);
                break;

            case EncDotNet.S100.Features.S100GeometryType.Point:
                yield return context.Symbol(EsbEnvironmentalStyles.SedimentGlyph);
                if (Label(feature) is { } label)
                    yield return context.PointLabel(label);
                break;
        }
    }

    /// <summary>The sediment's name for a point's label, from the cell's catalogue, such as "Chalk"; or its <c>OBJNAM</c>.</summary>
    /// <param name="feature">The feature.</param>
    public static string? Label(AmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (AmlLabels.Name(feature) is { } name)
            return name;
        var text = feature.Attribute(NatureAttribute);
        var first = text?.Split(',', StringSplitOptions.TrimEntries)[0];
        if (!int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            return null;
        var catalogue = IsAml30(feature) ? S57ObjectCatalogue.Aml30 : S57ObjectCatalogue.Aml10;
        if (!catalogue.TryGetAttribute(NatureAttribute, out var attribute) || !attribute.TryGetValue(code, out var value))
            return null;
        var label = value.Name.Replace(" (Folk)", string.Empty, StringComparison.Ordinal);
        return label.Length == 0 ? null : char.ToUpperInvariant(label[0]) + label[1..].ToLowerInvariant();
    }

    /// <summary>
    /// The sediment legend in <paramref name="palette"/>: a swatch of the wash
    /// and pattern for each kind, then one for a mixture (sandy gravel). The
    /// geological layer's legend entries, so the AML legend shows them while
    /// ESB is drawn.
    /// </summary>
    /// <param name="catalog">The catalogue whose palette the swatches are drawn in.</param>
    /// <param name="palette">The palette.</param>
    public static IReadOnlyList<LegendEntry> BuildLegend(AmlStyleCatalog catalog, PaletteType palette)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var colors = catalog.Palette.GetColorPalette(palette);
        var entries = Kinds
            .Select(kind => (Parts: (IReadOnlyList<AmlSedimentPart>)[new AmlSedimentPart(kind, AmlSedimentShare.Main)], Label: "Sediment: " + Name(kind).ToLowerInvariant()))
            .Append((Parts: Classify(543, aml30: false), Label: "Sediment mixture, such as sandy gravel: sparser marks for the lesser parts"));
        return [.. entries.Select(e => (LegendEntry)new SymbolLegendEntry(SvgProcessor.Process(Swatch(e.Parts), colors), e.Label))];
    }

    // AML 3.0 cells resolve their classes through the 3.0 catalogue, whose
    // definitions are their own objects.
    private static bool IsAml30(AmlFeature feature) =>
        feature.ObjectClass is { } objectClass
        && ReferenceEquals(objectClass, S57ObjectCatalogue.Aml30.GetObjectClass(objectClass.Acronym));

    private static LineInstruction Boundary(AmlFeature feature, AmlDrawingContext context)
    {
        var line = context.At(AmlDrawingContext.AreaPriority).Line(feature.Id, context.Family.ColorToken, BoundaryWidth);
        return new LineInstruction
        {
            FeatureReference = line.FeatureReference,
            LineStyleReference = line.LineStyleReference,
            LineColor = line.LineColor,
            LineWidth = line.LineWidth,
            Dashes = AmlSignature.DashDot,
            DashIntervalLength = AmlSignature.Interval,
            Plane = line.Plane,
            ViewingGroup = line.ViewingGroup,
            DrawingPriority = line.DrawingPriority,
            ScaleMinimum = line.ScaleMinimum,
            ScaleMaximum = line.ScaleMaximum,
        };
    }

    private static IReadOnlyList<AmlSedimentPart>? Parse(string name)
    {
        var groups = name[Stem.Length..].Split('_');
        if (groups.Length != 3 || groups[0].Length == 0)
            return null;
        var parts = new List<AmlSedimentPart>();
        var seen = new HashSet<AmlSedimentKind>();
        for (int g = 0; g < 3; g++)
        {
            foreach (var letter in groups[g])
            {
                var kind = Kinds.Where(k => Letter(k) == letter).Cast<AmlSedimentKind?>().FirstOrDefault();
                if (kind is null || !seen.Add(kind.Value))
                    return null;
                parts.Add(new AmlSedimentPart(kind.Value, (AmlSedimentShare)g));
            }
        }

        return parts;
    }

    // One kind's mark lattice in the tile: columns and rows of a brick lattice
    // (odd rows shifted half a column), and the mark's SVG at a position.
    private static (int Columns, int Rows, Func<double, double, string> Mark) Lattice(AmlSedimentKind kind) => kind switch
    {
        AmlSedimentKind.Sand => (4, 6, (x, y) => $"""<circle class="f{Ink}" cx="{Mm(x)}" cy="{Mm(y)}" r="0.29"/>"""),
        AmlSedimentKind.Mud => (3, 4, (x, y) => $"""<path class="s{Ink} f0" stroke-width="0.32" stroke-linecap="butt" d="M {Mm(x - 0.75)} {Mm(y)} L {Mm(x + 0.75)} {Mm(y)}"/>"""),
        AmlSedimentKind.Gravel => (3, 3, (x, y) => $"""<circle class="s{Ink} f0" stroke-width="0.29" cx="{Mm(x)}" cy="{Mm(y)}" r="0.6"/>"""),
        AmlSedimentKind.Boulders => (2, 2, (x, y) => $"""<circle class="s{Ink} f0" stroke-width="0.35" cx="{Mm(x)}" cy="{Mm(y)}" r="1.0"/>"""),
        AmlSedimentKind.Rock => (3, 3, (x, y) => $"""<path class="s{Ink} f0" stroke-width="0.29" d="M {Mm(x - 0.55)} {Mm(y)} L {Mm(x + 0.55)} {Mm(y)} M {Mm(x)} {Mm(y - 0.55)} L {Mm(x)} {Mm(y + 0.55)}"/>"""),
        AmlSedimentKind.Shells => (3, 3, (x, y) => $"""<path class="s{Ink} f0" stroke-width="0.29" stroke-linecap="round" d="M {Mm(x - 0.6)} {Mm(y - 0.3)} Q {Mm(x)} {Mm(y + 0.7)} {Mm(x + 0.6)} {Mm(y - 0.3)}"/>"""),
        AmlSedimentKind.Till => (3, 3, (x, y) => $"""<path class="s{Ink} f0" stroke-width="0.29" stroke-linejoin="round" d="M {Mm(x)} {Mm(y - 0.55)} L {Mm(x + 0.55)} {Mm(y + 0.4)} L {Mm(x - 0.55)} {Mm(y + 0.4)} Z"/>"""),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    // The marks of every part about the tile's centre. One kind alone uses
    // its own brick lattice. A mixture shares one 6 × 6 square lattice of
    // slots, so the marks interleave rather than collide: the main sediment
    // takes half the slots (a checkerboard), a qualifier a quarter, a trace
    // an eighth.
    private static string Marks(IReadOnlyList<AmlSedimentPart> parts, double originX, double originY)
    {
        var sb = new StringBuilder();
        if (parts.Count == 1)
        {
            var (columns, rows, mark) = Lattice(parts[0].Kind);
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < columns; i++)
                    sb.Append(Mark(mark, columns, rows, i, j, brick: true, originX, originY));
            }

            return sb.ToString();
        }

        const int Columns = 6, Rows = 6;
        // The slot groups after the main sediment's: the odd slots in even rows,
        // then in odd rows; a trace takes half a group.
        var groups = new Queue<Func<int, int, bool>>(
        [
            (i, j) => (i + j) % 2 == 1 && j % 2 == 0,
            (i, j) => (i + j) % 2 == 1 && j % 2 == 1,
        ]);
        foreach (var part in parts.OrderBy(p => p.Share))
        {
            Func<int, int, bool> slots = part.Share switch
            {
                AmlSedimentShare.Main => (i, j) => (i + j) % 2 == 0,
                _ when groups.Count == 0 => (i, j) => (i + j) % 2 == 1 && j % 2 == 1 && (i / 2 + j / 2) % 2 == 0,
                AmlSedimentShare.Minor => groups.Dequeue(),
                _ => Half(groups.Dequeue()),
            };
            var mark = Lattice(part.Kind).Mark;
            for (int j = 0; j < Rows; j++)
            {
                for (int i = 0; i < Columns; i++)
                {
                    if (slots(i, j))
                        sb.Append(Mark(mark, Columns, Rows, i, j, brick: false, originX, originY));
                }
            }
        }

        return sb.ToString();

        // Every other slot of a group, spread over the tile.
        static Func<int, int, bool> Half(Func<int, int, bool> group) => (i, j) => group(i, j) && (i / 2 + j / 2) % 2 == 0;
    }

    // The mark at column i and row j of a lattice over the tile: a brick
    // lattice (odd rows shifted half a column) or a square one.
    private static string Mark(Func<double, double, string> mark, int columns, int rows, int i, int j, bool brick, double originX, double originY)
    {
        double w = TileSize / columns, h = TileSize / rows;
        double x = (brick ? i + 0.25 + 0.5 * (j % 2) : i + 0.5) * w - TileSize / 2;
        double y = (j + 0.5) * h - TileSize / 2;
        return mark(originX + x, originY + y);
    }

    private static string Tile(string name, IReadOnlyList<AmlSedimentPart> parts)
    {
        var half = Mm(-TileSize / 2);
        var size = Mm(TileSize);
        var description = string.Join(", ", parts.Select(p => $"{Name(p.Kind).ToLowerInvariant()} ({p.Share.ToString().ToLowerInvariant()})"));
        return $"""<svg xmlns="http://www.w3.org/2000/svg" version="1.2" width="{size}mm" height="{size}mm" viewBox="{half} {half} {size} {size}"><title>{name}</title><desc>An AML sediment pattern tile, environmental family: {description}.</desc><rect class="symbolBox layout" fill="none" x="{half}" y="{half}" width="{size}" height="{size}"/>{Marks(parts, 0, 0)}<circle class="pivotPoint layout" fill="none" cx="0" cy="0" r="0.5"/></svg>""";
    }

    // A legend swatch: 9.6 × 6.4 mm of wash and pattern (the tile's top two
    // thirds), outlined in the ink.
    private static string Swatch(IReadOnlyList<AmlSedimentPart> parts)
    {
        const double Width = TileSize, Height = 6.4;
        var ink = Ink;
        var marks = Marks(parts, 0, 1.6);
        return $"""<svg xmlns="http://www.w3.org/2000/svg" version="1.2" width="{Mm(Width)}mm" height="{Mm(Height)}mm" viewBox="{Mm(-Width / 2)} {Mm(-Height / 2)} {Mm(Width)} {Mm(Height)}"><defs><clipPath id="swatch"><rect x="{Mm(-Width / 2)}" y="{Mm(-Height / 2)}" width="{Mm(Width)}" height="{Mm(Height)}"/></clipPath></defs><rect class="f{ink}" fill-opacity="{Mm(1 - WashTransparency)}" x="{Mm(-Width / 2)}" y="{Mm(-Height / 2)}" width="{Mm(Width)}" height="{Mm(Height)}"/><g clip-path="url(#swatch)">{marks}</g><rect class="s{ink} f0" stroke-width="0.2" x="{Mm(-Width / 2 + 0.1)}" y="{Mm(-Height / 2 + 0.1)}" width="{Mm(Width - 0.2)}" height="{Mm(Height - 0.2)}"/></svg>""";
    }

    private static string Mm(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
