namespace EncDotNet.S100.Datasets.S129.Fusion.Timeline;

/// <summary>
/// Strategy for selecting an <see cref="S129TimelineSnapshot"/> when a
/// requested time falls between two control-point time-stamps in an
/// <see cref="S129TimelineView"/>.
/// </summary>
/// <remarks>
/// S-129 Edition 2.0.0 does not prescribe a behaviour for off-grid time
/// queries — control points are discrete samples along the planned voyage.
/// <see cref="NearestEarlier"/> is the documented default for this
/// library because it matches a "most recent UKC observation as of T"
/// reading: a navigator querying time T sees the UKC reported at the
/// last control point already passed.
/// </remarks>
public enum S129TimelineSamplingMode
{
    /// <summary>
    /// Return the snapshot whose time is the greatest value ≤ <c>t</c>.
    /// When <c>t</c> precedes the first sample, returns <c>null</c>.
    /// This is the library default.
    /// </summary>
    NearestEarlier,

    /// <summary>
    /// Return the snapshot whose time is the least value ≥ <c>t</c>.
    /// When <c>t</c> follows the last sample, returns <c>null</c>.
    /// </summary>
    NearestLater,

    /// <summary>
    /// Return the snapshot whose time is the absolute-closest value to
    /// <c>t</c>. Ties resolve to the earlier sample.
    /// </summary>
    Nearest,

    /// <summary>
    /// Return only a snapshot whose time equals <c>t</c> exactly;
    /// otherwise <c>null</c>.
    /// </summary>
    Exact,
}
