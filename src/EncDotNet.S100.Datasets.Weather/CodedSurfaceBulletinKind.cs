namespace EncDotNet.S100.Datasets.Weather;

/// <summary>Whether a coded surface bulletin is an analysis or a forecast.</summary>
public enum CodedSurfaceBulletinKind
{
    /// <summary>
    /// The coded surface analysis (<c>CODSUS</c>), one analysis valid at one
    /// time.
    /// </summary>
    Analysis,

    /// <summary>
    /// A coded forecast bulletin (for example the short-range <c>CODSRP</c>):
    /// one or more forecasts, each valid at its own time.
    /// </summary>
    Forecast,
}
