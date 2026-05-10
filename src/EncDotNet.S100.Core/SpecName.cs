using System.Text.RegularExpressions;

namespace EncDotNet.S100.Core;

/// <summary>
/// Helpers for normalising S-100 product specification short names into the
/// canonical <c>"S-NNN"</c> form (e.g. <c>"S-101"</c>, <c>"S-102"</c>).
/// </summary>
public static partial class SpecName
{
    [GeneratedRegex(@"^S-?(?<digits>\d{2,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortNameRegex();

    [GeneratedRegex(@"^INT\.IHO\.S-?(?<digits>\d{2,3})(?:\.|$)", RegexOptions.IgnoreCase)]
    private static partial Regex ProductIdentifierRegex();

    /// <summary>
    /// Normalises a product specification short name into <c>"S-NNN"</c> form.
    /// Accepts inputs such as <c>"S-101"</c>, <c>"s101"</c>, <c>"S101"</c>,
    /// and the long-form product identifier <c>"INT.IHO.S-101.1.2.0"</c>.
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

        throw new FormatException($"'{name}' is not a recognised S-100 product specification name.");
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
