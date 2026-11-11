namespace EncDotNet.S100.Pipelines;

/// <summary>
/// A renderer-agnostic RGBA colour value.
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>Transparent (alpha = 0).</summary>
    public static RgbaColor Transparent => new(0, 0, 0, 0);

    /// <summary>
    /// Parses a hex colour string (e.g. "#61B7FF" or "#61B7FFCC") to an <see cref="RgbaColor"/>.
    /// </summary>
    public static RgbaColor FromHex(string hex)
    {
        ArgumentException.ThrowIfNullOrEmpty(hex);

        ReadOnlySpan<char> span = hex.AsSpan();
        if (span[0] == '#') span = span[1..];

        byte r = byte.Parse(span[..2], System.Globalization.NumberStyles.HexNumber);
        byte g = byte.Parse(span[2..4], System.Globalization.NumberStyles.HexNumber);
        byte b = byte.Parse(span[4..6], System.Globalization.NumberStyles.HexNumber);
        byte a = span.Length >= 8
            ? byte.Parse(span[6..8], System.Globalization.NumberStyles.HexNumber)
            : (byte)255;

        return new RgbaColor(r, g, b, a);
    }

    /// <summary>Returns the colour as a <c>#RRGGBB</c> hex string.</summary>
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}
