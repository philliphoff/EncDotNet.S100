using System.Text.RegularExpressions;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The WMTS layers <c>tiles serve</c> groups its datasets into (issue #1495),
/// besides the one layer of every dataset (<see cref="WmtsService.LayerIdentifier"/>).
/// </summary>
/// <remarks>
/// <para>
/// With <c>--layers-by product</c>, each dataset is in the layer of its
/// product: <c>chart</c> for S-57, S-101 and S-102, which draw together as a
/// chart, and the product without its hyphen (<c>S104</c>, <c>S111</c>,
/// <c>S124</c>, …) for the rest. With <c>--manage-datasets</c>, a client can
/// put a dataset in a layer it names when it adds the dataset, whatever
/// <c>--layers-by</c> says.
/// </para>
/// <para>
/// A layer draws only its datasets, with the same settings, zoom range and
/// tile grid as the layer of every dataset, and has its own time steps and
/// extent. Without <c>--layers-by</c> and without layers named by a client,
/// only the layer of every dataset is served, as before.
/// </para>
/// </remarks>
internal static partial class TileLayers
{
    /// <summary>The <c>--layers-by</c> value that serves no layers besides the one of every dataset.</summary>
    public const string None = "none";

    /// <summary>The <c>--layers-by</c> value that serves a layer per product.</summary>
    public const string Product = "product";

    /// <summary>The <c>--layers-by</c> values.</summary>
    public static readonly IReadOnlyList<string> Groupings = [None, Product];

    /// <summary>The layer of S-57, S-101 and S-102 datasets with <c>--layers-by product</c>.</summary>
    public const string Chart = "chart";

    /// <summary>The layer of a dataset of <paramref name="spec"/> with <c>--layers-by product</c>.</summary>
    public static string ForProduct(string spec) =>
        spec.ToUpperInvariant() is "S-57" or "S-101" or "S-102"
            ? Chart
            : spec.Replace("-", string.Empty, StringComparison.Ordinal).ToUpperInvariant();

    /// <summary>
    /// Whether <paramref name="id"/> can name a layer: 1–64 letters, digits,
    /// <c>_</c>, <c>-</c> and <c>.</c>, starting with a letter or digit, and
    /// not the layer of every dataset.
    /// </summary>
    public static bool IsValidId(string? id) =>
        id is not null
        && LayerId().IsMatch(id)
        && !string.Equals(id, WmtsService.LayerIdentifier, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The layer of each dataset, by input index: the one a client put it in
    /// (<paramref name="changes"/>), else its product's with <paramref name="grouping"/>
    /// <see cref="Product"/>, else <see langword="null"/> (only in the layer of every dataset).
    /// </summary>
    public static IReadOnlyList<string?> Assign(
        IReadOnlyList<string> paths, IReadOnlyList<string> specs, string? grouping, DatasetChanges? changes)
    {
        bool byProduct = string.Equals(grouping, Product, StringComparison.OrdinalIgnoreCase);
        var layers = new string?[paths.Count];
        for (int i = 0; i < layers.Length; i++)
        {
            layers[i] = (changes is { Layers.Count: > 0 } ? changes.LayerOf(paths[i]) : null)
                ?? (byProduct ? ForProduct(specs[i]) : null);
        }

        return layers;
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex LayerId();
}
