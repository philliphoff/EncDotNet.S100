using System.Globalization;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// S-100 colour-token resolution shared by the vector rendering backends.
/// Resolves a token through a <see cref="ColorPalette"/>, falling back to an
/// inline hex literal (e.g. the S-421 RouteActionPoint <c>&lt;foreground&gt;</c>
/// emits a bare <c>AA44A8</c>) and finally to opaque black.
/// </summary>
public static class ColorResolver
{
    /// <summary>
    /// Builds a colour resolver from the given palette. The returned delegate
    /// maps a (possibly null) S-100 colour token to a resolved
    /// <see cref="RgbaColor"/>.
    /// </summary>
    public static Func<string?, RgbaColor> Create(ColorPalette? palette)
    {
        return token =>
        {
            if (string.IsNullOrEmpty(token))
                return Black;

            if (palette is not null && palette.TryResolve(token, out var hex))
                return HexToColor(hex);

            if (TryParseHexLiteral(token, out var literal))
                return literal;

            return Black;
        };
    }

    /// <summary>Opaque black.</summary>
    public static RgbaColor Black { get; } = new(0, 0, 0, 255);

    /// <summary>
    /// Returns <paramref name="color"/> with its alpha attenuated by
    /// <paramref name="transparency"/> (0 = unchanged opaque, 1 = fully
    /// transparent). When <paramref name="transparency"/> is null the colour is
    /// returned unchanged.
    /// </summary>
    public static RgbaColor ApplyTransparency(RgbaColor color, double? transparency)
    {
        if (!transparency.HasValue)
            return color;
        var t = Math.Clamp(transparency.Value, 0.0, 1.0);
        var a = (byte)Math.Round(color.A * (1.0 - t));
        return new RgbaColor(color.R, color.G, color.B, a);
    }

    /// <summary>
    /// Heuristic fallback colour for a symbol that has no resolvable SVG, keyed
    /// off the S-100 symbol name prefix (mirrors the legacy renderer's
    /// dot-fallback palette).
    /// </summary>
    public static RgbaColor ResolveSymbolColor(string? symbolRef, Func<string?, RgbaColor> resolveColor)
    {
        if (string.IsNullOrEmpty(symbolRef))
            return Black;

        if (symbolRef.StartsWith("SAFCON", StringComparison.Ordinal))
            return resolveColor("SNDG1");
        if (symbolRef.StartsWith("BOYCAR", StringComparison.Ordinal) ||
            symbolRef.StartsWith("BOYLAT", StringComparison.Ordinal))
            return resolveColor("CHBLK");
        if (symbolRef.StartsWith("BCNLAT", StringComparison.Ordinal))
            return resolveColor("CHBLK");
        if (symbolRef == "QUESMRK1")
            return new RgbaColor(200, 0, 200, 120);
        if (symbolRef.StartsWith("LIGHTS", StringComparison.Ordinal))
            return resolveColor("LITYW");

        return resolveColor("OUTLW");
    }

    private static RgbaColor HexToColor(string hex) =>
        TryParseHexLiteral(hex, out var color) ? color : Black;

    private static bool TryParseHexLiteral(string value, out RgbaColor color)
    {
        color = Black;
        if (string.IsNullOrEmpty(value)) return false;

        var span = value.AsSpan();
        if (span[0] == '#') span = span[1..];
        if (span.Length != 6 && span.Length != 8) return false;

        if (!int.TryParse(span[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !int.TryParse(span.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !int.TryParse(span.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
        {
            return false;
        }

        int a = 255;
        if (span.Length == 8 &&
            !int.TryParse(span.Slice(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out a))
        {
            return false;
        }

        color = new RgbaColor((byte)r, (byte)g, (byte)b, (byte)a);
        return true;
    }
}
