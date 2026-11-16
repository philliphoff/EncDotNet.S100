using System.Globalization;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Parses SVG path data (the <c>d</c> attribute) into absolute
/// <see cref="SymbolPathSegment"/>s.
/// </summary>
/// <remarks>
/// Handles every SVG 1.1 command, absolute and relative: <c>M L H V Z</c>,
/// <c>C S</c>, <c>Q T</c> (raised to cubics) and <c>A</c>. Parsing stops at
/// the first malformed token, keeping what came before, as SVG renderers do.
/// </remarks>
internal static class SvgPathData
{
    public static List<SymbolPathSegment> Parse(string? d)
    {
        var segments = new List<SymbolPathSegment>();
        if (string.IsNullOrWhiteSpace(d))
            return segments;

        var r = new Reader(d);
        double cx = 0, cy = 0;           // current point
        double sx = 0, sy = 0;           // subpath start
        double lcx = 0, lcy = 0;         // last cubic control point (for S)
        double lqx = 0, lqy = 0;         // last quadratic control point (for T)
        var prev = ' ';
        var command = ' ';

        while (true)
        {
            r.SkipSeparators();
            if (r.AtEnd)
                break;

            if (r.PeekCommand(out var c))
            {
                command = c;
                r.Advance();
            }
            else if (command is ' ' or 'Z' or 'z')
            {
                break; // numbers with no command to repeat
            }

            var rel = char.IsLower(command);
            var ox = rel ? cx : 0;
            var oy = rel ? cy : 0;

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    if (!r.Number(out var mx) || !r.Number(out var my))
                        return segments;
                    cx = sx = ox + mx;
                    cy = sy = oy + my;
                    segments.Add(new SymbolMoveTo(cx, cy));
                    // Subsequent pairs are implicit line-tos.
                    command = rel ? 'l' : 'L';
                    prev = 'M';
                    continue;

                case 'L':
                    if (!r.Number(out var lx) || !r.Number(out var ly))
                        return segments;
                    cx = ox + lx;
                    cy = oy + ly;
                    segments.Add(new SymbolLineTo(cx, cy));
                    break;

                case 'H':
                    if (!r.Number(out var hx))
                        return segments;
                    cx = ox + hx;
                    segments.Add(new SymbolLineTo(cx, cy));
                    break;

                case 'V':
                    if (!r.Number(out var vy))
                        return segments;
                    cy = oy + vy;
                    segments.Add(new SymbolLineTo(cx, cy));
                    break;

                case 'C':
                {
                    if (!r.Number(out var x1) || !r.Number(out var y1) || !r.Number(out var x2) ||
                        !r.Number(out var y2) || !r.Number(out var x) || !r.Number(out var y))
                        return segments;
                    lcx = ox + x2;
                    lcy = oy + y2;
                    cx = ox + x;
                    cy = oy + y;
                    segments.Add(new SymbolCubicTo(ox + x1, oy + y1, lcx, lcy, cx, cy));
                    break;
                }

                case 'S':
                {
                    if (!r.Number(out var x2) || !r.Number(out var y2) || !r.Number(out var x) || !r.Number(out var y))
                        return segments;
                    var reflect = prev is 'C' or 'S';
                    var x1 = reflect ? 2 * cx - lcx : cx;
                    var y1 = reflect ? 2 * cy - lcy : cy;
                    lcx = ox + x2;
                    lcy = oy + y2;
                    cx = ox + x;
                    cy = oy + y;
                    segments.Add(new SymbolCubicTo(x1, y1, lcx, lcy, cx, cy));
                    break;
                }

                case 'Q':
                {
                    if (!r.Number(out var qx) || !r.Number(out var qy) || !r.Number(out var x) || !r.Number(out var y))
                        return segments;
                    lqx = ox + qx;
                    lqy = oy + qy;
                    var (ex, ey) = (ox + x, oy + y);
                    segments.Add(QuadToCubic(cx, cy, lqx, lqy, ex, ey));
                    (cx, cy) = (ex, ey);
                    break;
                }

                case 'T':
                {
                    if (!r.Number(out var x) || !r.Number(out var y))
                        return segments;
                    var reflect = prev is 'Q' or 'T';
                    lqx = reflect ? 2 * cx - lqx : cx;
                    lqy = reflect ? 2 * cy - lqy : cy;
                    var (ex, ey) = (ox + x, oy + y);
                    segments.Add(QuadToCubic(cx, cy, lqx, lqy, ex, ey));
                    (cx, cy) = (ex, ey);
                    break;
                }

                case 'A':
                {
                    if (!r.Number(out var rx) || !r.Number(out var ry) || !r.Number(out var rot) ||
                        !r.Flag(out var large) || !r.Flag(out var sweep) || !r.Number(out var x) || !r.Number(out var y))
                        return segments;
                    cx = ox + x;
                    cy = oy + y;
                    segments.Add(new SymbolArcTo(Math.Abs(rx), Math.Abs(ry), rot, large, sweep, cx, cy));
                    break;
                }

                case 'Z':
                    segments.Add(SymbolClosePath.Instance);
                    cx = sx;
                    cy = sy;
                    break;

                default:
                    return segments;
            }

            prev = char.ToUpperInvariant(command);
        }

