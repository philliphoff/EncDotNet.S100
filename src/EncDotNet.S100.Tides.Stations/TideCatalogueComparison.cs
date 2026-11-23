namespace EncDotNet.S100.Tides.Stations;

/// <summary>
/// What differs between two catalogues of the same provider's stations: the
/// stations added and removed, the stations whose data changed in any way,
/// and the tidal datum epoch.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="Noaa.NoaaTideStationClient.CheckForUpdatesAsync"/>,
/// which compares a catalogue with NOAA's station list and a few stations'
/// datums, this compares every station's whole entry: its name, position,
/// constituents, datums, reference station and offsets, as the
/// <see cref="TideStationCatalogueFile"/> format writes them. A catalogue
/// mirror uses it to publish a new download only when NOAA's data changed,
/// not every time it downloads.
/// </para>
/// <para>
/// The catalogues' <see cref="TideStationCatalogue.Provider"/> and
/// <see cref="TideStationCatalogue.Retrieved"/> aren't compared.
/// </para>
/// </remarks>
public sealed class TideCatalogueComparison
{
    private TideCatalogueComparison()
    {
    }

    /// <summary>Tide stations the newer catalogue has and the older doesn't.</summary>
    public IReadOnlyList<string> AddedStationIds { get; private init; } = [];

    /// <summary>Tide stations the older catalogue has and the newer doesn't.</summary>
    public IReadOnlyList<string> RemovedStationIds { get; private init; } = [];

    /// <summary>Tide stations in both whose data differs.</summary>
    public IReadOnlyList<string> ChangedStationIds { get; private init; } = [];

    /// <summary>Current stations the newer catalogue has and the older doesn't.</summary>
    public IReadOnlyList<string> AddedCurrentStationIds { get; private init; } = [];

    /// <summary>Current stations the older catalogue has and the newer doesn't.</summary>
    public IReadOnlyList<string> RemovedCurrentStationIds { get; private init; } = [];

    /// <summary>Current stations in both whose data differs.</summary>
    public IReadOnlyList<string> ChangedCurrentStationIds { get; private init; } = [];

    /// <summary>The older catalogue's <see cref="TideStationCatalogue.DatumEpoch"/>.</summary>
    public string? PreviousDatumEpoch { get; private init; }

    /// <summary>The newer catalogue's <see cref="TideStationCatalogue.DatumEpoch"/>.</summary>
    public string? DatumEpoch { get; private init; }

    /// <summary>Whether the two catalogues hold different data.</summary>
    public bool HasChanges =>
        AddedStationIds.Count > 0
        || RemovedStationIds.Count > 0
        || ChangedStationIds.Count > 0
        || AddedCurrentStationIds.Count > 0
        || RemovedCurrentStationIds.Count > 0
        || ChangedCurrentStationIds.Count > 0
        || !string.Equals(PreviousDatumEpoch, DatumEpoch, StringComparison.Ordinal);

    /// <summary>Compares two catalogues.</summary>
    /// <param name="previous">The older catalogue.</param>
    /// <param name="current">The newer catalogue.</param>
    /// <returns>What differs. Each list of identifiers is in the order of the catalogue it comes from.</returns>
    public static TideCatalogueComparison Compare(TideStationCatalogue previous, TideStationCatalogue current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var (added, removed, changed) = Diff(
            previous.Stations, current.Stations, s => s.Id, id => previous.Find(id), id => current.Find(id), TideStationCatalogueFile.ToJson);
        var (addedCurrents, removedCurrents, changedCurrents) = Diff(
            previous.CurrentStations,
            current.CurrentStations,
            s => s.Id,
            id => previous.FindCurrentStation(id),
            id => current.FindCurrentStation(id),
            TideStationCatalogueFile.ToJson);

        return new TideCatalogueComparison
        {
            AddedStationIds = added,
            RemovedStationIds = removed,
            ChangedStationIds = changed,
            AddedCurrentStationIds = addedCurrents,
            RemovedCurrentStationIds = removedCurrents,
            ChangedCurrentStationIds = changedCurrents,
            PreviousDatumEpoch = previous.DatumEpoch,
            DatumEpoch = current.DatumEpoch,
        };
    }

    private static (List<string> Added, List<string> Removed, List<string> Changed) Diff<T>(
        IReadOnlyList<T> previous,
        IReadOnlyList<T> current,
        Func<T, string> id,
        Func<string, T?> findPrevious,
        Func<string, T?> findCurrent,
        Func<T, string> toJson)
        where T : class
    {
        var added = new List<string>();
        var changed = new List<string>();
        foreach (var station in current)
        {
            if (findPrevious(id(station)) is not { } was)
            {
                added.Add(id(station));
            }
            else if (!string.Equals(toJson(was), toJson(station), StringComparison.Ordinal))
            {
                changed.Add(id(station));
            }
        }

        var removed = previous.Where(s => findCurrent(id(s)) is null).Select(id).ToList();
        return (added, removed, changed);
    }
}
