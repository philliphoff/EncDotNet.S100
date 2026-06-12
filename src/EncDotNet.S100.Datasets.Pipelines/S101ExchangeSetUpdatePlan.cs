using EncDotNet.S100.ExchangeSets;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Kind of a single <see cref="S101ExchangeSetLoadItem"/> produced when planning
/// how to load S-101 cells (and their sequential updates) from an exchange set.
/// </summary>
public enum S101LoadItemKind
{
    /// <summary>A dataset loaded individually (non-S-101, or an S-101 base with no in-set updates).</summary>
    Single = 0,

    /// <summary>An S-101 base cell with one or more in-set update files to apply.</summary>
    BaseWithUpdates = 1,

    /// <summary>An S-101 update file with no matching base cell in the same exchange set.</summary>
    OrphanUpdate = 2,
}

/// <summary>
/// One planned load operation: either a single dataset, an S-101 base cell with
/// its ordered in-set updates, or an orphan update with no base.
/// </summary>
/// <param name="Kind">The item kind.</param>
/// <param name="Base">The base (or single) dataset's discovery metadata.</param>
/// <param name="Updates">
/// Ordered (ascending update number) update metadata for
/// <see cref="S101LoadItemKind.BaseWithUpdates"/>; empty otherwise.
/// </param>
public sealed record S101ExchangeSetLoadItem(
    S101LoadItemKind Kind,
    DatasetDiscoveryMetadata Base,
    IReadOnlyList<DatasetDiscoveryMetadata> Updates);

/// <summary>
/// Groups an exchange set's catalogue entries so that each S-101 base cell is
/// loaded together with the sequential update files (<c>….001</c>, <c>….002</c>,
/// …) that target the same cell <b>within the same exchange set</b>.
/// </summary>
/// <remarks>
/// <para>
/// Updates are matched to their base by cell name (the dataset file name without
/// its numeric extension) and ordered by update number (from the catalogue's
/// <c>updateNumber</c>, falling back to the file extension). Non-S-101 datasets,
/// and S-101 base cells with no in-set updates, are emitted as
/// <see cref="S101LoadItemKind.Single"/> items in catalogue order; an S-101
/// update with no matching base is emitted as
/// <see cref="S101LoadItemKind.OrphanUpdate"/> (surfaced as a best-effort
/// warning by the loader). Cross-exchange-set application is intentionally not
/// supported here. S-101 / S-100 Part 10a.
/// </para>
/// </remarks>
public static class S101ExchangeSetUpdatePlan
{
    /// <summary>
    /// Builds the ordered load plan for <paramref name="datasets"/>.
    /// </summary>
    /// <param name="datasets">The exchange set catalogue's dataset discovery metadata, in catalogue order.</param>
    /// <returns>One <see cref="S101ExchangeSetLoadItem"/> per load operation, in catalogue order.</returns>
    public static IReadOnlyList<S101ExchangeSetLoadItem> Build(IReadOnlyList<DatasetDiscoveryMetadata> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);

        // Group S-101 entries by cell name (file stem, case-insensitive).
        var cellGroups = new Dictionary<string, List<DatasetDiscoveryMetadata>>(StringComparer.OrdinalIgnoreCase);
        foreach (var metadata in datasets)
        {
            if (!IsS101(metadata))
                continue;

            var cellName = GetCellName(metadata);
            if (!cellGroups.TryGetValue(cellName, out var members))
            {
                members = [];
                cellGroups[cellName] = members;
            }
            members.Add(metadata);
        }

        var emittedCells = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plan = new List<S101ExchangeSetLoadItem>(datasets.Count);

        foreach (var metadata in datasets)
        {
            if (!IsS101(metadata))
            {
                plan.Add(new S101ExchangeSetLoadItem(S101LoadItemKind.Single, metadata, []));
                continue;
            }

            var cellName = GetCellName(metadata);
            var members = cellGroups[cellName];
            var baseEntry = members.FirstOrDefault(m => GetUpdateNumber(m) == 0);

            if (baseEntry is null)
            {
                // No base in this set: every member is an orphan update. Emit each
                // at its own position so the warning maps to the right file.
                plan.Add(new S101ExchangeSetLoadItem(S101LoadItemKind.OrphanUpdate, metadata, []));
                continue;
            }

            // Emit the whole cell once, at the position of its base entry.
            if (!ReferenceEquals(metadata, baseEntry))
                continue;

            if (!emittedCells.Add(cellName))
                continue;

            var updates = members
                .Where(m => !ReferenceEquals(m, baseEntry) && GetUpdateNumber(m) > 0)
                .OrderBy(GetUpdateNumber)
                .ToList();

            plan.Add(updates.Count == 0
                ? new S101ExchangeSetLoadItem(S101LoadItemKind.Single, baseEntry, [])
                : new S101ExchangeSetLoadItem(S101LoadItemKind.BaseWithUpdates, baseEntry, updates));
        }

        return plan;
    }

    private static bool IsS101(DatasetDiscoveryMetadata metadata) =>
        DatasetPipelineFactory.MapProductSpecificationToSpec(metadata.ProductSpecification) == "S-101";

    private static string GetCellName(DatasetDiscoveryMetadata metadata) =>
        Path.GetFileNameWithoutExtension(metadata.FileName);

    /// <summary>
    /// Resolves the update number: the catalogue's <c>updateNumber</c> when
    /// present, otherwise the numeric file extension (<c>.000</c> → 0).
    /// </summary>
    private static int GetUpdateNumber(DatasetDiscoveryMetadata metadata)
    {
        if (metadata.UpdateNumber is { } declared)
            return declared;

        var ext = Path.GetExtension(metadata.FileName);
        if (ext.Length == 4 &&
            int.TryParse(ext.AsSpan(1), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var fromExt))
        {
            return fromExt;
        }

        return 0;
    }
}
