namespace EncDotNet.S100.Collections;

/// <summary>
/// The file-naming rules that identify exchange sets on disk: S-100
/// (<c>CATALOG.XML</c>) and S-57 (<c>CATALOG.031</c>) catalogues. Shared by
/// collection indexing and hosts that open exchange sets directly, so
/// detection has one owner.
/// </summary>
public static class ExchangeSetLayout
{
    /// <summary>
    /// Accepted S-100 exchange-catalogue file names, preferred first. S-100
    /// Part 17 names the file <c>CATALOG.XML</c>; some producers write
    /// <c>CATALOGUE.XML</c>.
    /// </summary>
    public static IReadOnlyList<string> S100CatalogueNames { get; } = ["CATALOG.XML", "CATALOGUE.XML"];

    /// <summary>The S-57 exchange-set catalogue file name (S-57 Part 3 §B.1).</summary>
    public const string S57CatalogueName = "CATALOG.031";

    /// <summary>True when <paramref name="fileName"/> is an S-100 exchange-catalogue name (case-insensitive).</summary>
    public static bool IsS100CatalogueName(string? fileName) =>
        fileName is not null
        && S100CatalogueNames.Any(n => string.Equals(n, fileName, StringComparison.OrdinalIgnoreCase));

    /// <summary>True when <paramref name="fileName"/> is the S-57 catalogue name (case-insensitive).</summary>
    public static bool IsS57CatalogueName(string? fileName) =>
        string.Equals(fileName, S57CatalogueName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Picks the S-100 catalogue among <paramref name="fileNames"/>, preferring
    /// <c>CATALOG.XML</c>, or returns <see langword="null"/>.
    /// </summary>
    public static string? PickS100Catalogue(IEnumerable<string?> fileNames)
    {
        ArgumentNullException.ThrowIfNull(fileNames);

        string? fallback = null;
        foreach (var name in fileNames)
        {
            if (string.Equals(name, S100CatalogueNames[0], StringComparison.OrdinalIgnoreCase))
                return name;
            if (fallback is null && IsS100CatalogueName(name))
                fallback = name;
        }

        return fallback;
    }
}
