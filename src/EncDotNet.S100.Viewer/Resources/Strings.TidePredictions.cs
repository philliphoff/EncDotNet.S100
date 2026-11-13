namespace EncDotNet.S100.Viewer.Resources;

// Tide predictions as a water-level source (#1451): the setting, the Depth
// card, the route check's clearance readout and its findings.
internal static partial class Strings
{
    public static string Settings_UseTidePredictions => Get(nameof(Settings_UseTidePredictions));
    public static string Label_UseTidePredictions => Get(nameof(Label_UseTidePredictions));
    public static string Settings_UseTidePredictions_Help => Get(nameof(Settings_UseTidePredictions_Help));
    public static string Pick_Depth_Label_DepthNowPredicted => Get(nameof(Pick_Depth_Label_DepthNowPredicted));
    public static string Pick_Depth_PredictedSourceFormat => Get(nameof(Pick_Depth_PredictedSourceFormat));
    public static string Pick_Depth_PredictedAwayFormat => Get(nameof(Pick_Depth_PredictedAwayFormat));
    public static string Pick_Depth_PredictedHarmonic => Get(nameof(Pick_Depth_PredictedHarmonic));
    public static string Pick_Depth_PredictedFromReferenceFormat => Get(nameof(Pick_Depth_PredictedFromReferenceFormat));
    public static string Pick_Depth_PredictedNotAForecast => Get(nameof(Pick_Depth_PredictedNotAForecast));
    public static string Pick_Depth_PredictionUncertainty => Get(nameof(Pick_Depth_PredictionUncertainty));
    public static string Pick_Depth_ChartedUncertaintyPredictedFormat => Get(nameof(Pick_Depth_ChartedUncertaintyPredictedFormat));
    public static string Pick_Depth_Refused_DifferentDatumPredicted => Get(nameof(Pick_Depth_Refused_DifferentDatumPredicted));
    public static string Pick_Depth_PredictionNoun => Get(nameof(Pick_Depth_PredictionNoun));
    public static string Pick_Depth_Series_Predicted => Get(nameof(Pick_Depth_Series_Predicted));
    public static string Clearance_SourcePredictedFormat => Get(nameof(Clearance_SourcePredictedFormat));
    public static string Clearance_PredictedStationFormat => Get(nameof(Clearance_PredictedStationFormat));
    public static string Clearance_Gap_WaterLevelPredicted => Get(nameof(Clearance_Gap_WaterLevelPredicted));
    public static string Check_ConfidencePredictedFormat => Get(nameof(Check_ConfidencePredictedFormat));
    public static string Check_WindowCaveatPredicted => Get(nameof(Check_WindowCaveatPredicted));
    public static string Findings_WaterLevelPredictedFormat => Get(nameof(Findings_WaterLevelPredictedFormat));
    public static string Findings_TightOnPredictedFormat => Get(nameof(Findings_TightOnPredictedFormat));
    public static string Findings_PredictedStationsFormat => Get(nameof(Findings_PredictedStationsFormat));
    public static string Findings_MoreStationsFormat => Get(nameof(Findings_MoreStationsFormat));
    public static string Findings_NotAForecast => Get(nameof(Findings_NotAForecast));
}
