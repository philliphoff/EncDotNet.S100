using System.Globalization;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// A GRIB2 parameter identity: the discipline (Section 0, code table 0.0),
/// the parameter category (code table 4.1) and the parameter number (code
/// table 4.2) of a product definition template.
/// </summary>
/// <param name="Discipline">The discipline, e.g. <c>0</c> for meteorological products or <c>10</c> for oceanographic products.</param>
/// <param name="Category">The parameter category within the discipline, e.g. <c>2</c> for momentum.</param>
/// <param name="Number">The parameter number within the category, e.g. <c>2</c> for the u-component of wind.</param>
public readonly record struct GribParameterCode(byte Discipline, byte Category, byte Number)
{
    /// <summary>Returns the code as <c>"discipline,category,number"</c>, e.g. <c>"0,2,2"</c>.</summary>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Discipline},{Category},{Number}");
}
