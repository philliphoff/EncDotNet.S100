using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services.DisplayCombinations;

/// <summary>
/// Maps a dataset to the product key a <see cref="DisplayCombination"/> names
/// it by (design note §1.2): S-57 and S-401 cells are the chart product
/// <c>"S-101"</c>, GRIB2 and NetCDF weather is <c>"Weather"</c>, and every
/// other product is its own specification.
/// </summary>
internal static class DisplayCombinationProducts
{
    /// <summary>The chart product's key.</summary>
    public const string Chart = "S-101";

    /// <summary>The weather pseudo-product's key.</summary>
    public const string Weather = "Weather";

    /// <summary>
    /// The display order of product keys in chips and summaries: the chart,
    /// then the S-100 products by number, then weather and anything else.
    /// </summary>
    public static IComparer<string> Order { get; } = Comparer<string>.Create(static (a, b) =>
    {
        var rank = Rank(a).CompareTo(Rank(b));
        return rank != 0 ? rank : StringComparer.OrdinalIgnoreCase.Compare(a, b);
    });

    private static int Rank(string key) =>
        string.Equals(key, Weather, StringComparison.OrdinalIgnoreCase) ? 2
        : key.StartsWith("S-", StringComparison.OrdinalIgnoreCase) ? 0
        : 1;

    /// <summary>The product key of <paramref name="entry"/>.</summary>
    public static string KeyOf(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return KeyOf(entry.ProductSpec, entry.PortrayalSpec);
    }

    /// <summary>
    /// The product key of a dataset of <paramref name="productSpec"/> that is
    /// portrayed with <paramref name="portrayalSpec"/>'s catalogues.
    /// </summary>
    public static string KeyOf(string productSpec, string? portrayalSpec)
    {
        ArgumentNullException.ThrowIfNull(productSpec);
        if (IsChart(portrayalSpec) || IsChart(productSpec) || string.Equals(productSpec, "S-57", StringComparison.OrdinalIgnoreCase))
            return Chart;
        if (string.Equals(productSpec, WeatherProducts.Grib2.Spec, StringComparison.OrdinalIgnoreCase)
            || string.Equals(productSpec, WeatherProducts.NetCdf.Spec, StringComparison.OrdinalIgnoreCase))
            return Weather;
        return productSpec;
    }

    private static bool IsChart(string? spec) =>
        string.Equals(spec, "S-101", StringComparison.OrdinalIgnoreCase)
        || string.Equals(spec, "S-401", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every product key the viewer can load, from the product registry:
    /// what "every product" means for a combination that lists its products.
    /// </summary>
    public static IReadOnlySet<string> All()
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in ProductRegistry.Default.Registrations)
            keys.Add(KeyOf(registration.Spec, ProductRegistry.Default.PortrayalSpecName(registration.Spec)));
        return keys;
    }

    /// <summary>
    /// The product a sub-layer key belongs to (<c>"s111.arrows"</c> is
    /// <c>"S-111"</c>, <c>"weather.wind.speed"</c> is <c>"Weather"</c>), or
    /// the key itself when it has no recognisable prefix.
    /// </summary>
    public static string OfLayerKey(string layerKey)
    {
        ArgumentNullException.ThrowIfNull(layerKey);
        var dot = layerKey.IndexOf('.', StringComparison.Ordinal);
        var prefix = dot < 0 ? layerKey : layerKey[..dot];
        if (string.Equals(prefix, "weather", StringComparison.OrdinalIgnoreCase))
            return Weather;
        if (prefix.Length > 1 && (prefix[0] is 's' or 'S') && prefix[1..].All(char.IsAsciiDigit))
            return "S-" + prefix[1..];
        return layerKey;
    }
}
