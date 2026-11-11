using EncDotNet.S100.Datasets.Pipelines;

namespace EncDotNet.S100.Viewer.Services.DisplayCombinations;

/// <summary>One loaded dataset as modified detection sees it.</summary>
/// <param name="Id">The dataset id.</param>
/// <param name="Product">Its <see cref="DisplayCombinationProducts"/> key.</param>
/// <param name="Active">Whether it is active for S-98 composition.</param>
/// <param name="SubLayers">Its sub-layers' visibility, by sub-layer key.</param>
internal sealed record DisplayPictureDataset(
    string Id,
    string Product,
    bool Active,
    IReadOnlyDictionary<string, bool> SubLayers);

/// <summary>What the map shows, as far as a combination can describe it.</summary>
/// <param name="Display">The ECDIS display state.</param>
/// <param name="Datasets">The loaded datasets.</param>
internal sealed record DisplayPictureSnapshot(
    EcdisDisplaySettings Display,
    IReadOnlyList<DisplayPictureDataset> Datasets);

/// <summary>What kind of control differs from a combination.</summary>
internal enum DisplayCombinationChangeKind
{
    /// <summary>The display category.</summary>
    Category,

    /// <summary>A product the combination leaves out has an active dataset.</summary>
    ProductMadeActive,

    /// <summary>A product the combination includes has no active dataset.</summary>
    ProductMadeInactive,

    /// <summary>A viewing group the combination sets.</summary>
    ViewingGroups,

    /// <summary>A display mode the combination sets.</summary>
    DisplayMode,

    /// <summary>A map-wide optional layer the combination sets.</summary>
    Layer,

    /// <summary>A sub-layer the combination sets, on some dataset.</summary>
    SubLayer,
}

/// <summary>One control that differs from the active combination.</summary>
/// <param name="Kind">What differs.</param>
/// <param name="Subject">The product, layer or sub-layer key it differs for; <see langword="null"/> for the category.</param>
/// <param name="Expected">The combination's value, for the category.</param>
/// <param name="Actual">The current value, for the category.</param>
internal sealed record DisplayCombinationChange(
    DisplayCombinationChangeKind Kind,
    string? Subject,
    EcdisDisplayCategory? Expected = null,
    EcdisDisplayCategory? Actual = null);

/// <summary>
/// Compares a <see cref="DisplayCombination"/> with what the map shows
/// (design note §3.4). Pure: it reads snapshots only, so it's tested without
/// the UI. Only controls the combination covers count; per-dataset visibility
/// (the Datasets panel eye) never does.
/// </summary>
internal static class DisplayCombinationComparison
{
    /// <summary>
    /// The controls that differ from <paramref name="combination"/>, in a
    /// stable order; empty when the picture matches it.
    /// </summary>
    public static IReadOnlyList<DisplayCombinationChange> Compare(
        DisplayCombination combination,
        DisplayPictureSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(combination);
        ArgumentNullException.ThrowIfNull(snapshot);
        var changes = new List<DisplayCombinationChange>();
        var display = snapshot.Display;

        if (combination.Category is { } category && display.Category != category)
            changes.Add(new(DisplayCombinationChangeKind.Category, null, category, display.Category));

        foreach (var group in snapshot.Datasets
            .GroupBy(d => d.Product, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, DisplayCombinationProducts.Order))
        {
            var anyActive = group.Any(d => d.Active);
            if (combination.Includes(group.Key))
            {
                if (!anyActive)
                    changes.Add(new(DisplayCombinationChangeKind.ProductMadeInactive, group.Key));
            }
            else if (anyActive)
            {
                changes.Add(new(DisplayCombinationChangeKind.ProductMadeActive, group.Key));
            }
        }

        var specs = combination.HiddenViewingGroups.Keys
            .Concat(combination.ShownViewingGroups.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, DisplayCombinationProducts.Order);
        foreach (var spec in specs)
        {
            display.HiddenViewingGroups.TryGetValue(spec, out var hidden);
            hidden ??= new HashSet<int>();
            var hiddenOk = !combination.HiddenViewingGroups.TryGetValue(spec, out var mustHide)
                || mustHide.All(hidden.Contains);
            var shownOk = !combination.ShownViewingGroups.TryGetValue(spec, out var mustShow)
                || !mustShow.Any(hidden.Contains);
            if (!hiddenOk || !shownOk)
                changes.Add(new(DisplayCombinationChangeKind.ViewingGroups, spec));
        }

        foreach (var (spec, mode) in combination.DisplayModes.OrderBy(kv => kv.Key, DisplayCombinationProducts.Order))
        {
            display.ActiveDisplayModes.TryGetValue(spec, out var actual);
            if (!string.Equals(actual, mode, StringComparison.Ordinal))
                changes.Add(new(DisplayCombinationChangeKind.DisplayMode, spec));
        }

        foreach (var (key, visible) in combination.LayerVisibility.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            // An unset switch follows its layer's default, which the
            // combination can't know here; only an explicit switch the other
            // way counts.
            if (display.LayerVisibility.TryGetValue(key, out var actual) && actual != visible)
                changes.Add(new(DisplayCombinationChangeKind.Layer, key));
        }

        foreach (var (key, visible) in combination.SubLayerVisibility.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (snapshot.Datasets.Any(d => d.SubLayers.TryGetValue(key, out var actual) && actual != visible))
                changes.Add(new(DisplayCombinationChangeKind.SubLayer, key));
        }

        return changes;
    }
}
