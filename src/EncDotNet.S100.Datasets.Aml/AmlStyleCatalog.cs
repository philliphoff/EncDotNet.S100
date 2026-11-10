using EncDotNet.S100.Datasets.Aml.Families;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// A set of AML class styles, usually one family's classes in one file, such
/// as <see cref="RalAirStyles"/>. A catalogue is built from modules, so a new
/// family or class lands in its own file.
/// </summary>
public interface IAmlStyleModule
{
    /// <summary>The module's name, such as "RAL air".</summary>
    string Name { get; }

    /// <summary>The module's class styles.</summary>
    IReadOnlyList<AmlClassStyle> Classes { get; }

    /// <summary>The extra symbols, line styles and area fills its classes draw with; none by default.</summary>
    AmlStyleResources Resources => AmlStyleResources.Empty;
}

/// <summary>A module that is just a list of class styles.</summary>
/// <param name="Name">The module's name.</param>
/// <param name="Classes">The module's class styles.</param>
public sealed record AmlStyleModule(string Name, IReadOnlyList<AmlClassStyle> Classes) : IAmlStyleModule
{
    /// <inheritdoc/>
    public AmlStyleResources Resources { get; init; } = AmlStyleResources.Empty;
}

/// <summary>A family's legend: its name and one entry per class, in module order.</summary>
/// <param name="Family">The family.</param>
/// <param name="Entries">The entries.</param>
public sealed record AmlLegendGroup(AmlFamily Family, IReadOnlyList<LegendEntry> Entries);

/// <summary>
/// The AML styles the portrayal draws with: every class style of its modules
/// by acronym, and what they draw with, which are the palette, the symbols,
/// the line styles, and the legend.
/// </summary>
/// <remarks>
/// <para>
/// <b>Adding a family or a class.</b> Write an <see cref="IAmlStyleModule"/>
/// in its own file (see <see cref="RalAirStyles"/>), with an
/// <see cref="AmlClassStyle"/> per class. Then either build a catalogue with
/// it, <c>new AmlStyleCatalog([.. AmlStyleCatalog.BuiltInModules, MyStyles.Module])</c>,
/// or add it to <see cref="BuiltInModules"/>. The symbols, line styles,
/// palette and legend follow from the styles; no switch needs editing. A new
/// family needs only a new <see cref="AmlFamily"/> value used by its styles.
/// </para>
/// <para>
/// <b>Legend.</b> <see cref="BuildLegend(PaletteType)"/> gives one group per family, with an
/// entry per class drawn from the class's own style, so the legend always
/// matches what is drawn.
/// </para>
/// </remarks>
public sealed class AmlStyleCatalog
{
    /// <summary>The style's name in the tile cache scope.</summary>
    public const string StyleName = "Aml";

    /// <summary>The style's version: bump it whenever its symbols, palette or rules change.</summary>
    public const int Version = 1;

    /// <summary>The caption the legend and the display section show (decision 23).</summary>
    public const string NoChecksNote = "AML depths aren't used for depth answers or route checks.";

    private readonly Dictionary<string, AmlClassStyle> _byAcronym = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LineStyle> _lineStyles = new(StringComparer.Ordinal);

    /// <summary>Builds a catalogue of <paramref name="modules"/>.</summary>
    /// <param name="modules">The modules, in legend order.</param>
    public AmlStyleCatalog(IEnumerable<IAmlStyleModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        Modules = [.. modules];
        var classes = new List<AmlClassStyle>();
        foreach (var module in Modules)
        {
            foreach (var style in module.Classes)
            {
                style.Validate();
                if (!_byAcronym.TryAdd(style.Acronym, style))
                    throw new ArgumentException($"Two AML styles are for class '{style.Acronym}' ({module.Name}).", nameof(modules));
                classes.Add(style);
            }
        }

        Classes = classes;
        Families = [.. AmlFamily.All.Concat(classes.Select(c => c.Family)).Distinct().OrderBy(f => f.Order)];
        Palette = AmlPalette.Build(Families);
        var resources = Modules.Select(m => m.Resources).ToList();
        Symbols = new AmlSymbols(
            Families,
            classes.Where(c => c.Glyph is not null).Select(c => (c.Family, c.Glyph!)).Distinct(),
            resources.SelectMany(r => r.Symbols),
            [.. resources.Select(r => r.SymbolResolver).OfType<Func<string, string?>>()]);
        foreach (var style in Families.SelectMany(AmlSignature.LineStyles).Concat(resources.SelectMany(r => r.LineStyles)))
        {
            if (!_lineStyles.TryAdd(style.Name, style))
                throw new ArgumentException($"Two AML line styles are named '{style.Name}'.", nameof(modules));
        }

        var fillResolvers = resources.Select(r => r.AreaFillResolver).OfType<Func<string, AreaFill?>>().ToList();
        LineStyleProvider = name => _lineStyles.GetValueOrDefault(name);
        AreaFillProvider = name => fillResolvers.Select(resolve => resolve(name)).FirstOrDefault(fill => fill is not null);
        ContentScope = ApplicationContentScope.For(StyleName, Version);
    }

    /// <summary>The built-in modules: <see cref="RalAirStyles"/>, <see cref="RalOperationalStyles"/> and <see cref="EsbEnvironmentalStyles"/>.</summary>
    public static IReadOnlyList<IAmlStyleModule> BuiltInModules { get; } = [RalAirStyles.Module, RalOperationalStyles.Module, EsbEnvironmentalStyles.Module];

    /// <summary>The catalogue of <see cref="BuiltInModules"/>.</summary>
    public static AmlStyleCatalog Default { get; } = new(BuiltInModules);

