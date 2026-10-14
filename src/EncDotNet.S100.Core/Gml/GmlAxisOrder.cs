namespace EncDotNet.S100.Features;

/// <summary>
/// The ordinate order <see cref="GmlCoordinateParser"/> reads
/// <c>EPSG:4326</c> positions in.
/// </summary>
public enum GmlAxisOrder
{
    /// <summary>
    /// Latitude first, as S-100 Part 10b §6.2 requires. The default.
    /// </summary>
    LatitudeFirst,

    /// <summary>
    /// Longitude first for every position. Used for a dataset that
    /// <see cref="GmlCoordinateParser.AssessAxisOrder"/> found to be encoded
    /// longitude first (issue #650).
    /// </summary>
    LongitudeFirst,
}
