using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Nmea;

/// <summary>
/// Decodes the NMEA 0183 sentences this library knows into
/// <see cref="NmeaMessage"/> records, by formatter and whatever the talker:
/// <c>MWV</c>, <c>MWD</c>, <c>MTW</c>, <c>XDR</c>, <c>HDT</c>, <c>HDG</c>,
/// <c>VHW</c>, <c>RMC</c> and <c>VTG</c>.
/// </summary>
/// <remarks>
/// Field layouts follow NMEA 0183 v4.11. Sentences from older versions
/// that lack trailing fields (the RMC and VTG mode indicator, the RMC
/// navigational status) decode too: a missing field is a null one.
/// </remarks>
public static class NmeaDecoder
{
    private const double MetresPerSecondPerStatuteMph = 0.44704;

    /// <summary>The formatters <see cref="TryDecode(NmeaSentence, out NmeaMessage?)"/> decodes.</summary>
    public static IReadOnlySet<string> SupportedFormatters { get; } =
        new HashSet<string>(["MWV", "MWD", "MTW", "XDR", "HDT", "HDG", "VHW", "RMC", "VTG"], StringComparer.Ordinal);

    /// <summary>Decodes <paramref name="sentence"/>.</summary>
    /// <param name="sentence">A parsed sentence.</param>
    /// <param name="message">The decoded message, or <see langword="null"/>.</param>
    /// <returns>Whether the sentence is one this decoder knows (proprietary and encapsulated sentences never are).</returns>
    public static bool TryDecode(NmeaSentence sentence, [NotNullWhen(true)] out NmeaMessage? message)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        message = null;
        if (sentence.IsProprietary || sentence.StartCharacter != '$')
            return false;

