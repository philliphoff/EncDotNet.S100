namespace EncDotNet.S100.Datasets.Nis;

/// <summary>
/// A WGS 84 position in decimal degrees, with the depth of a sounding when
/// there is one.
/// </summary>
/// <param name="Longitude">Longitude in decimal degrees, positive east.</param>
/// <param name="Latitude">Latitude in decimal degrees, positive north.</param>
/// <param name="Z">
/// The stored Z value, or <see langword="null"/> when the geometry has none.
/// For NIS soundings it is the depth in metres, positive down.
/// </param>
public readonly record struct NisPosition(double Longitude, double Latitude, double? Z = null);
