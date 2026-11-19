using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Cli.Web;

/// <summary>
/// What the Datasets panel (issue #1578) works out about each dataset: its
/// navigational purpose, the text of its row and details, and whether it has
/// a place on the map (handoff D7, D8, D10, D33).
/// </summary>
internal static partial class ServeDatasetInfo
{
    /// <summary>The navigational purposes, 1 Overview to 6 Berthing, in order.</summary>
    public static IReadOnlyList<string> Purposes { get; } = ["Overview", "General", "Coastal", "Approach", "Harbour", "Berthing"];

    /// <summary>The group of datasets with no purpose band.</summary>
    public const string OtherPurpose = "Other";

    /// <summary>Whether <paramref name="product"/> is a chart grouped by navigational purpose.</summary>
    public static bool HasPurpose(string product) => IsS57(product) || IsS101(product);

    /// <summary>
    /// The navigational purpose (1 Overview … 6 Berthing): an S-57 cell's from
    /// the third character of its name (S-57 Appendix B.1); an S-101 cell's
    /// from the maximum display scale of its DataCoverage, in the S-57
    /// compilation-scale bands (<see cref="Band"/>). <see langword="null"/>
    /// when there's none, or for other products.
    /// </summary>
    public static int? Purpose(ServedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (IsS57(dataset.Product))
            return S57Name().Match(dataset.File) is { Success: true } name ? name.Groups["purpose"].Value[0] - '0' : null;
        if (IsS101(dataset.Product))
            return dataset.MaximumDisplayScale is int scale and > 0 ? Band(scale) : null;
        return null;
    }

    /// <summary>
    /// The purpose band of a compilation scale <c>1:<paramref name="denominator"/></c>,
    /// as the S-57 bands (IHO S-65 Annex A) have it: Overview smaller than
    /// 1:1 500 000, General to 1:350 000, Coastal to 1:90 000, Approach to
    /// 1:22 000, Harbour to 1:4 000, Berthing larger.
    /// </summary>
    public static int Band(int denominator) => denominator switch
    {
        >= 1_500_000 => 1,
        >= 350_000 => 2,
        >= 90_000 => 3,
        >= 22_000 => 4,
        >= 4_000 => 5,
        _ => 6,
    };

    /// <summary>The name of a purpose, or "Other" for none.</summary>
    public static string PurposeName(int? purpose) =>
        purpose is >= 1 and <= 6 ? Purposes[purpose.Value - 1] : OtherPurpose;

    /// <summary>The purpose a dataset is grouped and filtered by: a name, "Other", or <see langword="null"/> for a product without purposes.</summary>
    public static string? PurposeGroup(ServedDataset dataset) =>
        HasPurpose(dataset.Product) ? PurposeName(Purpose(dataset)) : null;

    /// <summary>Whether the dataset has a place on the map: a coverage or bounds to outline and frame (D33).</summary>
    public static bool HasPlace(ServedDataset dataset) => dataset.Coverage is not null || dataset.Bounds is { Length: 4 };

    /// <summary>Whether the dataset is a problem the Problems filter shows: for now, one with no coverage.</summary>
    public static bool IsProblem(ServedDataset dataset) => !HasPlace(dataset);

    /// <summary>"Ed 9 · Upd 2", "Ed 9" for a base cell; <see langword="null"/> when the dataset has no edition of its own.</summary>
    public static string? EditionShort(ServedDataset dataset) =>
        dataset.EditionNumber is int edition
            ? "Ed " + Number(edition) + (dataset.UpdateNumber is int update and > 0 ? " · Upd " + Number(update) : "")
            : null;

    /// <summary>The right of a row (D7): edition and producer, "Ed 9 · Upd 2 · US".</summary>
    public static string RowMeta(ServedDataset dataset) =>
        string.Join(" · ", new[] { EditionShort(dataset), dataset.Producer }.Where(s => !string.IsNullOrEmpty(s)));

