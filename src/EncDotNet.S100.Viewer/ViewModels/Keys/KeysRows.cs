using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows.Input;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.ViewModels.Keys;

/// <summary>The colour a badge, status or banner takes from the theme (#845).</summary>
internal enum KeysTone
{
    /// <summary>Muted text, neutral fill.</summary>
    Neutral,

    /// <summary>The accent colour.</summary>
    Accent,

    /// <summary>Success (green).</summary>
    Success,

    /// <summary>Warning (amber).</summary>
    Warning,

    /// <summary>Destructive (red).</summary>
    Destructive,
}

/// <summary>A small-caps badge on a row, e.g. VESSEL or BUILT IN.</summary>
internal sealed record KeysBadge(string Text, KeysTone Tone)
{
    public bool IsNeutral => Tone == KeysTone.Neutral;
    public bool IsAccent => Tone == KeysTone.Accent;
    public bool IsSuccess => Tone == KeysTone.Success;
    public bool IsWarning => Tone == KeysTone.Warning;
    public bool IsDestructive => Tone == KeysTone.Destructive;
}

/// <summary>An action chip under a row: Details, Replace…, Remove, Use…</summary>
internal sealed record KeysRowAction(string Id, string Label, ICommand Command, bool IsDestructive = false);

/// <summary>
/// A row in the Identities or Trusted authorities list (#845 B4): icon, title,
/// badges, a right-aligned status, a mono line, a muted sub-line, an optional
/// reference id and action chips.
/// </summary>
internal sealed class KeysRow
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public IReadOnlyList<KeysBadge> Badges { get; init; } = [];
    public string? Status { get; init; }
    public KeysTone StatusTone { get; init; }
    public bool StatusIsRevoked { get; init; }
    public string? Mono { get; init; }
    public string Sub { get; init; } = "";
    public string? Reference { get; init; }
    public IReadOnlyList<KeysRowAction> Actions { get; init; } = [];

    /// <summary>True for a trusted authority (shield icon); false for an identity (id card).</summary>
    public bool IsAuthority { get; init; }

    /// <summary>True for a turned-off authority, drawn faded.</summary>
    public bool IsDimmed { get; init; }

    public bool HasStatus => Status is not null;
    public bool StatusIsWarning => StatusTone == KeysTone.Warning;
    public bool StatusIsDestructive => StatusTone == KeysTone.Destructive;
    public bool StatusIsNeutral => StatusTone == KeysTone.Neutral;
    public bool StatusShowsClock => StatusTone == KeysTone.Warning;
    public bool HasMono => Mono is not null;
    public bool HasReference => Reference is not null;
    public bool HasActions => Actions.Count > 0;
    public double Opacity => IsDimmed ? 0.5 : 1;
}

/// <summary>A label and value in the import check or a details dialog.</summary>
internal sealed record KeysField(string Label, string Value, bool IsMono = false, KeysTone Tone = KeysTone.Neutral, bool ShowsCheck = false)
{
    public bool IsWarning => Tone == KeysTone.Warning;
    public bool IsSuccess => Tone == KeysTone.Success;
    public bool IsDestructive => Tone == KeysTone.Destructive;
}

/// <summary>How the Keys &amp; certificates page words certificates (#845).</summary>
internal static class CertificateText
{
    /// <summary>A date as the page shows it: the machine's local date, ISO style.</summary>
    public static string Date(DateTimeOffset at) => at.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>"SHA-256 2B7E 15A1 9C04 … 0C9D": the start and end of the fingerprint.</summary>
    public static string ShortFingerprint(X509Certificate2 certificate)
    {
        var hex = Convert.ToHexString(SHA256.HashData(certificate.RawData));
        return string.Format(CultureInfo.InvariantCulture, Strings.Keys_FingerprintShortFormat,
            $"{hex[..4]} {hex[4..8]} {hex[8..12]}", hex[^4..]);
    }

    /// <summary>The whole SHA-256 fingerprint in groups of four.</summary>
    public static string Fingerprint(X509Certificate2 certificate) =>
        string.Join(' ', Convert.ToHexString(SHA256.HashData(certificate.RawData)).Chunk(4).Select(c => new string(c)));

    /// <summary>"Elliptic curve P-384", "RSA 2048-bit" or the key's algorithm name.</summary>
    public static string KeyDescription(X509Certificate2 certificate)
    {
        using (var ec = certificate.GetECDsaPublicKey())
        {
            if (ec is not null)
            {
                var curve = ec.KeySize switch { 256 => "P-256", 384 => "P-384", 521 => "P-521", var bits => $"{bits}-bit" };
                return string.Format(CultureInfo.CurrentCulture, Strings.Keys_KeyEllipticCurveFormat, curve);
            }
        }

        using (var rsa = certificate.GetRSAPublicKey())
        {
            if (rsa is not null)
                return string.Format(CultureInfo.CurrentCulture, Strings.Keys_KeyRsaFormat, rsa.KeySize);
        }

        return certificate.PublicKey.Oid.FriendlyName ?? certificate.PublicKey.Oid.Value ?? "";
    }

    /// <summary>The organisation (O) of a certificate's subject, or its common name.</summary>
    public static string Organisation(X509Certificate2 certificate)
    {
        foreach (var rdn in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            if (!rdn.HasMultipleElements && rdn.GetSingleElementType().Value == "2.5.4.10" && rdn.GetSingleElementValue() is { Length: > 0 } o)
                return o;
        }

        return certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
    }

    /// <summary>Whole days from <paramref name="now"/> to <paramref name="end"/>, rounded up.</summary>
    public static int DaysUntil(DateTimeOffset end, DateTimeOffset now) => (int)Math.Ceiling((end - now).TotalDays);

    /// <summary>"Ends today", "Ends in 1 day", "Ends in 12 days".</summary>
    public static string EndsIn(int days) => days switch
    {
        <= 0 => Strings.Keys_Status_EndsToday,
        1 => Strings.Keys_Status_EndsInOneDay,
        _ => string.Format(CultureInfo.CurrentCulture, Strings.Keys_Status_EndsInDaysFormat, days),
    };
}
