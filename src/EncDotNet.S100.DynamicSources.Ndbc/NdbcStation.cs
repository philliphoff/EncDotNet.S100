namespace EncDotNet.S100.DynamicSources.Ndbc;

/// <summary>
/// One station in the NDBC station table (<c>activestations.xml</c>): its
/// identifier, position and what it reports.
/// </summary>
/// <param name="Id">
/// The station identifier, upper case as in the <c>realtime2</c> file names:
/// a WMO number for a moored buoy (e.g. <c>44013</c>) or a five-character
/// call for a C-MAN or partner station (e.g. <c>BUZM3</c>).
/// </param>
/// <param name="Name">The station's name, e.g. <c>"BOSTON 16 NM East of Boston, MA"</c>.</param>
/// <param name="Latitude">Latitude in decimal degrees, WGS-84.</param>
/// <param name="Longitude">Longitude in decimal degrees, WGS-84, -180 to 180.</param>
public sealed record NdbcStation(string Id, string Name, double Latitude, double Longitude)
{
    /// <summary>The station type as NDBC lists it, e.g. <c>buoy</c>, <c>fixed</c> (C-MAN and other fixed platforms), <c>dart</c> or <c>tao</c>.</summary>
    public string? Type { get; init; }

    /// <summary>The owner, e.g. <c>NDBC</c> or a partner programme.</summary>
    public string? Owner { get; init; }

    /// <summary>The programme the station belongs to, e.g. <c>"NDBC Meteorological/Ocean"</c>.</summary>
    public string? Program { get; init; }

    /// <summary>Whether the station reports meteorological data (<c>met="y"</c>), and so has a <c>realtime2/{id}.txt</c> standard meteorological file.</summary>
    public bool HasMeteorology { get; init; }

    /// <summary>Whether the station is a moored or drifting buoy (<see cref="Type"/> <c>buoy</c>).</summary>
    public bool IsBuoy => string.Equals(Type, "buoy", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether the station is a fixed platform, such as a C-MAN station (<see cref="Type"/> <c>fixed</c>).</summary>
    public bool IsFixed => string.Equals(Type, "fixed", StringComparison.OrdinalIgnoreCase);
}
