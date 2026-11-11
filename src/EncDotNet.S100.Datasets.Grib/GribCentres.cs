namespace EncDotNet.S100.Datasets.Grib;

/// <summary>
/// Short names for the originating centres (WMO common code table C-11) of the
/// sources the weather readers target, for display and for
/// <see cref="Weather.WeatherOrigin.Centre"/>.
/// </summary>
public static class GribCentres
{
    /// <summary>US National Centers for Environmental Prediction (GFS, GFS-Wave).</summary>
    public const ushort Ncep = 7;

    /// <summary>European Centre for Medium-Range Weather Forecasts.</summary>
    public const ushort Ecmwf = 98;

    /// <summary>Deutscher Wetterdienst.</summary>
    public const ushort Dwd = 78;

    /// <summary>
    /// Returns a short name for a centre, or <see langword="null"/> when the
    /// centre isn't in this short list.
    /// </summary>
    /// <param name="centre">The centre code from Section 1.</param>
    public static string? GetName(ushort centre) => centre switch
    {
        Ncep => "NCEP",
        8 => "NWS",
        34 => "JMA",
        54 => "CMC",
        74 => "UKMO",
        Dwd => "DWD",
        84 or 85 => "Météo-France",
        Ecmwf => "ECMWF",
        _ => null,
    };
}
