using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// The side of a line a <see cref="ComplexLineStyle"/> places its symbols
/// on, looking along the line from its first point to its last.
/// </summary>
public enum LineSymbolSide
{
    /// <summary>The left of the line: the side a symbol is drawn on in its SVG (above its x axis).</summary>
    Left,

    /// <summary>The right of the line: the symbol turned through 180°.</summary>
    Right,
}

/// <summary>
/// A complex line style defined in code: a pen, optionally dashed, with
/// symbols repeated along the line at a fixed spacing, each on a chosen side.
/// It is the S-100 Part 9 line style a catalogue would hold, built from a few
/// numbers: a front's line and pips, for example. Register it in a
/// <see cref="LineStyleSet"/> and draw it with
/// <see cref="InstructionBuilder.ComplexLine"/>.
/// </summary>
/// <remarks>
/// <para>
/// Spacing, widths and dashes are millimetres on the S-100 display surface,
/// like every size in a portrayal, so the renderers place the symbols in
/// screen space: the spacing on screen is the same at every scale.
/// </para>
/// <para>
/// The <see cref="Symbols"/> are placed in turn, one every
/// <see cref="SymbolSpacing"/>, the first <see cref="SymbolStart"/> along the
/// line, and the list repeats. Each symbol is drawn with its pivot on the line
/// and its x axis along it, so a symbol drawn above its x axis (negative y in
/// the SVG) sits on the left of the line. <see cref="LineSymbolSide.Right"/>
/// turns it through 180° (S-100 Part 9 <c>LineSymbol rotation</c>), so draw
/// symbols that are symmetric about their y axis, as a front's triangles and
/// semicircles are.
/// </para>
/// <para>
/// For a stationary front, two symbols in different colours on alternating
/// sides:
/// </para>
/// <code>
/// var stationary = new ComplexLineStyle
/// {
///     Name = "WXFRONT_STATIONARY",
///     ColorToken = "WXFRONT",
///     Width = 0.7,
///     Symbols = ["WXPIP_TRIANGLE", "WXPIP_SEMICIRCLE"],
///     AlternateSides = true,
/// };
/// </code>
/// </remarks>
public sealed record ComplexLineStyle
{
    /// <summary>The style's name, which line instructions reference.</summary>
    public required string Name { get; init; }

    /// <summary>The pen's colour token.</summary>
    public required string ColorToken { get; init; }

    /// <summary>The pen width in millimetres; 0 (the default) draws no pen, only the symbols.</summary>
    public double Width { get; init; }

    /// <summary>The dash length in millimetres; 0 (the default) for a solid pen.</summary>
    public double DashLength { get; init; }

    /// <summary>The gap between dashes in millimetres; 0 (the default) for a solid pen.</summary>
    public double GapLength { get; init; }

    /// <summary>
    /// The symbols placed along the line, in turn, by name in the portrayal's
    /// <see cref="SymbolSet"/>; empty (the default) for a pen alone.
    /// </summary>
    public IReadOnlyList<string> Symbols { get; init; } = [];

    /// <summary>
    /// The distance between one symbol and the next, in millimetres. Defaults
    /// to 9, the spacing of a front's pips (vector weather V20).
    /// </summary>
    public double SymbolSpacing { get; init; } = 9;

    /// <summary>
    /// The distance from the line's first point to its first symbol, in
    /// millimetres. Defaults to 4 (V20).
    /// </summary>
    public double SymbolStart { get; init; } = 4;

    /// <summary>The side of the line the first symbol goes on. Defaults to <see cref="LineSymbolSide.Left"/>.</summary>
    public LineSymbolSide Side { get; init; }

    /// <summary>
    /// Whether the symbols alternate sides, the first on <see cref="Side"/>,
    /// the next on the other side, and so on (a stationary front).
    /// </summary>
    public bool AlternateSides { get; init; }

    /// <summary>Whether the pen is dashed.</summary>
    public bool IsDashed => DashLength > 0 && GapLength > 0;

