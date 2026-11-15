namespace EncDotNet.S100.Viewer.Resources;

// How S-104 water levels and S-111 currents were produced (#1505): the labels
// Object information and the Legend give them, shared with SoundCharts for
// ArcGIS (design brief §7.2).
internal static partial class Strings
{
    public static string DataDynamicity_Observed => Get(nameof(DataDynamicity_Observed));
    public static string DataDynamicity_Predicted => Get(nameof(DataDynamicity_Predicted));
    public static string DataDynamicity_Analysis => Get(nameof(DataDynamicity_Analysis));
    public static string DataDynamicity_Hindcast => Get(nameof(DataDynamicity_Hindcast));
    public static string DataDynamicity_Forecast => Get(nameof(DataDynamicity_Forecast));
    public static string DataDynamicity_ForecastRunFormat => Get(nameof(DataDynamicity_ForecastRunFormat));
}
