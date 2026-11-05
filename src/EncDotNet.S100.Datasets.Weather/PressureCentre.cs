using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// A high or low pressure centre of a surface analysis: its central pressure
/// and where it is.
/// </summary>
/// <param name="Kind">High or low.</param>
/// <param name="PressureHectopascals">The central pressure in whole hectopascals (millibars), for example 1032.</param>
/// <param name="Position">The centre, in WGS-84 degrees.</param>
public sealed record PressureCentre(PressureCentreKind Kind, int PressureHectopascals, GeoPosition Position);
