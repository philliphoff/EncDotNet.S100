namespace EncDotNet.S100.Viewer.Resources;

// Chart-wide water level adjustment (#1132): the setting, the depth basis
// indicator's adjusted states and the adjusted-area boundary.
internal static partial class Strings
{
    public static string Settings_WaterLevelAdjustment => Get(nameof(Settings_WaterLevelAdjustment));
    public static string Settings_WaterLevelAdjustment_Help => Get(nameof(Settings_WaterLevelAdjustment_Help));
    public static string Settings_WaterLevelAdjustment_Off => Get(nameof(Settings_WaterLevelAdjustment_Off));
    public static string Settings_WaterLevelAdjustment_AtViewTime => Get(nameof(Settings_WaterLevelAdjustment_AtViewTime));
    public static string Settings_WaterLevelApplyUndeclaredDatum => Get(nameof(Settings_WaterLevelApplyUndeclaredDatum));
    public static string Tooltip_WaterLevelApplyUndeclaredDatum => Get(nameof(Tooltip_WaterLevelApplyUndeclaredDatum));
    public static string Status_DepthBasis_AdjustedFormat => Get(nameof(Status_DepthBasis_AdjustedFormat));
    public static string Status_DepthBasis_AdjustedFormat_Compact => Get(nameof(Status_DepthBasis_AdjustedFormat_Compact));
    public static string Status_DepthBasis_AdjustedPartialFormat => Get(nameof(Status_DepthBasis_AdjustedPartialFormat));
    public static string Status_DepthBasis_AdjustedPartialFormat_Short => Get(nameof(Status_DepthBasis_AdjustedPartialFormat_Short));
    public static string Status_DepthBasis_DatumUnconfirmedSuffix => Get(nameof(Status_DepthBasis_DatumUnconfirmedSuffix));
    public static string Status_DepthBasis_NoWaterLevelAtFormat => Get(nameof(Status_DepthBasis_NoWaterLevelAtFormat));
    public static string Status_DepthBasis_NoWaterLevelAt_Short => Get(nameof(Status_DepthBasis_NoWaterLevelAt_Short));
    public static string Status_DepthBasis_DatumMismatch => Get(nameof(Status_DepthBasis_DatumMismatch));
    public static string Status_DepthBasisPopup_AdjustedBasisFormat => Get(nameof(Status_DepthBasisPopup_AdjustedBasisFormat));
    public static string Status_DepthBasisPopup_AdjustedDatumSame => Get(nameof(Status_DepthBasisPopup_AdjustedDatumSame));
    public static string Status_DepthBasisPopup_AdjustedDatumUnconfirmed => Get(nameof(Status_DepthBasisPopup_AdjustedDatumUnconfirmed));
    public static string Status_DepthBasisPopup_NotAdjustedFormat => Get(nameof(Status_DepthBasisPopup_NotAdjustedFormat));
    public static string Status_DepthBasisPopup_NoWaterLevelInViewNow => Get(nameof(Status_DepthBasisPopup_NoWaterLevelInViewNow));
    public static string Status_DepthBasisPopup_OutsideTimeRangeFormat => Get(nameof(Status_DepthBasisPopup_OutsideTimeRangeFormat));
    public static string Status_DepthBasisPopup_WhereChart => Get(nameof(Status_DepthBasisPopup_WhereChart));
    public static string Pick_Depth_ChartBasisAdjustedFormat => Get(nameof(Pick_Depth_ChartBasisAdjustedFormat));
    public static string Map_WaterLevelAdjustmentBoundary => Get(nameof(Map_WaterLevelAdjustmentBoundary));
    public static string Pick_Depth_DatumUnconfirmed => Get(nameof(Pick_Depth_DatumUnconfirmed));
}
