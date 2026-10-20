namespace EncDotNet.S100.Viewer.Resources;

// S-124 warnings in place while checking a route (#959 E1–E4). Kept in their
// own part so concurrent Routes and warnings work doesn't collide.
internal static partial class Strings
{
    public static string Findings_OpenInWarnings => Get(nameof(Findings_OpenInWarnings));
    public static string Tooltip_FindingOpenInWarnings => Get(nameof(Tooltip_FindingOpenInWarnings));
    public static string NavWarnings_ScopeAllFormat => Get(nameof(NavWarnings_ScopeAllFormat));
    public static string NavWarnings_ScopeRouteFormat => Get(nameof(NavWarnings_ScopeRouteFormat));
    public static string Tooltip_NavWarnings_ScopeAll => Get(nameof(Tooltip_NavWarnings_ScopeAll));
    public static string Tooltip_NavWarnings_ScopeRoute => Get(nameof(Tooltip_NavWarnings_ScopeRoute));
    public static string NavWarnings_OnRouteAtFormat => Get(nameof(NavWarnings_OnRouteAtFormat));
    public static string NavWarnings_NearRouteAtFormat => Get(nameof(NavWarnings_NearRouteAtFormat));
    public static string NavWarnings_OnRoute => Get(nameof(NavWarnings_OnRoute));
    public static string NavWarnings_NearRoute => Get(nameof(NavWarnings_NearRoute));
    public static string NavWarnings_NoneOnRoute => Get(nameof(NavWarnings_NoneOnRoute));
}
