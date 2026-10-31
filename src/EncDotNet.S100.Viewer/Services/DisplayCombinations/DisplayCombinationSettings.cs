using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services.DisplayCombinations;

/// <summary>
/// A user display combination as <c>settings.json</c> keeps it
/// (<see cref="ViewerSettings.DisplayCombinations"/>). Id lists are
/// comma-separated, as <see cref="ViewerSettings.EcdisHiddenViewingGroups"/>
/// is, so the file stays editable by hand.
/// </summary>
internal sealed class DisplayCombinationSettings
{
    /// <summary>The combination's id.</summary>
    public string Id { get; set; } = "";

    /// <summary>The name the mariner gave it.</summary>
    public string Label { get; set; } = "";

    /// <summary>Product keys in the picture, comma-separated; empty for every product.</summary>
    public string Products { get; set; } = "";

    /// <summary>The display category's name, or <see langword="null"/> to leave it.</summary>
    public string? Category { get; set; }

    /// <summary>Hidden viewing-group ids, comma-separated, per portrayal spec.</summary>
    public Dictionary<string, string> HiddenViewingGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shown viewing-group ids, comma-separated, per portrayal spec.</summary>
    public Dictionary<string, string> ShownViewingGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Display-mode ids, per portrayal spec.</summary>
    public Dictionary<string, string> DisplayModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Map-wide optional-layer switches, by layer key.</summary>
    public Dictionary<string, bool> LayerVisibility { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Sub-layer visibility, by sub-layer key.</summary>
    public Dictionary<string, bool> SubLayerVisibility { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Creates the settings record for a user combination.</summary>
    public static DisplayCombinationSettings From(DisplayCombination combination)
    {
        ArgumentNullException.ThrowIfNull(combination);
        return new DisplayCombinationSettings
        {
            Id = combination.Id,
            Label = combination.Label,
            Products = string.Join(",", combination.Products.Order(DisplayCombinationProducts.Order)),
            Category = combination.Category?.ToString(),
            HiddenViewingGroups = Join(combination.HiddenViewingGroups),
            ShownViewingGroups = Join(combination.ShownViewingGroups),
            DisplayModes = new(combination.DisplayModes, StringComparer.OrdinalIgnoreCase),
            LayerVisibility = new(combination.LayerVisibility, StringComparer.Ordinal),
            SubLayerVisibility = new(combination.SubLayerVisibility, StringComparer.Ordinal),
        };
    }

    /// <summary>
    /// The user combination this record describes, or <see langword="null"/>
    /// when it has no id or name (a hand-edited file).
    /// </summary>
    public DisplayCombination? ToCombination()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Label))
            return null;
        EcdisDisplayCategory? category = Enum.TryParse<EcdisDisplayCategory>(Category, ignoreCase: true, out var c) ? c : null;
        return DisplayCombination.Create(Id.Trim(), Label.Trim(), DisplayCombinationOrigin.User) with
        {
            Products = new HashSet<string>(Split(Products), StringComparer.OrdinalIgnoreCase),
            Category = category,
            HiddenViewingGroups = Parse(HiddenViewingGroups),
            ShownViewingGroups = Parse(ShownViewingGroups),
            DisplayModes = (DisplayModes ?? [])
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            LayerVisibility = new Dictionary<string, bool>(LayerVisibility ?? [], StringComparer.Ordinal),
            SubLayerVisibility = new Dictionary<string, bool>(SubLayerVisibility ?? [], StringComparer.Ordinal),
        };
    }

    private static Dictionary<string, string> Join(IReadOnlyDictionary<string, IReadOnlySet<int>> groups)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (spec, ids) in groups)
        {
            if (ids.Count > 0)
                result[spec] = string.Join(",", ids.Order().Select(i => i.ToString(CultureInfo.InvariantCulture)));
        }
        return result;
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<int>> Parse(Dictionary<string, string>? groups)
    {
        var result = new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (spec, value) in groups ?? [])
        {
            var ids = new HashSet<int>();
            foreach (var token in Split(value))
            {
                if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                    ids.Add(id);
            }
            if (ids.Count > 0)
                result[spec] = ids;
        }
        return result;
    }

    private static string[] Split(string? value) =>
        (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
