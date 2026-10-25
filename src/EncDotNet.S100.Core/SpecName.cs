using System.Text.RegularExpressions;

namespace EncDotNet.S100.Core;

/// <summary>
/// Helpers for normalising S-100 product specification short names into the
/// canonical <c>"S-NNN"</c> form (e.g. <c>"S-101"</c>, <c>"S-102"</c>), and
/// the closed set of non-S-100 format names a product may be registered under
/// (<see cref="Grib2"/>, <see cref="NetCdf"/>, <see cref="Cap"/>).
/// </summary>
/// <remarks>
/// A non-S-100 format keeps its own name rather than a made-up <c>S-0nn</c>
/// number, and its edition is the format's version: <c>GRIB2/2.0.0</c>,
/// <c>NetCDF/4.0.0</c> (or <c>NetCDF/3.0.0</c>), <c>CAP/1.2.0</c>. Whether a
/// product is S-100 is a trait of its registration
/// (<c>ProductRegistration.IsS100</c>), so hosts ask the product registry
/// rather than parse names.
/// </remarks>
public static partial class SpecName
{
    /// <summary>WMO GRIB edition 2 (FM 92), canonical name <c>"GRIB2"</c>.</summary>
    public const string Grib2 = "GRIB2";

    /// <summary>Unidata NetCDF (classic and NetCDF-4), canonical name <c>"NetCDF"</c>.</summary>
    public const string NetCdf = "NetCDF";

    /// <summary>OASIS Common Alerting Protocol, canonical name <c>"CAP"</c>.</summary>
    public const string Cap = "CAP";

    /// <summary>
    /// The non-S-100 format names <see cref="Normalize"/> accepts, in their
    /// canonical casing. The set is closed: a new format is added here.
    /// </summary>
    public static IReadOnlyList<string> NonS100Formats { get; } = [Grib2, NetCdf, Cap];

    /// <summary>
    /// Whether <paramref name="name"/> (trimmed, any casing) is one of the
    /// <see cref="NonS100Formats"/>.
    /// </summary>
    public static bool IsNonS100Format(string? name) => TryGetNonS100Format(name, out _);

    private static bool TryGetNonS100Format(string? name, out string canonical)
    {
        canonical = string.Empty;
        if (name is null) return false;
        var trimmed = name.Trim();
        foreach (var format in NonS100Formats)
        {
            if (string.Equals(trimmed, format, StringComparison.OrdinalIgnoreCase))
            {
                canonical = format;
                return true;
            }
        }

        return false;
    }

    [GeneratedRegex(@"^S-?(?<digits>\d{2,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortNameRegex();

    [GeneratedRegex(@"^INT\.IHO\.S-?(?<digits>\d{2,3})(?:\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ProductIdentifierRegex();

    /// <summary>
    /// Normalises a product specification short name into <c>"S-NNN"</c> form.
    /// Accepts inputs such as <c>"S-101"</c>, <c>"s101"</c>, <c>"S101"</c>,
    /// and the long-form product identifier <c>"INT.IHO.S-101.1.2.0"</c>. A
    /// non-S-100 format name (<c>"grib2"</c>, <c>"netcdf"</c>, <c>"Cap"</c>) is
    /// returned in its canonical casing (<see cref="NonS100Formats"/>).
    /// </summary>
    /// <exception cref="FormatException">The input does not match any recognised form.</exception>
    public static string Normalize(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new FormatException("Product specification name is empty.");
        }

        if (TryGetNonS100Format(trimmed, out var format))
        {
            return format;
        }

        var m = ShortNameRegex().Match(trimmed);
        if (m.Success)
        {
            return $"S-{m.Groups["digits"].Value}";
        }

        m = ProductIdentifierRegex().Match(trimmed);
        if (m.Success)
        {
            return $"S-{m.Groups["digits"].Value}";
        }

        throw new FormatException($"'{name}' is not a recognised S-100 product specification or format name.");
    }

    /// <summary>
    /// Attempts to normalise a product specification short name. Returns
    /// <c>false</c> when <paramref name="name"/> does not match a recognised
    /// form.
    /// </summary>
    public static bool TryNormalize(string? name, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(name)) return false;

        try
        {
            canonical = Normalize(name);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
