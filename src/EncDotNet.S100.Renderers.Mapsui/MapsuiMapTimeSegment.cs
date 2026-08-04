namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// A closed global-clock interval over which at least one map dataset can
/// portray data.
/// </summary>
/// <param name="Start">Inclusive interval start.</param>
/// <param name="End">Inclusive interval end.</param>
public readonly record struct MapsuiMapTimeSegment(DateTime Start, DateTime End);
