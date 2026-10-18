using System.Globalization;

namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Names the languages S-100 datasets write: ISO 639-2 three-letter codes
/// (<c>"eng"</c>, <c>"fra"</c>, or the bibliographic <c>"fre"</c>), and
/// two-letter or BCP 47 tags (<c>"en"</c>, <c>"de-CH"</c>).
/// </summary>
internal static class LanguageNames
{
    // ISO 639-2 bibliographic codes .NET does not know, mapped to the
    // terminology codes it uses.
    private static readonly Dictionary<string, string> Bibliographic = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fre"] = "fra",
        ["ger"] = "deu",
        ["dut"] = "nld",
        ["chi"] = "zho",
        ["cze"] = "ces",
        ["gre"] = "ell",
        ["ice"] = "isl",
        ["per"] = "fas",
        ["rum"] = "ron",
        ["slo"] = "slk",
        ["wel"] = "cym",
        ["alb"] = "sqi",
        ["arm"] = "hye",
        ["baq"] = "eus",
        ["bur"] = "mya",
        ["geo"] = "kat",
        ["mac"] = "mkd",
        ["mao"] = "mri",
        ["may"] = "msa",
        ["tib"] = "bod",
    };

    private static readonly Lazy<Dictionary<string, CultureInfo>> ByThreeLetter = new(() =>
    {
        var map = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var culture in CultureInfo.GetCultures(CultureTypes.NeutralCultures))
        {
            if (culture.ThreeLetterISOLanguageName is { Length: 3 } code)
                map.TryAdd(code, culture);
        }
        return map;
    });

    /// <summary>
    /// The culture <paramref name="code"/> names, or <c>null</c> when it is
    /// empty or unknown.
    /// </summary>
    public static CultureInfo? Find(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;
        code = code.Trim();
        if (code.Length == 3 && !code.Contains('-', StringComparison.Ordinal))
        {
            var terminology = Bibliographic.GetValueOrDefault(code, code);
            return ByThreeLetter.Value.GetValueOrDefault(terminology);
        }

        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            // An unknown tag yields a culture whose name is the tag itself
            // and whose language is not a real one.
            return culture.ThreeLetterISOLanguageName is { Length: 3 } && !string.IsNullOrEmpty(culture.EnglishName)
                && !culture.EnglishName.StartsWith("Unknown", StringComparison.Ordinal)
                    ? culture
                    : null;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The language's own name, capitalised (e.g. "Français"); the code as
    /// written when it is unknown; "—" when there is none.
    /// </summary>
    public static string Display(string? code)
    {
        if (Find(code) is not { } culture)
            return string.IsNullOrWhiteSpace(code) ? "—" : code.Trim();
        var name = culture.NativeName;
        return name.Length == 0 ? code!.Trim() : char.ToUpper(name[0], culture) + name[1..];
    }

    /// <summary>Whether <paramref name="code"/> names the language of <paramref name="culture"/>.</summary>
    public static bool Matches(string? code, CultureInfo culture) =>
        Find(code) is { } found
        && string.Equals(found.TwoLetterISOLanguageName, culture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase);
}
