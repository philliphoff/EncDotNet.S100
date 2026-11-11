namespace EncDotNet.S100.Datasets.NetCdf;

/// <summary>
/// A data variable of a NetCDF file that isn't on the weather grid, so it
/// isn't in the <see cref="Weather.WeatherGridSet"/> at all, with why: for
/// example a variable with several depth levels, or one along other
/// dimensions. Hosts list these in <c>info</c> output.
/// </summary>
/// <param name="Name">The variable's name.</param>
/// <param name="Reason">Why it was left out, as a sentence fragment.</param>
public sealed record NetCdfIgnoredVariable(string Name, string Reason);
