using System.Globalization;

namespace EncDotNet.S100.Nmea;

/// <summary>
/// One NMEA 0183 sentence, split into its address and data fields, with its
/// checksum checked.
/// </summary>
/// <remarks>
/// <para>
/// A sentence is <c>$</c> (or <c>!</c> for encapsulated sentences such as AIS),
/// an address field, comma-separated data fields, and <c>*hh</c>: the two hex
/// digits of the XOR of every character between the start character and the
/// <c>*</c> (NMEA 0183 v4.11 §5.2). The address of an approved sentence is a
/// two-letter talker (<c>II</c>, <c>WI</c>, <c>GP</c> and so on) and a
/// three-letter formatter (<c>MWV</c>); a proprietary one is <c>P</c> and a
/// maker's code, kept whole in <see cref="Formatter"/>.
/// </para>
/// <para>
/// The parser is lenient where real streams are untidy and strict where a
/// wrong value could be read: it skips an NMEA 4 tag block (<c>\...\</c>), and
/// when a line holds the tail of a cut-off sentence glued to a whole one it
/// keeps only the last sentence; but a sentence whose checksum is wrong, or
/// that has none, is rejected (unless <see cref="NmeaParseOptions.RequireChecksum"/>
/// is off), as is anything without an address.
/// </para>
/// </remarks>
public sealed record NmeaSentence
{
    /// <summary>The start character: <c>$</c>, or <c>!</c> for an encapsulated sentence.</summary>
    public required char StartCharacter { get; init; }

    /// <summary>The talker id, e.g. <c>WI</c>; <c>P</c> for a proprietary sentence.</summary>
    public required string Talker { get; init; }

    /// <summary>The sentence formatter, e.g. <c>MWV</c>; for a proprietary sentence, the rest of the address (e.g. <c>GRME</c>).</summary>
    public required string Formatter { get; init; }

    /// <summary>The data fields after the address, empty strings for null fields.</summary>
    public required IReadOnlyList<string> Fields { get; init; }

    /// <summary>Whether the sentence carried a checksum (which then matched).</summary>
    public required bool HasChecksum { get; init; }

    /// <summary>The sentence as read, from the start character to the checksum.</summary>
    public required string Raw { get; init; }

    /// <summary>Whether the sentence is proprietary (address starting <c>P</c>).</summary>
    public bool IsProprietary => Talker == "P";

    /// <summary>The field at <paramref name="index"/>, or an empty string past the end.</summary>
    /// <param name="index">The zero-based data field index (the address is not a field).</param>
    public string Field(int index) => index >= 0 && index < Fields.Count ? Fields[index] : string.Empty;

    /// <summary>The field at <paramref name="index"/> as an invariant-culture number, or <see langword="null"/> when empty or not a number.</summary>
    /// <param name="index">The zero-based data field index.</param>
    public double? Number(int index) =>
        double.TryParse(Field(index), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v)
            ? v
            : null;

    /// <summary>The field at <paramref name="index"/> as a single character, or <see langword="null"/> when empty.</summary>
    /// <param name="index">The zero-based data field index.</param>
    public char? Letter(int index) => Field(index) is { Length: > 0 } f ? char.ToUpperInvariant(f[0]) : null;

    /// <summary>Parses <paramref name="line"/>, throwing when it isn't a valid sentence.</summary>
    /// <param name="line">One line of an NMEA stream.</param>
    /// <param name="options">The parse options; <see langword="null"/> for the defaults.</param>
    /// <exception cref="FormatException">The line is not a valid sentence; the message says why.</exception>
    public static NmeaSentence Parse(string line, NmeaParseOptions? options = null) =>
        TryParse(line, out var sentence, out var error, options)
            ? sentence
            : throw new FormatException($"Not a valid NMEA 0183 sentence ({error}): {line}");

    /// <summary>Parses <paramref name="line"/>.</summary>
    /// <param name="line">One line of an NMEA stream, with or without its line ending.</param>
    /// <param name="sentence">The sentence, when the line is valid.</param>
    /// <param name="error">Why the line was rejected, or <see cref="NmeaParseError.None"/>.</param>
    /// <param name="options">The parse options; <see langword="null"/> for the defaults.</param>
    /// <returns>Whether the line is a valid sentence.</returns>
    public static bool TryParse(
        string? line,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out NmeaSentence? sentence,
        out NmeaParseError error,
        NmeaParseOptions? options = null)
    {
        sentence = null;
        options ??= NmeaParseOptions.Default;

        var span = (line ?? string.Empty).AsSpan().Trim();
        if (span.IsEmpty)
        {
            error = NmeaParseError.Empty;
            return false;
        }

        if (span.Length > options.MaxLength)
        {
            error = NmeaParseError.TooLong;
            return false;
        }

        // An NMEA 4 tag block: \s:r3,c:1700000000*hh\$WIMWV,...
        if (span[0] == '\\')
        {
            int end = span[1..].IndexOf('\\');
            if (end < 0)
            {
                error = NmeaParseError.NoStartCharacter;
                return false;
            }
            span = span[(end + 2)..];
        }

        // The last start character begins the sentence: anything before it is
        // the tail of a sentence cut off mid-line.
        int start = span.LastIndexOfAny('$', '!');
        if (start < 0)
        {
            error = NmeaParseError.NoStartCharacter;
            return false;
        }
        span = span[start..];

        int star = span.IndexOf('*');
        bool hasChecksum = star >= 0;
        var body = hasChecksum ? span[1..star] : span[1..];
        if (hasChecksum)
        {
            var hex = span[(star + 1)..];
            if (hex.Length != 2
                || !byte.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte expected))
            {
                error = NmeaParseError.MalformedChecksum;
                return false;
            }
            if (ComputeChecksum(body) != expected)
            {
                error = NmeaParseError.ChecksumMismatch;
                return false;
            }
        }
        else if (options.RequireChecksum)
        {
            error = NmeaParseError.MissingChecksum;
            return false;
        }