    /// <summary>The modules, in legend order.</summary>
    public IReadOnlyList<IAmlStyleModule> Modules { get; }

    /// <summary>Every class style, in module order.</summary>
    public IReadOnlyList<AmlClassStyle> Classes { get; }

    /// <summary>The families: the four of the handoff and any the styles add, bottom of the plane first.</summary>
    public IReadOnlyList<AmlFamily> Families { get; }

    /// <summary>The palette of the families and the halo.</summary>
    public ApplicationPalette Palette { get; }

    /// <summary>The symbols.</summary>
    public AmlSymbols Symbols { get; }

    /// <summary>The line styles by name, for a renderer's line-style provider.</summary>
    public Func<string, LineStyle?> LineStyleProvider { get; }

    /// <summary>The modules' area fills by name (<see cref="AmlStyleResources.AreaFillResolver"/>), for a renderer's area fill provider.</summary>
    public Func<string, AreaFill?> AreaFillProvider { get; }

    /// <summary>The tile disk-cache content scope of the style (<see cref="ApplicationContentScope"/>).</summary>
    public string ContentScope { get; }

    /// <summary>The style of the class <paramref name="acronym"/>, or <see langword="null"/> when it isn't drawn.</summary>
    /// <param name="acronym">The class's acronym, such as <c>airres</c>.</param>
    public AmlClassStyle? Find(string acronym)
    {
        ArgumentNullException.ThrowIfNull(acronym);
        return _byAcronym.GetValueOrDefault(acronym);
    }

    /// <summary>The legend in <paramref name="palette"/>: one group per family that has classes, top of the plane first.</summary>
    /// <param name="palette">The palette the samples are drawn in.</param>
    public IReadOnlyList<AmlLegendGroup> BuildLegend(PaletteType palette) =>
        [.. Families.Reverse()
            .Select(f => new AmlLegendGroup(f, BuildLegend(f, palette)))
            .Where(g => g.Entries.Count > 0)];

    /// <summary>The legend of <paramref name="family"/> in <paramref name="palette"/>: one entry per class, in module order.</summary>
    /// <param name="family">The family.</param>
    /// <param name="palette">The palette the samples are drawn in.</param>
    public IReadOnlyList<LegendEntry> BuildLegend(AmlFamily family, PaletteType palette)
    {
        ArgumentNullException.ThrowIfNull(family);
        var legend = new LegendBuilder(Palette, palette, Symbols.Provider);
        return [.. Classes.Where(c => c.Family == family).SelectMany(c => Entries(c, legend, palette))];
    }

    /// <summary>The legend entry of one class in <paramref name="palette"/>: the first of its <see cref="AmlClassStyle.Legend"/> entries when it has its own.</summary>
    /// <param name="style">The class's style.</param>
    /// <param name="palette">The palette the sample is drawn in.</param>
    public LegendEntry BuildLegendEntry(AmlClassStyle style, PaletteType palette)
    {
        ArgumentNullException.ThrowIfNull(style);
        return Entries(style, new LegendBuilder(Palette, palette, Symbols.Provider), palette).First();
    }

    private IEnumerable<LegendEntry> Entries(AmlClassStyle style, LegendBuilder legend, PaletteType palette)
    {
        if (style.Legend is { } own && own(this, palette) is { Count: > 0 } entries)
            return entries;
        return [Entry(style, legend, palette)];
    }

    private LegendEntry Entry(AmlClassStyle style, LegendBuilder legend, PaletteType palette)
    {
        var family = style.Family;
        return style.Kind switch
        {
            AmlClassKind.Point when style.Glyph is { } glyph => legend.Symbol(AmlSignature.FramedSymbol(family, glyph), style.LegendLabel),
            AmlClassKind.Area => new SwatchLegendEntry("#00000000", style.LegendLabel) { Outline = Sample(family, palette, ticks: true, route: false) },
            AmlClassKind.Route => new LineLegendEntry(Sample(family, palette, ticks: false, route: true), style.LegendLabel),
            _ => new LineLegendEntry(Sample(family, palette, ticks: false, route: false), style.LegendLabel),
        };
    }

    // The dash-dot in the family's ink; for an area, with two ticks on the
    // inside per dash-dot; for a route, heavier with a chevron.
    private LegendLineSample Sample(AmlFamily family, PaletteType palette, bool ticks, bool route)
    {
        var colors = Palette.GetColorPalette(palette);
        LegendLineSymbol Symbol(string name, double position, double rotation)
        {
            var raw = Symbols.GetSymbol(name)!;
            var pivot = SvgPivotMetrics.TryParse(raw)?.RelativeOffset ?? (0.0, 0.0);
            return new LegendLineSymbol(SvgProcessor.Process(raw, colors), position, rotation, pivot.X, pivot.Y);
        }

        IReadOnlyList<LegendLineSymbol> symbols =
            ticks ? [Symbol(AmlSignature.TickSymbol(family), AmlSignature.Interval / 4, 180), Symbol(AmlSignature.TickSymbol(family), 3 * AmlSignature.Interval / 4, 180)]
            : route ? [Symbol(AmlSignature.ChevronSymbol(family), AmlSignature.Interval / 2, 0)]
            : [];
        return new LegendLineSample
        {
            Color = Palette.Resolve(family.ColorToken, palette).ToHex(),
            Width = route ? AmlSignature.RouteInkWidth : AmlSignature.InkWidth,
            Dashes = AmlSignature.DashDot,
            IntervalLength = AmlSignature.Interval,
            Symbols = symbols,
        };
    }
}
