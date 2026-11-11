namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// One entry of what a weather file holds (<see cref="WeatherGridProcessorBase.Contents"/>),
/// for <c>info</c> output: a parameter of the grid set, or something the reader
/// left out of it.
/// </summary>
/// <param name="Name">
/// The canonical id and the file's name for it (<c>"wave.hs (HTSGW)"</c>),
/// an unknown parameter's reader id (<c>"grib2:0,0,0 (TMP)"</c>), or what was
/// left out (a GRIB2 field, a NetCDF variable).
/// </param>
/// <param name="Level">The level description, for example <c>"10 m above ground"</c>; empty when the reader gives none.</param>
/// <param name="Steps">How many time steps the parameter has; 0 for something left out.</param>
/// <param name="Reason">Why it isn't drawn or sampled, or <see langword="null"/> when it's used.</param>
public sealed record WeatherDatasetContent(string Name, string Level, int Steps, string? Reason)
{
    /// <summary>Whether the parameter is used: drawn where a style applies, and sampled and picked.</summary>
    public bool IsUsed => Reason is null;
}
