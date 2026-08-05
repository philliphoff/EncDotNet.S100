namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A renderer-neutral geographic pick request for
/// <see cref="IS100MapQuery.PickAsync"/>.
/// </summary>
/// <remarks>
/// The query is purely geographic — screen-to-world conversion and pointer
/// gestures belong in UI-framework interaction adapters, not here.
/// </remarks>
public sealed record GeographicPickQuery
{
    /// <summary>Pick latitude in WGS-84 degrees.</summary>
    public required double Latitude { get; init; }

    /// <summary>Pick longitude in WGS-84 degrees.</summary>
    public required double Longitude { get; init; }

    /// <summary>
    /// Search tolerance for point / curve features in metres; area features use
    /// exact containment and ignore it. Non-finite or negative values are
    /// treated as 0. Defaults to 50 m.
    /// </summary>
    public double RadiusMeters { get; init; } = 50.0;

    /// <summary>
    /// Optional cap on the number of returned picks (topmost-first). When
    /// <see langword="null"/>, all matches are returned.
    /// </summary>
    public int? MaxResults { get; init; }
}
