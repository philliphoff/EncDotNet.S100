using Microsoft.Extensions.Logging;

namespace EncDotNet.S100.Viewer.Resources;

// Logs panel and recent logs in feedback reports (#1080). Kept in its own part
// so concurrent viewer work doesn't collide.
internal static partial class Strings
{
    public static string Pane_Logs => Get(nameof(Pane_Logs));
    public static string Label_Activity_Logs => Get(nameof(Label_Activity_Logs));
    public static string Tooltip_Logs => Get(nameof(Tooltip_Logs));
    public static string Menu_ShowLogs => Get(nameof(Menu_ShowLogs));
    public static string Logs_Column_Time => Get(nameof(Logs_Column_Time));
    public static string Logs_Column_Level => Get(nameof(Logs_Column_Level));
    public static string Logs_Column_Area => Get(nameof(Logs_Column_Area));
    public static string Logs_Column_Message => Get(nameof(Logs_Column_Message));
    public static string Logs_Filter_All => Get(nameof(Logs_Filter_All));
    public static string Logs_Filter_Information => Get(nameof(Logs_Filter_Information));
    public static string Logs_Filter_Warning => Get(nameof(Logs_Filter_Warning));
    public static string Logs_Filter_Error => Get(nameof(Logs_Filter_Error));
    public static string Label_LogsLevelFilter => Get(nameof(Label_LogsLevelFilter));
    public static string Tooltip_LogsLevelFilter => Get(nameof(Tooltip_LogsLevelFilter));
    public static string Label_LogsSearch => Get(nameof(Label_LogsSearch));
    public static string Logs_SearchWatermark => Get(nameof(Logs_SearchWatermark));
    public static string Logs_Verbose => Get(nameof(Logs_Verbose));
    public static string Tooltip_LogsVerbose => Get(nameof(Tooltip_LogsVerbose));
    public static string Logs_Copy => Get(nameof(Logs_Copy));
    public static string Tooltip_LogsCopy => Get(nameof(Tooltip_LogsCopy));
    public static string Logs_Clear => Get(nameof(Logs_Clear));
    public static string Tooltip_LogsClear => Get(nameof(Tooltip_LogsClear));
    public static string Logs_SendFeedback => Get(nameof(Logs_SendFeedback));
    public static string Tooltip_LogsSendFeedback => Get(nameof(Tooltip_LogsSendFeedback));
    public static string Logs_Empty => Get(nameof(Logs_Empty));
    public static string Logs_EmptyFiltered => Get(nameof(Logs_EmptyFiltered));
    public static string Logs_NewEntriesFormat => Get(nameof(Logs_NewEntriesFormat));
    public static string Label_LogsNewEntriesFormat => Get(nameof(Label_LogsNewEntriesFormat));
    public static string Tooltip_LogsNewEntries => Get(nameof(Tooltip_LogsNewEntries));
    public static string Logs_RepeatFormat => Get(nameof(Logs_RepeatFormat));
    public static string Logs_BadgeName => Get(nameof(Logs_BadgeName));
    public static string Logs_RowNameFormat => Get(nameof(Logs_RowNameFormat));
    public static string Logs_Details_Category => Get(nameof(Logs_Details_Category));
    public static string Logs_Details_Exception => Get(nameof(Logs_Details_Exception));
    public static string Logs_Details_Repeated => Get(nameof(Logs_Details_Repeated));
    public static string Label_LogsCloseDetails => Get(nameof(Label_LogsCloseDetails));
    public static string Tooltip_LogsCloseDetails => Get(nameof(Tooltip_LogsCloseDetails));
    public static string Label_LogsEntries => Get(nameof(Label_LogsEntries));
    public static string Feedback_RawLogsHeader => Get(nameof(Feedback_RawLogsHeader));

    /// <summary>
    /// The localized name of a log level, as the Logs panel shows it
    /// (<c>LogLevel_*</c>).
    /// </summary>
    public static string LogLevelName(LogLevel level) =>
        ResourceManager.GetString("LogLevel_" + level, Culture) ?? level.ToString();

    /// <summary>
    /// The short label of a log level for the Logs panel's narrow level
    /// column (<c>LogLevelShort_*</c>, #1171), falling back to
    /// <see cref="LogLevelName"/>.
    /// </summary>
    public static string LogLevelShortName(LogLevel level) =>
        ResourceManager.GetString("LogLevelShort_" + level, Culture) ?? LogLevelName(level);

    /// <summary>
    /// The localized name of a Logs panel area id (see
    /// <c>Diagnostics.LogAreas</c>), or the id itself when it has none (a
    /// category no rule matched shows its type name).
    /// </summary>
    public static string LogAreaName(string area) =>
        ResourceManager.GetString("LogArea_" + area, Culture) ?? area;
}
