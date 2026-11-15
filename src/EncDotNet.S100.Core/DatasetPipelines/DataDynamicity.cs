namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// How a water level or surface current series was produced, so a host can
/// label every value as observed, predicted, analysed, hindcast or forecast
/// (#1505). The numbers are S-104 Edition 2.0.0 <c>dataDynamicity</c>;
/// <see cref="DataDynamicityCodes.FromS111TypeOfCurrentData"/> maps S-111's
/// <c>typeOfCurrentData</c> onto them.
/// </summary>
public enum DataDynamicity
{
    /// <summary>Observed values (S-104 <c>1</c>; S-111 <c>1</c> historical and <c>2</c> real-time observation).</summary>
    Observation = 1,

    /// <summary>Astronomical prediction: the tide only, not a forecast (S-104 <c>2</c>; S-111 <c>3</c>).</summary>
    AstronomicalPrediction = 2,

    /// <summary>Analysis or hybrid method (S-104 <c>3</c>; S-111 <c>4</c>).</summary>
    AnalysisOrHybrid = 3,

    /// <summary>Hydrodynamic model hindcast (S-104 <c>4</c>; S-111 <c>5</c>).</summary>
    HydrodynamicHindcast = 4,

    /// <summary>Hydrodynamic model forecast (S-104 <c>5</c>; S-111 <c>6</c>).</summary>
    HydrodynamicForecast = 5,
}

/// <summary>Reads <see cref="DataDynamicity"/> from the products' own codes.</summary>
public static class DataDynamicityCodes
{
    /// <summary>
    /// An S-104 Edition 2.0.0 <c>dataDynamicity</c> value, or
    /// <see langword="null"/> when it is absent or not one of <c>1</c>–<c>5</c>.
    /// </summary>
    /// <param name="code">The attribute's value.</param>
    public static DataDynamicity? FromS104(int? code) =>
        code is >= 1 and <= 5 ? (DataDynamicity)code.Value : null;

    /// <summary>
    /// An S-111 <c>typeOfCurrentData</c> value (<c>1</c> historical
    /// observation, <c>2</c> real-time observation, <c>3</c> astronomical
    /// prediction, <c>4</c> analysis or hybrid, <c>5</c> hindcast, <c>6</c>
    /// forecast), or <see langword="null"/> when it is absent or unknown.
    /// </summary>
    /// <param name="code">The attribute's value.</param>
    public static DataDynamicity? FromS111TypeOfCurrentData(int? code) => code switch
    {
        1 or 2 => DataDynamicity.Observation,
        3 => DataDynamicity.AstronomicalPrediction,
        4 => DataDynamicity.AnalysisOrHybrid,
        5 => DataDynamicity.HydrodynamicHindcast,
        6 => DataDynamicity.HydrodynamicForecast,
        _ => null,
    };
}
