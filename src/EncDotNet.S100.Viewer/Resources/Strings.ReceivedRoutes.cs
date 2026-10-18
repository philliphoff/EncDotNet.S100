namespace EncDotNet.S100.Viewer.Resources;

// Received routes, Copy to edit, import and export (#960). Kept in their own
// part so concurrent Routes work doesn't collide.
internal static partial class Strings
{
    public static string Received_NotARoute => Get(nameof(Received_NotARoute));
    public static string Received_TooFewWaypoints => Get(nameof(Received_TooFewWaypoints));
    public static string Received_OlderEditionFormat => Get(nameof(Received_OlderEditionFormat));
    public static string Toast_CheckRouteEndedFormat => Get(nameof(Toast_CheckRouteEndedFormat));
    public static string Received_RouteClosedFormat => Get(nameof(Received_RouteClosedFormat));
    public static string Routes_ReceivedBadge => Get(nameof(Routes_ReceivedBadge));
    public static string Routes_ReceivedSourceFormat => Get(nameof(Routes_ReceivedSourceFormat));
    public static string Routes_ReceivedSourceNoEditionFormat => Get(nameof(Routes_ReceivedSourceNoEditionFormat));
    public static string Routes_RouteMetaReceivedFormat => Get(nameof(Routes_RouteMetaReceivedFormat));
    public static string Routes_DisplayModeNative => Get(nameof(Routes_DisplayModeNative));
    public static string Routes_DisplayModePlanning => Get(nameof(Routes_DisplayModePlanning));
    public static string Tooltip_DisplayModeNative => Get(nameof(Tooltip_DisplayModeNative));
    public static string Tooltip_DisplayModePlanning => Get(nameof(Tooltip_DisplayModePlanning));
    public static string Tooltip_DisplayModeChecking => Get(nameof(Tooltip_DisplayModeChecking));
    public static string Tooltip_EditReceivedRoute => Get(nameof(Tooltip_EditReceivedRoute));
    public static string Menu_CloseRoute => Get(nameof(Menu_CloseRoute));
    public static string Routes_PassageCheckSettings => Get(nameof(Routes_PassageCheckSettings));
    public static string Routes_PassageCheckSettingsNote => Get(nameof(Routes_PassageCheckSettingsNote));
    public static string Routes_PassageRouteValues => Get(nameof(Routes_PassageRouteValues));
    public static string Routes_PassageFromFile => Get(nameof(Routes_PassageFromFile));
    public static string Routes_PassageRouteValuesFromFile => Get(nameof(Routes_PassageRouteValuesFromFile));
}
