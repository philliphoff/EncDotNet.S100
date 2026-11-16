namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// A portrayal-catalogue symbol as parsed vector geometry: the shapes its SVG
/// draws, with colours left as palette tokens, so a renderer that is not
/// SVG-based (GIS symbology, vector PDF) can draw the symbol without parsing
/// SVG itself.
/// </summary>
/// <remarks>
/// <para>
/// Built from <see cref="SvgSymbol.SvgContent"/> by
/// <see cref="SymbolGeometryParser"/>, which reads the S-100 Part 9 class
/// conventions (<c>fTOKEN</c>, <c>sTOKEN</c>, <c>f0</c>, <c>sl</c>,
/// <c>layout</c>) through the same <see cref="SvgSymbolClasses"/> reading
/// the SVG renderers use, so both agree on what is drawn and in which
/// colour token.
/// </para>
/// <para>
/// All coordinates and widths are in the SVG's user units, which in S-100
/// symbols are millimetres, with y pointing down. Shapes are listed in
/// document (paint) order. Colours are tokens, not RGB: resolve them against
/// the active <see cref="ColorPalette"/> at draw time
/// (<see cref="SymbolColor.Resolve"/>).
/// </para>
/// </remarks>
public sealed class SymbolGeometry
{
    /// <summary>The symbol identifier (e.g. <c>"BOYLAT13"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>
    /// The SVG <c>viewBox</c>, in millimetres: the symbol's drawing area.
    /// <see langword="null"/> when the SVG declares none.
    /// </summary>
    public SymbolRect? ViewBox { get; init; }

    /// <summary>
    /// The symbol's pivot point (S-100 Part 9: the point placed on the
    /// feature anchor), in millimetres, from the <c>pivotPoint</c> layout
    /// circle. <c>(0, 0)</c> when the symbol has none.
    /// </summary>
    public (double X, double Y) Pivot { get; init; }

    /// <summary>Whether the SVG has a <c>pivotPoint</c> element.</summary>
    public bool HasPivot { get; init; }

    /// <summary>The drawn shapes, in paint order. Layout elements are excluded.</summary>
    public required IReadOnlyList<SymbolShape> Shapes { get; init; }

    /// <summary>
    /// Elements the parser could not turn into geometry (for example
    /// <c>text</c>, a <c>transform</c>, or a pattern paint), one entry per
    /// element, named by element. Empty for every symbol the bundled
    /// catalogues draw with shapes only.
    /// </summary>
    public IReadOnlyList<string> Unsupported { get; init; } = [];
}

/// <summary>An axis-aligned rectangle in symbol units (millimetres).</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width.</param>
/// <param name="Height">The height.</param>
public readonly record struct SymbolRect(double X, double Y, double Width, double Height);

/// <summary>One drawn shape of a <see cref="SymbolGeometry"/>.</summary>
public abstract class SymbolShape
{
    private protected SymbolShape()
    {
    }

    /// <summary>How the shape is filled and stroked.</summary>
    public required SymbolPaint Paint { get; init; }
}

/// <summary>
/// A path: SVG <c>path</c> elements, and <c>rect</c>, <c>polygon</c>,
/// <c>polyline</c>, <c>line</c> and <c>ellipse</c> elements converted to the
/// same form.
/// </summary>
public sealed class SymbolPath : SymbolShape
{
    /// <summary>
    /// The path's segments in absolute coordinates. Every subpath starts
    /// with a <see cref="SymbolMoveTo"/>; quadratic and smooth curves are
    /// given as <see cref="SymbolCubicTo"/>.
    /// </summary>
    public required IReadOnlyList<SymbolPathSegment> Segments { get; init; }
}

/// <summary>A circle (SVG <c>circle</c>).</summary>
public sealed class SymbolCircle : SymbolShape
{
    /// <summary>The centre's x coordinate.</summary>
    public required double CenterX { get; init; }

    /// <summary>The centre's y coordinate.</summary>
    public required double CenterY { get; init; }

    /// <summary>The radius.</summary>
    public required double Radius { get; init; }
}

/// <summary>One segment of a <see cref="SymbolPath"/>, in absolute coordinates.</summary>
public abstract record SymbolPathSegment;

/// <summary>Starts a new subpath at a point.</summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
public sealed record SymbolMoveTo(double X, double Y) : SymbolPathSegment;

/// <summary>A straight line to a point.</summary>
/// <param name="X">The x coordinate.</param>
/// <param name="Y">The y coordinate.</param>
public sealed record SymbolLineTo(double X, double Y) : SymbolPathSegment;

/// <summary>A cubic Bézier curve to a point.</summary>
/// <param name="X1">The first control point's x coordinate.</param>
/// <param name="Y1">The first control point's y coordinate.</param>
/// <param name="X2">The second control point's x coordinate.</param>
/// <param name="Y2">The second control point's y coordinate.</param>
/// <param name="X">The end point's x coordinate.</param>
/// <param name="Y">The end point's y coordinate.</param>
public sealed record SymbolCubicTo(double X1, double Y1, double X2, double Y2, double X, double Y) : SymbolPathSegment;

