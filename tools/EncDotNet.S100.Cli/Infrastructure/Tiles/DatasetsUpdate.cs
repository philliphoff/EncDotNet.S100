namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>What happened to a dataset served by <c>tiles serve</c> (issue #1583).</summary>
internal enum DatasetChangeKind
{
    /// <summary>Its files changed and it's being opened again.</summary>
    Reloading,

    /// <summary>It's served now and wasn't before.</summary>
    Added,

    /// <summary>It was served and isn't now.</summary>
    Removed,

    /// <summary>It was opened again after its files changed.</summary>
    Reloaded,

    /// <summary>Opening it again failed; the previous version still serves.</summary>
    Failed,
}

/// <summary>A change to one dataset.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Id">The dataset's id in the <c>/datasets</c> document; <see langword="null"/> for a failure no dataset can be blamed for.</param>
/// <param name="File">The dataset's file name; <see langword="null"/> with <paramref name="Id"/>.</param>
/// <param name="Reason">Why it failed, and what serves now, for <see cref="DatasetChangeKind.Failed"/>: a sentence or two for a person.</param>
internal sealed record DatasetChange(DatasetChangeKind Kind, string? Id, string? File, string? Reason = null);

/// <summary>
/// The datasets served are changing or have changed (issue #1583): pushed by
/// <see cref="RefreshingTileSource"/> when a reload starts and ends, and when
/// a dataset is added or removed with <c>--manage-datasets</c>.
/// </summary>
/// <param name="InProgress">Whether the datasets are being opened again now; <see langword="false"/> once they serve (or failed to).</param>
/// <param name="Revision">The revision served after the update.</param>
/// <param name="Changes">The datasets changed, or changing.</param>
internal sealed record DatasetsUpdate(bool InProgress, string? Revision, IReadOnlyList<DatasetChange> Changes)
{
    /// <summary>
    /// A reload starting: the datasets of <paramref name="session"/> whose
    /// files have changed since it opened them, as <see cref="DatasetChangeKind.Reloading"/>;
    /// none while nothing is served (<paramref name="session"/> <see langword="null"/>).
    /// </summary>
    public static DatasetsUpdate Reloading(TileRenderSession? session, string? revision)
    {
        if (session is null)
            return new DatasetsUpdate(true, revision, []);
        var ids = DatasetsDocument.Ids(session.Names);
        var changes = new List<DatasetChange>();
        for (int i = 0; i < session.Paths.Count; i++)
        {
            if (TileRenderSession.DatasetFingerprint(session.Paths[i]) != session.DatasetFingerprints[i])
                changes.Add(new DatasetChange(DatasetChangeKind.Reloading, ids[i], session.Names[i]));
        }

        return new DatasetsUpdate(true, revision, changes);
    }

    /// <summary>
    /// A reload that failed: each dataset that was reloading, or one change
    /// for the set, with <paramref name="reason"/>. The previous version still serves.
    /// </summary>
    public static DatasetsUpdate Failed(DatasetsUpdate reloading, string? revision, string reason)
    {
        ArgumentNullException.ThrowIfNull(reloading);
        ArgumentNullException.ThrowIfNull(reason);
        reason = reason.Trim().TrimEnd('.') + ". The previous version still serves.";
        IReadOnlyList<DatasetChange> changes = reloading.Changes.Count == 0
            ? [new DatasetChange(DatasetChangeKind.Failed, null, null, reason)]
            : [.. reloading.Changes.Select(c => c with { Kind = DatasetChangeKind.Failed, Reason = reason })];
        return new DatasetsUpdate(false, revision, changes);
    }

    /// <summary>
    /// The datasets <paramref name="after"/> serves that <paramref name="before"/>
    /// didn't (added), those it no longer serves (removed, or failed when
    /// <paramref name="after"/> skipped them as unreadable), and those whose
    /// files changed between the two (reloaded). Datasets are matched by path;
    /// ids are as <paramref name="after"/>'s document gives them, or <paramref name="before"/>'s for the removed.
    /// Either is <see langword="null"/> while nothing is served (issue #1601): every dataset was then added, or removed.
    /// </summary>
    public static DatasetsUpdate Between(TileRenderSession? before, TileRenderSession? after, string? revision)
    {
        IReadOnlyList<string> beforePaths = before?.Paths ?? [], beforeNames = before?.Names ?? [];
        IReadOnlyList<string> afterPaths = after?.Paths ?? [], afterNames = after?.Names ?? [];
        var beforeIds = DatasetsDocument.Ids(beforeNames);
        var afterIds = DatasetsDocument.Ids(afterNames);
        var previous = new Dictionary<string, int>(DatasetChanges.PathComparer);
        for (int i = 0; i < beforePaths.Count; i++)
            previous.TryAdd(beforePaths[i], i);

        var changes = new List<DatasetChange>();
        var kept = new HashSet<string>(DatasetChanges.PathComparer);
        for (int i = 0; i < afterPaths.Count; i++)
        {
            var path = afterPaths[i];
            kept.Add(path);
            if (!previous.TryGetValue(path, out var j))
                changes.Add(new DatasetChange(DatasetChangeKind.Added, afterIds[i], afterNames[i]));
            else if (before!.DatasetFingerprints[j] != after!.DatasetFingerprints[i])
                changes.Add(new DatasetChange(DatasetChangeKind.Reloaded, afterIds[i], afterNames[i]));
        }

        // A folder's dataset that no longer opens is skipped, not served: that's a failed reload, not a removal.
        var skipped = (after?.Skipped ?? [])
            .GroupBy(s => Path.GetFullPath(s.Path), DatasetChanges.PathComparer)
            .ToDictionary(g => g.Key, g => g.First(), DatasetChanges.PathComparer);
        for (int i = 0; i < beforePaths.Count; i++)
        {
            if (kept.Contains(beforePaths[i]))
                continue;
            changes.Add(skipped.TryGetValue(beforePaths[i], out var why)
                ? new DatasetChange(DatasetChangeKind.Failed, beforeIds[i], beforeNames[i], why.Reason + " It isn't served now.")
                : new DatasetChange(DatasetChangeKind.Removed, beforeIds[i], beforeNames[i]));
        }

        return new DatasetsUpdate(false, revision, changes);
    }
}
