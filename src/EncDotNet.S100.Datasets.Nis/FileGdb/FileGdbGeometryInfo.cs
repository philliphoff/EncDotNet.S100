namespace EncDotNet.S100.Datasets.Nis.FileGdb;

/// <summary>
/// How a table's geometry field stores coordinates: each is an integer
/// offset from an origin, divided by a scale.
/// </summary>
internal sealed record FileGdbGeometryInfo(
    string Wkt,
    bool HasZ,
    bool HasM,
    double XOrigin,
    double YOrigin,
    double XYScale,
    double ZOrigin,
    double ZScale);
