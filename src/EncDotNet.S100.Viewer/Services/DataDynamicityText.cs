using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// How S-104 water levels and S-111 currents were produced, as Object
/// information and the Legend say it (#1505): <b>Observed</b>,
/// <b>Predicted</b> (with "astronomical tide only, not a forecast"),
/// <b>Analysis</b>, <b>Hindcast</b> or <b>Forecast</b> (with "run {time}"
/// from the dataset's issue time). The same labels as SoundCharts for ArcGIS
/// (design brief §7.2), so both products describe values the same way.
/// </summary>
internal static class DataDynamicityText
{
    /// <summary>The label for <paramref name="kind"/>.</summary>
    /// <param name="kind">How the values were produced.</param>
    /// <param name="runTime">A forecast's run time (UTC), or null when unknown; ignored for other kinds.</param>
    /// <param name="format">The user's Local/UTC setting, for the run time.</param>
    public static string Label(DataDynamicity kind, DateTime? runTime, TimeFormat format) => kind switch
    {
        DataDynamicity.Observation => Strings.DataDynamicity_Observed,
        DataDynamicity.AstronomicalPrediction => Strings.DataDynamicity_Predicted,
        DataDynamicity.AnalysisOrHybrid => Strings.DataDynamicity_Analysis,
        DataDynamicity.HydrodynamicHindcast => Strings.DataDynamicity_Hindcast,
        _ => runTime is { } run
            ? string.Format(CultureInfo.CurrentCulture, Strings.DataDynamicity_ForecastRunFormat, TimeFormatting.Format(run, format))
            : Strings.DataDynamicity_Forecast,
    };

    /// <summary>
    /// The label a <see cref="PickAttribute.DataDynamicityCode"/> row carries,
    /// or null when <paramref name="row"/> isn't one or its value is unknown.
    /// </summary>
    /// <param name="row">An Object information row.</param>
    /// <param name="format">The user's Local/UTC setting, for a forecast's run time.</param>
    public static string? LabelOf(PickAttribute row, TimeFormat format)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!string.Equals(row.Code, PickAttribute.DataDynamicityCode, StringComparison.Ordinal)
            || !int.TryParse(row.RawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)
            || DataDynamicityCodes.FromS104(code) is not { } kind)
        {
            return null;
        }
        return Label(kind, row.DateTimeValue, format);
    }
}