        foreach (char c in body)
        {
            if (c < 0x20 || c > 0x7E)
            {
                error = NmeaParseError.InvalidCharacter;
                return false;
            }
        }

        var parts = body.ToString().Split(',');
        string address = parts[0];
        string talker, formatter;
        if (address.Length >= 2 && address[0] == 'P')
        {
            talker = "P";
            formatter = address[1..];
        }
        else if (address.Length == 5 && IsAlphanumeric(address))
        {
            talker = address[..2];
            formatter = address[2..];
        }
        else
        {
            error = NmeaParseError.InvalidAddress;
            return false;
        }

        sentence = new NmeaSentence
        {
            StartCharacter = span[0],
            Talker = talker,
            Formatter = formatter,
            Fields = parts.Length > 1 ? parts[1..] : [],
            HasChecksum = hasChecksum,
            Raw = span.ToString(),
        };
        error = NmeaParseError.None;
        return true;
    }

    /// <summary>The NMEA checksum of <paramref name="body"/>: the XOR of its characters.</summary>
    /// <param name="body">The characters between the start character and the <c>*</c>.</param>
    public static byte ComputeChecksum(ReadOnlySpan<char> body)
    {
        byte sum = 0;
        foreach (char c in body)
            sum ^= (byte)c;
        return sum;
    }

    /// <summary>Builds a sentence's text with its checksum, e.g. <c>$WIMWV,045.0,R,12.0,N,A*hh</c>.</summary>
    /// <param name="body">The address and fields without the start character, e.g. <c>WIMWV,045.0,R,12.0,N,A</c>.</param>
    /// <param name="startCharacter">The start character.</param>
    public static string Format(string body, char startCharacter = '$')
    {
        ArgumentNullException.ThrowIfNull(body);
        return string.Create(CultureInfo.InvariantCulture, $"{startCharacter}{body}*{ComputeChecksum(body):X2}");
    }

    private static bool IsAlphanumeric(string s)
    {
        foreach (char c in s)
        {
            if (!char.IsAsciiLetterOrDigit(c))
                return false;
        }
        return true;
    }
}

/// <summary>Options for <see cref="NmeaSentence.TryParse"/>.</summary>
public sealed record NmeaParseOptions
{
    /// <summary>The defaults: a checksum is required, lines up to 512 characters.</summary>
    public static NmeaParseOptions Default { get; } = new();

    /// <summary>
    /// Whether a sentence without a checksum is rejected. NMEA 0183 makes the
    /// checksum mandatory for every sentence this library decodes, and without
    /// one a cut-off sentence can't be told from a whole one. Defaults to
    /// <see langword="true"/>.
    /// </summary>
    public bool RequireChecksum { get; init; } = true;

    /// <summary>
    /// The longest line accepted, tag block included. The standard's limit is
    /// 82 characters, but proprietary sentences and tag blocks often run over,
    /// so the default is a generous 512: past that the line is noise.
    /// </summary>
    public int MaxLength { get; init; } = 512;
}

/// <summary>Why <see cref="NmeaSentence.TryParse"/> rejected a line.</summary>
public enum NmeaParseError
{
    /// <summary>The line is a valid sentence.</summary>
    None,

    /// <summary>The line is empty or white space.</summary>
    Empty,

    /// <summary>The line is longer than <see cref="NmeaParseOptions.MaxLength"/>.</summary>
    TooLong,

    /// <summary>The line has no <c>$</c> or <c>!</c>: the middle or end of a cut-off sentence, or noise.</summary>
    NoStartCharacter,

    /// <summary>The line has no <c>*hh</c> checksum, and one is required: often a sentence cut off before its end.</summary>
    MissingChecksum,

    /// <summary>The characters after <c>*</c> aren't two hex digits.</summary>
    MalformedChecksum,

    /// <summary>The checksum doesn't match the sentence: it was corrupted in transit.</summary>
    ChecksumMismatch,

    /// <summary>The sentence holds a control or non-ASCII character.</summary>
    InvalidCharacter,

    /// <summary>The address field isn't a talker and formatter (five letters or digits) or a proprietary <c>P</c> address.</summary>
    InvalidAddress,
}
