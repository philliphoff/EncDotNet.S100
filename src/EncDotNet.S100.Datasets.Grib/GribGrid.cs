namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// The grid definition section (Section 3) of a GRIB2 field.
/// </summary>
/// <param name="TemplateNumber">The grid definition template number (code table 3.1), for example <c>0</c> for a regular latitude/longitude grid or <c>30</c> for Lambert conformal.</param>
/// <param name="NumberOfPoints">The number of grid points, including any the bitmap marks as missing.</param>
/// <param name="LatLon">
/// The grid's geometry when it's a supported regular latitude/longitude grid
/// (template 3.0); otherwise <see langword="null"/>, and the field's
/// <see cref="GribField.UnsupportedReason"/> says why.
/// </param>
public sealed record GribGrid(int TemplateNumber, long NumberOfPoints, GribLatLonGrid? LatLon);