    /// <summary>
    /// The S-100 Part 9 line style this describes: the pen, the dashes over
    /// one repeat of the symbol pattern, and a symbol per place in it.
    /// </summary>
    /// <remarks>
    /// The pattern repeats after every symbol has been placed on every side it
    /// takes: two symbols alternating sides repeat after two places, one
    /// symbol alternating sides after two, three symbols on one side after
    /// three. A dashed pen restarts its dashes with each repeat.
    /// </remarks>
    /// <exception cref="ArgumentException">The style is not valid: see the message.</exception>
    public LineStyle ToLineStyle()
    {
        var layout = Layout();
        var dashes = new float[layout.Dashes.Count * 2];
        for (int i = 0; i < layout.Dashes.Count; i++)
        {
            dashes[2 * i] = (float)layout.Dashes[i].Start;
            dashes[2 * i + 1] = (float)layout.Dashes[i].Length;
        }

        return new LineStyle
        {
            Name = Name,
            Width = (float)Width,
            Color = ColorToken,
            DashPattern = dashes.Length > 0 ? dashes : null,
            IntervalLength = (float)layout.Interval,
            HasPen = Width > 0,
            Symbols = [.. layout.Symbols.Select(s => new LineStyleSymbol(s.Reference, (float)s.Position, (float)s.Rotation))],
        };
    }

    /// <summary>
    /// The style laid out over one interval in millimetres: the interval, the
    /// dashes as (start, length) pairs, and each symbol's place and rotation.
    /// </summary>
    /// <exception cref="ArgumentException">The style is not valid.</exception>
    internal (double Interval, IReadOnlyList<(double Start, double Length)> Dashes, IReadOnlyList<(string Reference, double Position, double Rotation)> Symbols) Layout()
    {
        Validate();

        int places = Symbols.Count == 0 ? 0 : LeastCommonMultiple(Symbols.Count, AlternateSides ? 2 : 1);
        double interval = places > 0 ? places * SymbolSpacing : IsDashed ? DashLength + GapLength : 0;

        var symbols = new (string, double, double)[places];
        for (int i = 0; i < places; i++)
        {
            var side = AlternateSides && i % 2 == 1 ? Opposite(Side) : Side;
            symbols[i] = (Symbols[i % Symbols.Count], SymbolStart + i * SymbolSpacing, side == LineSymbolSide.Right ? 180.0 : 0.0);
        }

        var dashes = new List<(double, double)>();
        if (IsDashed)
        {
            // The dashes restart with each interval; the last may be cut short.
            double period = DashLength + GapLength;
            for (int k = 0; k * period < interval - 1e-9; k++)
                dashes.Add((k * period, Math.Min(DashLength, interval - k * period)));
        }

        return (interval, dashes, symbols);
    }

    private void Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            throw new ArgumentException("A line style needs a name.", nameof(Name));
        if (string.IsNullOrWhiteSpace(ColorToken))
            throw new ArgumentException($"Line style '{Name}' needs a colour token.", nameof(ColorToken));
        if (Width < 0 || DashLength < 0 || GapLength < 0 || SymbolStart < 0)
            throw new ArgumentException($"Line style '{Name}' has a negative width, dash, gap or start.");
        if (Symbols.Count > 0 && !(SymbolSpacing > 0))
            throw new ArgumentException($"Line style '{Name}' places symbols, so its spacing must be positive.", nameof(SymbolSpacing));
        if (Symbols.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Line style '{Name}' has an empty symbol name.", nameof(Symbols));
        if (Width == 0 && Symbols.Count == 0)
            throw new ArgumentException($"Line style '{Name}' has neither a pen nor symbols, so it draws nothing.");
    }

    private static LineSymbolSide Opposite(LineSymbolSide side) =>
        side == LineSymbolSide.Left ? LineSymbolSide.Right : LineSymbolSide.Left;

    private static int LeastCommonMultiple(int a, int b)
    {
        int x = a, y = b;
        while (y != 0)
            (x, y) = (y, x % y);
        return a / x * b;
    }
}