        message = sentence.Formatter switch
        {
            "MWV" => DecodeMwv(sentence),
            "MWD" => DecodeMwd(sentence),
            "MTW" => DecodeMtw(sentence),
            "XDR" => DecodeXdr(sentence),
            "HDT" => new HdtSentence(Direction(sentence, 0)) { Talker = sentence.Talker },
            "HDG" => DecodeHdg(sentence),
            "VHW" => DecodeVhw(sentence),
            "RMC" => DecodeRmc(sentence),
            "VTG" => DecodeVtg(sentence),
            _ => null,
        };
        return message is not null;
    }

    /// <summary>Parses <paramref name="line"/> and decodes it.</summary>
    /// <param name="line">One line of an NMEA stream.</param>
    /// <param name="message">The decoded message, or <see langword="null"/>.</param>
    /// <param name="options">The parse options; <see langword="null"/> for the defaults.</param>
    /// <returns>Whether the line is a valid sentence of a known formatter.</returns>
    public static bool TryDecode(string? line, [NotNullWhen(true)] out NmeaMessage? message, NmeaParseOptions? options = null)
    {
        message = null;
        return NmeaSentence.TryParse(line, out var sentence, out _, options) && TryDecode(sentence, out message);
    }

    /// <summary>Converts a speed in an NMEA unit letter (<c>N</c> knots, <c>M</c> m/s, <c>K</c> km/h, <c>S</c> statute mph).</summary>
    /// <param name="value">The value.</param>
    /// <param name="unit">The unit letter.</param>
    /// <returns>The speed, or <see langword="null"/> for an unknown unit or a negative value.</returns>
    public static Speed? ToSpeed(double? value, char? unit)
    {
        if (value is not { } v || v < 0)
            return null;
        return unit switch
        {
            'N' => Speed.FromKnots(v),
            'M' => Speed.FromMetresPerSecond(v),
            'K' => Speed.FromKilometresPerHour(v),
            'S' => Speed.FromMetresPerSecond(v * MetresPerSecondPerStatuteMph),
            _ => null,
        };
    }

    private static MwvSentence? DecodeMwv(NmeaSentence s)
    {
        WindReference? reference = s.Letter(1) switch
        {
            'R' => WindReference.Relative,
            'T' => WindReference.True,
            _ => null,
        };
        if (reference is null)
            return null;

        return new MwvSentence(
            Direction(s, 0),
            reference.Value,
            ToSpeed(s.Number(2), s.Letter(3)),
            IsValid: s.Letter(4) != 'V')
        {
            Talker = s.Talker,
        };
    }

    private static MwdSentence DecodeMwd(NmeaSentence s)
    {
        // $--MWD,x.x,T,x.x,M,x.x,N,x.x,M
        var trueDir = s.Letter(1) == 'T' ? Direction(s, 0) : null;
        var magDir = s.Letter(3) == 'M' ? Direction(s, 2) : null;
        var speed = (s.Letter(5) == 'N' ? ToSpeed(s.Number(4), 'N') : null)
            ?? (s.Letter(7) == 'M' ? ToSpeed(s.Number(6), 'M') : null);
        return new MwdSentence(trueDir, magDir, speed) { Talker = s.Talker };
    }

    private static MtwSentence DecodeMtw(NmeaSentence s) =>
        new(s.Letter(1) == 'C' && s.Number(0) is { } c && c is > -5 and < 50 ? c : null) { Talker = s.Talker };

    private static XdrSentence DecodeXdr(NmeaSentence s)
    {
        var measurements = new List<XdrMeasurement>(s.Fields.Count / 4);
        for (int i = 0; i < s.Fields.Count; i += 4)
        {
            if (s.Letter(i) is not { } type)
                continue;
            measurements.Add(new XdrMeasurement(type, s.Number(i + 1), s.Field(i + 2), s.Field(i + 3)));
        }
        return new XdrSentence(measurements) { Talker = s.Talker };
    }

    private static HdgSentence DecodeHdg(NmeaSentence s) =>
        new(Direction(s, 0), Signed(s, 1, 2), Signed(s, 3, 4)) { Talker = s.Talker };

    private static VhwSentence DecodeVhw(NmeaSentence s)
    {
        // $--VHW,x.x,T,x.x,M,x.x,N,x.x,K
        var trueHeading = s.Letter(1) == 'T' ? Direction(s, 0) : null;
        var magHeading = s.Letter(3) == 'M' ? Direction(s, 2) : null;
        var stw = (s.Letter(5) == 'N' ? ToSpeed(s.Number(4), 'N') : null)
            ?? (s.Letter(7) == 'K' ? ToSpeed(s.Number(6), 'K') : null);
        return new VhwSentence(trueHeading, magHeading, stw) { Talker = s.Talker };
    }

    private static RmcSentence DecodeRmc(NmeaSentence s)
    {
        // $--RMC,hhmmss.ss,A,llll.ll,a,yyyyy.yy,a,x.x,x.x,ddmmyy,x.x,a,m,s
        bool valid = s.Letter(1) == 'A' && s.Letter(11) != 'N';
        return new RmcSentence(
            Time(s.Field(0), s.Field(8)),
            valid ? Position(s, 2) : null,
            valid ? ToSpeed(s.Number(6), 'N') : null,
            valid ? Direction(s, 7) : null,
            Signed(s, 9, 10),
            valid)
        {
            Talker = s.Talker,
        };
    }

    private static VtgSentence DecodeVtg(NmeaSentence s)
    {
        // $--VTG,x.x,T,x.x,M,x.x,N,x.x,K,m
        bool valid = s.Letter(8) != 'N';
        var cog = valid && s.Letter(1) == 'T' ? Direction(s, 0) : null;
        var sog = valid
            ? (s.Letter(5) == 'N' ? ToSpeed(s.Number(4), 'N') : null) ?? (s.Letter(7) == 'K' ? ToSpeed(s.Number(6), 'K') : null)
            : null;
        return new VtgSentence(cog, sog, valid) { Talker = s.Talker };
    }

    // A direction in degrees, 0 to 360 (360 read as 0).
    private static Angle? Direction(NmeaSentence s, int index) =>
        s.Number(index) is { } d && d is >= 0 and <= 360 ? NmeaAngles.Normalise(Angle.FromDegrees(d)) : null;

    // A value with an E/W hemisphere letter, east positive.
    private static Angle? Signed(NmeaSentence s, int valueIndex, int letterIndex)
    {
        if (s.Number(valueIndex) is not { } v || v is < 0 or > 180)
            return null;
        return s.Letter(letterIndex) switch
        {
            'E' => Angle.FromDegrees(v),
            'W' => Angle.FromDegrees(-v),
            _ => null,
        };
    }

    private static GeoPosition? Position(NmeaSentence s, int index)
    {
        if (Coordinate(s.Field(index), 2) is not { } lat || Coordinate(s.Field(index + 2), 3) is not { } lon)
            return null;
        lat = s.Letter(index + 1) switch { 'N' => lat, 'S' => -lat, _ => double.NaN };
        lon = s.Letter(index + 3) switch { 'E' => lon, 'W' => -lon, _ => double.NaN };
        return lat is >= -90 and <= 90 && lon is >= -180 and <= 180 ? new GeoPosition(lat, lon) : null;
    }

    // ddmm.mmmm or dddmm.mmmm to decimal degrees.
    private static double? Coordinate(string field, int degreeDigits)
    {
        if (field.Length < degreeDigits + 2
            || !int.TryParse(field.AsSpan(0, degreeDigits), NumberStyles.None, CultureInfo.InvariantCulture, out int degrees)
            || !double.TryParse(field.AsSpan(degreeDigits), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double minutes)
            || minutes >= 60)
        {
            return null;
        }
        return degrees + minutes / 60;
    }

    private static DateTimeOffset? Time(string time, string date)
    {
        if (time.Length < 6 || date.Length != 6
            || !int.TryParse(time.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int hh)
            || !int.TryParse(time.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int mm)
            || !double.TryParse(time.AsSpan(4), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double ss)
            || !int.TryParse(date.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            || !int.TryParse(date.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int month)
            || !int.TryParse(date.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int yy)
            || hh > 23 || mm > 59 || ss >= 61 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2000 + yy, month))
        {
            return null;
        }

        // Two-digit years: 80-99 are 1980-1999, the rest 2000-2079.
        int year = yy >= 80 ? 1900 + yy : 2000 + yy;
        var midnight = new DateTimeOffset(year, month, day, 0, 0, 0, TimeSpan.Zero);
        return midnight + TimeSpan.FromHours(hh) + TimeSpan.FromMinutes(mm) + TimeSpan.FromSeconds(Math.Min(ss, 59.999));
    }
}
