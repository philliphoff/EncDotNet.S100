namespace EncDotNet.S100.Features;

/// <summary>
/// The ordinate order <see cref="GmlCoordinateParser"/> assumes for
/// <c>EPSG:4326</c> positions.
/// </summary>
public enum GmlAxisOrder
{
    /// <summary>
    /// Latitude first, as S-100 Part 10b §6.2 requires, except where a
    /// position list shows itself to be longitude first (a first ordinate
    /// beyond ±90°). The default.
    /// </summary>
    Auto,

    /// <summary>
    /// Longitude first for every position. Used for a dataset that
    /// <see cref="GmlCoordinateParser.DetectAxisOrder"/> found to be longitude
    /// first, so that positions whose longitude happens to lie within ±90° are
    /// read the same way as the rest (issue #760).
    /// </summary>
    LongitudeFirst,
}
