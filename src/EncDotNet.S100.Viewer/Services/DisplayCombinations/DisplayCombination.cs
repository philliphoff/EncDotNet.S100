using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services.DisplayCombinations;

/// <summary>Where a <see cref="DisplayCombination"/> comes from.</summary>
internal enum DisplayCombinationOrigin
{
    /// <summary>Ships with SoundCharts (<see cref="BuiltInDisplayCombinations"/>); can't be edited or deleted.</summary>
    BuiltIn,

    /// <summary>Saved by the mariner with <b>Save current view as…</b>; kept in <see cref="ViewerSettings.DisplayCombinations"/>.</summary>
    User,

    /// <summary>
    /// An S-98 Interoperability Catalogue <c>S100_IC_PredefinedCombination</c>.
    /// Reserved: there is no catalogue loader yet (design note §3.5).
    /// </summary>
    InteroperabilityCatalogue,
}

/// <summary>
/// A named, product-level description of what the map shows (design note
/// <c>docs/design/predefined-display-combinations.md</c> §1.1): which products
/// are in the picture, the display category, viewing groups, display modes,
/// map-wide optional layers and sub-layers. It never describes mariner safety
/// settings, the Navigational warnings switch, display planes, the palette,
/// dynamic sources or routes.
/// </summary>
/// <param name="Id">Stable id: a built-in's fixed id (<c>"pilotage"</c>) or a user combination's generated one.</param>
/// <param name="Label">The name shown in the View menu.</param>
/// <param name="Origin">Where the combination comes from.</param>
/// <param name="Products">
/// The products in the picture, as <see cref="DisplayCombinationProducts.KeyOf(string, string)"/>
/// keys (<c>"S-101"</c>, <c>"S-102"</c>, …, <c>"Weather"</c>). Empty means every product.
/// </param>
/// <param name="Category">The display category, or <see langword="null"/> to leave it.</param>
/// <param name="HiddenViewingGroups">Viewing groups the combination hides, per portrayal spec.</param>
/// <param name="ShownViewingGroups">Viewing groups the combination shows, per portrayal spec.</param>
/// <param name="DisplayModes">S-100 Part 9 §11.7 display-mode ids, per portrayal spec.</param>
/// <param name="LayerVisibility">Map-wide optional-layer switches (<see cref="EcdisDisplaySettings.LayerVisibility"/>).</param>
/// <param name="SubLayerVisibility">Sub-layer visibility by sub-layer key (<c>"s111.arrows"</c>), for every dataset that has it.</param>
internal sealed record DisplayCombination(
    string Id,
    string Label,
    DisplayCombinationOrigin Origin,
    IReadOnlySet<string> Products,
    EcdisDisplayCategory? Category,
    IReadOnlyDictionary<string, IReadOnlySet<int>> HiddenViewingGroups,
    IReadOnlyDictionary<string, IReadOnlySet<int>> ShownViewingGroups,
    IReadOnlyDictionary<string, string> DisplayModes,
    IReadOnlyDictionary<string, bool> LayerVisibility,
    IReadOnlyDictionary<string, bool> SubLayerVisibility)
{
    /// <summary>A one-line note on what the combination adds, or <see langword="null"/>.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The combination's step on the <b>Build up the picture</b> ladder
    /// (1, 2, 3), or <see langword="null"/> when it isn't on it.
    /// </summary>
    public int? LadderStep { get; init; }

    /// <summary>True when <see cref="Products"/> is empty: every product is in.</summary>
    public bool IncludesEveryProduct => Products.Count == 0;

    /// <summary>Whether the product with key <paramref name="productKey"/> is in the picture.</summary>
    public bool Includes(string productKey) =>
        IncludesEveryProduct || Products.Contains(productKey);

    /// <summary>Creates an empty combination to build on with <c>with</c>.</summary>
    public static DisplayCombination Create(string id, string label, DisplayCombinationOrigin origin) => new(
        id,
        label,
        origin,
        new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        null,
        EmptyGroups,
        EmptyGroups,
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, bool>(StringComparer.Ordinal),
        new Dictionary<string, bool>(StringComparer.Ordinal));

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<int>> EmptyGroups =
        new Dictionary<string, IReadOnlySet<int>>(StringComparer.OrdinalIgnoreCase);
}