    /// <summary>
    /// The third line of a time-varying dataset's row (D7): dynamicity, time
    /// range and step count, "Forecast · 1 Apr 2021 00:00 – 3 Apr 23:45 UTC · 288 steps";
    /// <see langword="null"/> without times.
    /// </summary>
    public static string? TimeLine(ServedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.Times.Count == 0)
            return null;
        var parts = new List<string>(3);
        if (!string.IsNullOrEmpty(dataset.Dynamicity))
            parts.Add(dataset.Dynamicity);
        parts.Add(TimeRange(dataset.Times));
        parts.Add(Steps(dataset.Times.Count));
        return string.Join(" · ", parts);
    }

    /// <summary>"1 Apr 2021 00:00 – 3 Apr 23:45 UTC", or one time.</summary>
    public static string TimeRange(IReadOnlyList<DateTime> times)
    {
        ArgumentNullException.ThrowIfNull(times);
        if (times.Count == 0)
            return "";
        var first = times[0];
        var last = times[^1];
        if (first == last)
            return Time(first, withYear: true) + " UTC";
        return Time(first, withYear: true) + " – " + Time(last, withYear: last.Year != first.Year) + " UTC";
    }

    /// <summary>"288 steps", "1 step".</summary>
    public static string Steps(int count) => count == 1 ? "1 step" : Number(count) + " steps";

    /// <summary>"Edition 4, update 1, 12 Mar 2025"; <see langword="null"/> when the dataset has no edition of its own.</summary>
    public static string? EditionLong(ServedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.EditionNumber is not int edition)
            return null;
        var text = "Edition " + Number(edition) + (dataset.UpdateNumber is int update and > 0 ? ", update " + Number(update) : "");
        return dataset.IssueDate is { } issued ? text + ", " + issued.ToString("d MMM yyyy", CultureInfo.InvariantCulture) : text;
    }

    /// <summary>"1:45 000 – 1:22 000" for an S-101 cell's display scale range; <see langword="null"/> without one.</summary>
    public static string? DisplayScale(ServedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return (dataset.MinimumDisplayScale, dataset.MaximumDisplayScale) switch
        {
            (int min, int max) => Scale(min) + " – " + Scale(max),
            (int min, null) => "From " + Scale(min),
            (null, int max) => "To " + Scale(max),
            _ => null,
        };
    }

    /// <summary>"1:45 000", thousands set apart by a no-break space.</summary>
    public static string Scale(int denominator) =>
        "1:" + denominator.ToString("#,0", CultureInfo.InvariantCulture).Replace(',', ' ');

    /// <summary>The bounds in degrees and minutes, latitudes then longitudes: ("50°42.9′N – 50°53.0′N", "001°27.4′W – 001°04.0′W").</summary>
    public static (string Latitudes, string Longitudes)? Bounds(ServedDataset dataset) =>
        dataset.Bounds is [var west, var south, var east, var north]
            ? (DegreesMinutes(south, 'N', 'S', 2) + " – " + DegreesMinutes(north, 'N', 'S', 2),
               DegreesMinutes(west, 'E', 'W', 3) + " – " + DegreesMinutes(east, 'E', 'W', 3))
            : null;

    /// <summary>The number of areas in the dataset's coverage polygon.</summary>
    public static int CoverageAreas(ServedDataset dataset) => dataset.Coverage switch
    {
        { } c when (string?)c["type"] == "MultiPolygon" && c["coordinates"] is JsonArray polygons => polygons.Count,
        { } c when (string?)c["type"] == "Polygon" => 1,
        _ => 0,
    };

    /// <summary>"Data coverage, 2 areas", "Bounds only", or <see langword="null"/> for none.</summary>
    public static string? CoverageText(ServedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (dataset.HasDataCoverage && dataset.Coverage is not null)
        {
            var areas = CoverageAreas(dataset);
            return "Data coverage, " + (areas == 1 ? "1 area" : Number(areas) + " areas");
        }

        return HasPlace(dataset) ? "Bounds only" : null;
    }

    /// <summary>Whether a search matches a dataset: its id or file name, ignoring case (D9).</summary>
    public static bool MatchesSearch(ServedDataset dataset, string search) =>
        search.Length == 0
        || dataset.Id.Contains(search, StringComparison.OrdinalIgnoreCase)
        || dataset.File.Contains(search, StringComparison.OrdinalIgnoreCase);

    /// <summary>"12", "1,234".</summary>
    public static string Number(int value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>A latitude (<paramref name="width"/> 2) or longitude (3) in degrees and minutes: 50°46.5′N.</summary>
    public static string DegreesMinutes(double value, char positive, char negative, int width)
    {
        var hemisphere = value < 0 ? negative : positive;
        var v = Math.Abs(value);
        var degrees = Math.Floor(v);
        var minutes = (v - degrees) * 60;
        if (minutes >= 59.95)
        {
            degrees += 1;
            minutes = 0;
        }

        var text = new StringBuilder();
        text.Append(((int)degrees).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0'));
        text.Append('°');
        text.Append(minutes.ToString("00.0", CultureInfo.InvariantCulture));
        text.Append('′');
        text.Append(hemisphere);
        return text.ToString();
    }

    private static string Time(DateTime time, bool withYear) =>
        time.ToString(withYear ? "d MMM yyyy HH:mm" : "d MMM HH:mm", CultureInfo.InvariantCulture);

    private static bool IsS57(string product) => string.Equals(product, "S-57", StringComparison.OrdinalIgnoreCase);

    private static bool IsS101(string product) => string.Equals(product, "S-101", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[A-Z0-9]{2}(?<purpose>[1-6])[A-Z0-9]{5}(\.[0-9]{3})?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex S57Name();
}

/// <summary>A group of the Datasets panel (D8): a product, and a purpose for charts, within a layer.</summary>
/// <param name="Key">Unique among the groups: layer, product and purpose.</param>
/// <param name="Layer">The layer, or <see langword="null"/> when there are no layers.</param>
/// <param name="Product">The product.</param>
/// <param name="Purpose">The purpose name, "Other", or <see langword="null"/> for a product without purposes.</param>
/// <param name="Datasets">The datasets, in input order.</param>
internal sealed record DatasetGroup(string Key, string? Layer, string Product, string? Purpose, IReadOnlyList<ServedDataset> Datasets)
{
    /// <summary>"S-57 · Coastal", or the product alone.</summary>
    public string Label => Purpose is null ? Product : Product + " · " + Purpose;
}

/// <summary>The search and filter chips of the Datasets panel (D9).</summary>
/// <param name="Search">The search text, over id and file name.</param>
/// <param name="Products">The products shown; empty for all.</param>
/// <param name="Purposes">The purposes shown ("Other" included); empty for all.</param>
/// <param name="Problems">Only the datasets with problems.</param>
internal sealed record DatasetFilter(string Search, IReadOnlySet<string> Products, IReadOnlySet<string> Purposes, bool Problems)
{
    /// <summary>No search and no filter.</summary>
    public static DatasetFilter None { get; } = new("", new HashSet<string>(), new HashSet<string>(), false);

    /// <summary>Whether anything narrows the list, so it's flat with a count and only the matches are outlined.</summary>
    public bool IsActive => Search.Trim().Length > 0 || Products.Count > 0 || Purposes.Count > 0 || Problems;

    /// <summary>Whether <paramref name="dataset"/> passes the search and every filter.</summary>
    public bool Matches(ServedDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        if (!ServeDatasetInfo.MatchesSearch(dataset, Search.Trim()))
            return false;
        if (Products.Count > 0 && !Products.Contains(dataset.Product))
            return false;
        if (Purposes.Count > 0 && (ServeDatasetInfo.PurposeGroup(dataset) is not { } purpose || !Purposes.Contains(purpose)))
            return false;
        return !Problems || ServeDatasetInfo.IsProblem(dataset);
    }
}

/// <summary>How the Datasets panel groups and narrows its list (D8, D9, D11).</summary>
internal static class ServeDatasetList
{
    /// <summary>Above this many datasets, the groups start collapsed except the first (D8).</summary>
    public const int CollapseAbove = 30;

    /// <summary>Above this many rows, the list renders only the visible ones (D11).</summary>
    public const int VirtualizeAbove = 200;

    /// <summary>
    /// The datasets grouped by layer (in <paramref name="layers"/> order, then
    /// those in none), then product, then navigational purpose (Overview …
    /// Berthing, then Other). Empty groups are left out.
    /// </summary>
    public static IReadOnlyList<DatasetGroup> Group(IReadOnlyList<ServedDataset> datasets, IReadOnlyList<string> layers)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(layers);
        int LayerOrder(string? layer)
        {
            if (layer is null)
                return int.MaxValue;
            var index = IndexOf(layers, layer);
            return index < 0 ? int.MaxValue - 1 : index;
        }

        static int PurposeOrder(string? purpose) =>
            purpose is null ? 0 : IndexOf(ServeDatasetInfo.Purposes, purpose) is var i and >= 0 ? i + 1 : ServeDatasetInfo.Purposes.Count + 1;

        return [.. datasets
            .GroupBy(d => (Layer: layers.Count > 0 ? d.Layer : null, d.Product, Purpose: ServeDatasetInfo.PurposeGroup(d)))
            .OrderBy(g => LayerOrder(g.Key.Layer))
            .ThenBy(g => g.Key.Layer, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Product, StringComparer.Ordinal)
            .ThenBy(g => PurposeOrder(g.Key.Purpose))
            .Select(g => new DatasetGroup(
                g.Key.Layer + "|" + g.Key.Product + "|" + g.Key.Purpose,
                g.Key.Layer,
                g.Key.Product,
                g.Key.Purpose,
                [.. g]))];
    }

    /// <summary>The groups open when the panel opens: all of them, or above <see cref="CollapseAbove"/> datasets only the first (D8).</summary>
    public static IReadOnlySet<string> InitiallyExpanded(IReadOnlyList<DatasetGroup> groups, int datasetCount)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return datasetCount > CollapseAbove
            ? groups.Take(1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal)
            : groups.Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The list's rows: a layer heading when there are several layers, each
    /// group's header, and the datasets of the open groups; or, while
    /// searching or filtering, the matches alone (D8, D9).
    /// </summary>
    public static List<DatasetListItem> Items(
        IReadOnlyList<ServedDataset> datasets,
        IReadOnlyList<DatasetGroup> groups,
        IReadOnlySet<string> expanded,
        DatasetFilter filter,
        bool layerHeadings)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(expanded);
        ArgumentNullException.ThrowIfNull(filter);
        if (filter.IsActive)
            return [.. datasets.Where(filter.Matches).Select(d => new DatasetListItem(d, null, null))];

        var items = new List<DatasetListItem>();
        string? layer = null;
        foreach (var group in groups)
        {
            if (layerHeadings && group.Layer != layer)
            {
                layer = group.Layer;
                items.Add(new DatasetListItem(null, null, layer ?? "No layer"));
            }

            items.Add(new DatasetListItem(null, group, null));
            if (expanded.Contains(group.Key))
                items.AddRange(group.Datasets.Select(d => new DatasetListItem(d, null, null)));
        }

        return items;
    }

    private static int IndexOf(IReadOnlyList<string> list, string value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i], value, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }
}

/// <summary>A row of the Datasets panel's list: a dataset, a group header, or a layer heading.</summary>
/// <param name="Dataset">The dataset, for a dataset row.</param>
/// <param name="Group">The group, for a group header.</param>
/// <param name="LayerHeading">The layer, for a layer heading.</param>
internal sealed record DatasetListItem(ServedDataset? Dataset, DatasetGroup? Group, string? LayerHeading);
