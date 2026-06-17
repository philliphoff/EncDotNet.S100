namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Geographic location (WGS84) of the point the user clicked to produce a
/// pick. Pick reports describe features at a location, but that location is
/// otherwise not part of the report; capturing it here lets the Object
/// Information panel show — and copy — the exact lat/lon for debugging.
/// </summary>
/// <param name="Latitude">Latitude in decimal degrees (positive north).</param>
/// <param name="Longitude">Longitude in decimal degrees (positive east).</param>
internal readonly record struct PickLocation(double Latitude, double Longitude);
