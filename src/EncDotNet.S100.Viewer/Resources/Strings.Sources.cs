namespace EncDotNet.S100.Viewer.Resources;

/// <summary>The live feed strings in the Datasets panel (data source design note D3, D8).</summary>
internal static partial class Strings
{
    public static string Tooltip_AddDataSource => Get(nameof(Tooltip_AddDataSource));
    public static string Label_DataSourceAlreadyAddedFormat => Get(nameof(Label_DataSourceAlreadyAddedFormat));
    public static string SourceStatus_Paused => Get(nameof(SourceStatus_Paused));
    public static string SourceStatus_WaitingForZoom => Get(nameof(SourceStatus_WaitingForZoom));
    public static string SourceStatus_WaitingForZoomFormat => Get(nameof(SourceStatus_WaitingForZoomFormat));
    public static string SourceStatus_Failed => Get(nameof(SourceStatus_Failed));
    public static string SourceStatus_FailedSinceFormat => Get(nameof(SourceStatus_FailedSinceFormat));
    public static string SourceStatus_Starting => Get(nameof(SourceStatus_Starting));
    public static string SourceStatus_StaleFormat => Get(nameof(SourceStatus_StaleFormat));
    public static string SourceStatus_LiveFormat => Get(nameof(SourceStatus_LiveFormat));
    public static string SourceStatus_AgeJustNow => Get(nameof(SourceStatus_AgeJustNow));
    public static string SourceStatus_AgeMinutesFormat => Get(nameof(SourceStatus_AgeMinutesFormat));
    public static string SourceStatus_AgeHoursFormat => Get(nameof(SourceStatus_AgeHoursFormat));
    public static string SourceStatus_AgeDaysFormat => Get(nameof(SourceStatus_AgeDaysFormat));
    public static string SourceGroup_Observations => Get(nameof(SourceGroup_Observations));
    public static string SourceGroup_OwnShipSensors => Get(nameof(SourceGroup_OwnShipSensors));
    public static string SourceGroup_Vessels => Get(nameof(SourceGroup_Vessels));
    public static string Toast_SourceRemovedFormat => Get(nameof(Toast_SourceRemovedFormat));
    public static string SourceStatus_LiveJustNow => Get(nameof(SourceStatus_LiveJustNow));
    public static string Button_AddLiveFeed => Get(nameof(Button_AddLiveFeed));
}
