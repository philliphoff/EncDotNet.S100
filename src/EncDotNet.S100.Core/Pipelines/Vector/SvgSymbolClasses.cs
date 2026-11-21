namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Reads the S-100 Part 9 CSS class conventions on a symbol SVG element's
/// <c>class</c> attribute. Shared by the SVG processor the renderers use and
/// by <see cref="SymbolGeometryParser"/>, so both read a symbol the same way.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>fTOKEN</c> — fill with palette token <c>TOKEN</c></item>
///   <item><c>sTOKEN</c> — stroke with palette token <c>TOKEN</c></item>
///   <item><c>f0</c> — no fill</item>
///   <item><c>sl</c> — round line caps and joins</item>
///   <item><c>layout</c> — a layout-only element (symbol box, pivot point), not drawn</item>
///   <item><c>pivotPoint</c> — the layout circle marking the symbol's pivot</item>
/// </list>
/// Within one attribute, a later fill directive (<c>f0</c> or <c>fTOKEN</c>)
/// replaces an earlier one, and likewise for strokes.
/// </remarks>
public static class SvgSymbolClasses
{
    /// <summary>Reads a <c>class</c> attribute value.</summary>
    /// <param name="classAttribute">The attribute value, or <see langword="null"/>.</param>
    /// <returns>What the classes say.</returns>
    public static SvgSymbolClassStyle Read(string? classAttribute)
    {
        if (string.IsNullOrWhiteSpace(classAttribute))
            return SvgSymbolClassStyle.Empty;

        string? fillToken = null;
        var fillNone = false;
        string? strokeToken = null;
        var round = false;
        var layout = false;
        var pivot = false;
        List<string>? other = null;

        foreach (var cls in classAttribute.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (cls == "f0")
            {
                fillNone = true;
                fillToken = null;
            }
            else if (cls == "sl")
            {
                round = true;
            }
            else if (cls.Length > 1 && cls[0] == 'f' && char.IsUpper(cls[1]))
            {
                fillToken = cls[1..];
                fillNone = false;
            }
            else if (cls.Length > 1 && cls[0] == 's' && char.IsUpper(cls[1]))
            {
                strokeToken = cls[1..];
            }
            else
            {
                if (cls == "layout")
                    layout = true;
                else if (cls == "pivotPoint")
                    pivot = true;

                (other ??= []).Add(cls);
            }
        }

        return new SvgSymbolClassStyle(fillToken, fillNone, strokeToken, round, layout, pivot, other ?? (IReadOnlyList<string>)[]);
    }
}

/// <summary>What an S-100 symbol element's classes say (see <see cref="SvgSymbolClasses"/>).</summary>
/// <param name="FillToken">The fill palette token (<c>fTOKEN</c>), or <see langword="null"/>.</param>
/// <param name="FillNone">Whether the classes turn the fill off (<c>f0</c>).</param>
/// <param name="StrokeToken">The stroke palette token (<c>sTOKEN</c>), or <see langword="null"/>.</param>
/// <param name="RoundCapsAndJoins">Whether strokes use round caps and joins (<c>sl</c>).</param>
/// <param name="IsLayout">Whether the element is layout-only and not drawn (<c>layout</c>).</param>
/// <param name="IsPivotPoint">Whether the element marks the symbol's pivot (<c>pivotPoint</c>).</param>
/// <param name="OtherClasses">Classes that are not styling directives, including <c>layout</c> and <c>pivotPoint</c>.</param>
public sealed record SvgSymbolClassStyle(
    string? FillToken,
    bool FillNone,
    string? StrokeToken,
    bool RoundCapsAndJoins,
    bool IsLayout,
    bool IsPivotPoint,
    IReadOnlyList<string> OtherClasses)
{
    /// <summary>No classes.</summary>
    public static SvgSymbolClassStyle Empty { get; } = new(null, false, null, false, false, false, []);
}
