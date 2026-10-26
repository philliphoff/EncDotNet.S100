using EncDotNet.S100.Datasets.Weather;

namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// The NCEP short names (as <c>wgrib2</c> prints them, from NCEP's GRIB2
/// parameter tables) of the wind, wave and pressure parameters the target
/// sources carry, for listing a file's contents. Only for display: the
/// readers map parameters by their codes.
/// </summary>
internal static class GribShortNames
{
    /// <summary>The short name of a parameter, or <see langword="null"/> when this list doesn't have it.</summary>
    public static string? Get(GribParameterCode code) => (code.Discipline, code.Category, code.Number) switch
    {
        (0, 0, 0) => "TMP",
        (0, 0, 4) => "TMAX",
        (0, 0, 5) => "TMIN",
        (0, 2, 0) => "WDIR",
        (0, 2, 1) => "WIND",
        (0, 2, 2) => "UGRD",
        (0, 2, 3) => "VGRD",
        (0, 2, 22) => "GUST",
        (0, 3, 0) => "PRES",
        (0, 3, 1) => "PRMSL",
        (10, 0, 3) => "HTSGW",
        (10, 0, 4) => "WVDIR",
        (10, 0, 5) => "WVHGT",
        (10, 0, 6) => "WVPER",
        (10, 0, 7) => "SWDIR",
        (10, 0, 8) => "SWELL",
        (10, 0, 9) => "SWPER",
        (10, 0, 10) => "DIRPW",
        (10, 0, 11) => "PERPW",
        (10, 0, 14) => "MWSDIR",
        (10, 0, 15) => "MWSPER",
        _ => null,
    };

    /// <summary>The short name and code, for example <c>"HTSGW (10,0,3)"</c>, or just the code.</summary>
    public static string Describe(GribParameterCode code) =>
        Get(code) is { } name ? $"{name} ({code})" : code.ToString();
}
