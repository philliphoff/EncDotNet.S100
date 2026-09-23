using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Features;

/// <summary>
/// One surface of a feature: an exterior ring and the interior rings (holes)
/// that belong to it.
/// </summary>
/// <remarks>
/// A feature may reference several surfaces (an S-100 Part 10a feature record's
/// spatial association field repeats), for example a depth area split by the
/// cell boundary. Each must be filled, outlined and queried on its own: joining
/// their exterior rings into one ring draws and measures segments across the
/// gaps between them (issue #643).
/// </remarks>
/// <param name="ExteriorRing">The exterior ring, in (latitude, longitude) order.</param>
/// <param name="InteriorRings">The interior rings (holes) of this surface.</param>
public sealed record SurfacePart(
    IReadOnlyList<GeoPosition> ExteriorRing,
    IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings);