        return segments;
    }

    /// <summary>Reads a whitespace/comma separated list of numbers (viewBox, points).</summary>
    public static List<double> Numbers(string? text)
    {
        var numbers = new List<double>();
        if (string.IsNullOrWhiteSpace(text))
            return numbers;

        var r = new Reader(text);
        while (true)
        {
            r.SkipSeparators();
            if (r.AtEnd || !r.Number(out var v))
                break;
            numbers.Add(v);
        }

        return numbers;
    }

    private static SymbolCubicTo QuadToCubic(double x0, double y0, double qx, double qy, double x, double y) =>
        new(x0 + 2.0 / 3.0 * (qx - x0), y0 + 2.0 / 3.0 * (qy - y0),
            x + 2.0 / 3.0 * (qx - x), y + 2.0 / 3.0 * (qy - y),
            x, y);

    private ref struct Reader(string text)
    {
        private readonly ReadOnlySpan<char> _s = text;
        private int _i;

        public readonly bool AtEnd => _i >= _s.Length;

        public void Advance() => _i++;

        public void SkipSeparators()
        {
            while (_i < _s.Length && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ','))
                _i++;
        }

        public readonly bool PeekCommand(out char c)
        {
            c = _i < _s.Length ? _s[_i] : '\0';
            return c is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v' or 'C' or 'c' or 'S' or 's'
                or 'Q' or 'q' or 'T' or 't' or 'A' or 'a' or 'Z' or 'z';
        }

        public bool Flag(out bool value)
        {
            SkipSeparators();
            value = false;
            if (_i >= _s.Length || (_s[_i] != '0' && _s[_i] != '1'))
                return false;
            value = _s[_i] == '1';
            _i++;
            return true;
        }

        public bool Number(out double value)
        {
            SkipSeparators();
            value = 0;
            var start = _i;
            if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                _i++;

            var digits = false;
            while (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
            {
                _i++;
                digits = true;
            }

            if (_i < _s.Length && _s[_i] == '.')
            {
                _i++;
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
                {
                    _i++;
                    digits = true;
                }
            }

            if (!digits)
            {
                _i = start;
                return false;
            }

            if (_i < _s.Length && (_s[_i] == 'e' || _s[_i] == 'E'))
            {
                var mark = _i;
                _i++;
                if (_i < _s.Length && (_s[_i] == '+' || _s[_i] == '-'))
                    _i++;
                var expDigits = false;
                while (_i < _s.Length && char.IsAsciiDigit(_s[_i]))
                {
                    _i++;
                    expDigits = true;
                }

                if (!expDigits)
                    _i = mark;
            }

            return double.TryParse(_s[start.._i], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