/// <summary>An elliptical arc to a point, with SVG arc parameters.</summary>
/// <param name="RadiusX">The ellipse's x radius.</param>
/// <param name="RadiusY">The ellipse's y radius.</param>
/// <param name="XAxisRotation">The ellipse's rotation, in degrees.</param>
/// <param name="LargeArc">Whether the larger of the two candidate arcs is drawn.</param>
/// <param name="Sweep">Whether the arc is drawn in the positive-angle (clockwise, y down) direction.</param>
/// <param name="X">The end point's x coordinate.</param>
/// <param name="Y">The end point's y coordinate.</param>
public sealed record SymbolArcTo(double RadiusX, double RadiusY, double XAxisRotation, bool LargeArc, bool Sweep, double X, double Y) : SymbolPathSegment;

/// <summary>Closes the current subpath.</summary>
public sealed record SymbolClosePath : SymbolPathSegment
{
    /// <summary>The single instance.</summary>
    public static SymbolClosePath Instance { get; } = new();
}

/// <summary>
/// How a <see cref="SymbolShape"/> is filled and stroked, with SVG defaults
/// applied: a shape that names no fill is filled black, as an SVG renderer
/// draws it.
/// </summary>
public sealed record SymbolPaint
{
    /// <summary>The fill colour, or <see langword="null"/> for no fill.</summary>
    public SymbolColor? Fill { get; init; }

    /// <summary>The fill opacity, 0–1.</summary>
    public double FillOpacity { get; init; } = 1.0;

    /// <summary>The fill rule for self-overlapping or nested subpaths.</summary>
    public SymbolFillRule FillRule { get; init; } = SymbolFillRule.NonZero;

    /// <summary>The stroke colour, or <see langword="null"/> for no stroke.</summary>
    public SymbolColor? Stroke { get; init; }

    /// <summary>The stroke opacity, 0–1.</summary>
    public double StrokeOpacity { get; init; } = 1.0;

    /// <summary>The stroke width, in millimetres.</summary>
    public double StrokeWidth { get; init; } = 1.0;

    /// <summary>The stroke's line cap.</summary>
    public SymbolLineCap LineCap { get; init; } = SymbolLineCap.Butt;

    /// <summary>The stroke's line join.</summary>
    public SymbolLineJoin LineJoin { get; init; } = SymbolLineJoin.Miter;
}

/// <summary>
/// A symbol colour: an S-100 palette token (e.g. <c>"CHBLK"</c>), or, for
/// the rare SVG that writes a colour directly, that literal colour.
/// </summary>
public sealed record SymbolColor
{
    private SymbolColor(string? token, string? literal)
    {
        Token = token;
        Literal = literal;
    }

    /// <summary>The SVG default fill: literal black.</summary>
    public static SymbolColor DefaultFill { get; } = new(null, "#000000");

    /// <summary>The palette token, or <see langword="null"/> for a literal colour.</summary>
    public string? Token { get; }

    /// <summary>The literal colour as the SVG writes it, or <see langword="null"/> for a token.</summary>
    public string? Literal { get; }

    /// <summary>Creates a palette-token colour.</summary>
    /// <param name="token">The S-100 colour token.</param>
    /// <returns>The colour.</returns>
    public static SymbolColor FromToken(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return new SymbolColor(token, null);
    }

    /// <summary>Creates a literal colour.</summary>
    /// <param name="literal">The colour as written (e.g. <c>"#FF0000"</c> or <c>"red"</c>).</param>
    /// <returns>The colour.</returns>
    public static SymbolColor FromLiteral(string literal)
    {
        ArgumentException.ThrowIfNullOrEmpty(literal);
        return new SymbolColor(null, literal);
    }

    /// <summary>
    /// Resolves the colour against a palette: a token through
    /// <see cref="ColorPalette.Resolve"/>, a literal as written.
    /// </summary>
    /// <param name="palette">The active palette.</param>
    /// <returns>The colour as a CSS colour string (a <c>#RRGGBB</c> hex string for tokens).</returns>
    public string Resolve(ColorPalette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);
        return Token is not null ? palette.Resolve(Token) : Literal!;
    }

    /// <inheritdoc />
    public override string ToString() => Token ?? Literal!;
}

/// <summary>The SVG fill rule.</summary>
public enum SymbolFillRule
{
    /// <summary>SVG <c>nonzero</c>.</summary>
    NonZero,

    /// <summary>SVG <c>evenodd</c>.</summary>
    EvenOdd,
}

/// <summary>The SVG stroke line cap.</summary>
public enum SymbolLineCap
{
    /// <summary>SVG <c>butt</c>.</summary>
    Butt,

    /// <summary>SVG <c>round</c>.</summary>
    Round,

    /// <summary>SVG <c>square</c>.</summary>
    Square,
}

/// <summary>The SVG stroke line join.</summary>
public enum SymbolLineJoin
{
    /// <summary>SVG <c>miter</c>.</summary>
    Miter,

    /// <summary>SVG <c>round</c>.</summary>
    Round,

    /// <summary>SVG <c>bevel</c>.</summary>
    Bevel,
}
