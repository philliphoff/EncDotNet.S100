namespace EncDotNet.S100.Tides.Stations.Noaa;

/// <summary>
/// What NOAA has changed since a catalogue was downloaded, from a cheap
/// check: the station list and a few stations' datums, not the full
/// download.
/// </summary>
public sealed class TideCatalogueUpdateCheck
{
    /// <summary>When the check was made.</summary>
    public required DateTimeOffset Checked { get; init; }

    /// <summary>
    /// The tidal datum epoch NOAA's sentinel stations are now on, when it
    /// differs from the catalogue's (such as <c>2002-2020</c> after NOAA's
    /// national release); otherwise <c>null</c>.
    /// </summary>
    public string? NewDatumEpoch { get; init; }

    /// <summary>Stations NOAA now lists that the catalogue doesn't have.</summary>
    public IReadOnlyList<string> AddedStationIds { get; init; } = [];

    /// <summary>Stations the catalogue has that NOAA no longer lists.</summary>
    public IReadOnlyList<string> RemovedStationIds { get; init; } = [];

    /// <summary>Stations whose position, kind, reference station or offsets NOAA has changed.</summary>
    public IReadOnlyList<string> ChangedStationIds { get; init; } = [];

    /// <summary>Sentinel stations whose datums NOAA has accepted again since the catalogue was downloaded.</summary>
    public IReadOnlyList<string> RevisedDatumStationIds { get; init; } = [];

    /// <summary>Whether NOAA has published newer data than the catalogue holds.</summary>
    public bool HasNewerData =>
        NewDatumEpoch is not null
        || AddedStationIds.Count > 0
        || RemovedStationIds.Count > 0
        || ChangedStationIds.Count > 0
        || RevisedDatumStationIds.Count > 0;
}
