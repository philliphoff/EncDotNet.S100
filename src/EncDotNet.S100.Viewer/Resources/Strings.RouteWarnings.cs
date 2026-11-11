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

    // The Warning card's "Crosses route · leg 3 → 4" row (#959).
    public static string Pick_Warning_CrossesRouteLegFormat => Get(nameof(Pick_Warning_CrossesRouteLegFormat));
    public static string Pick_Warning_NearRouteLegFormat => Get(nameof(Pick_Warning_NearRouteLegFormat));
    public static string Pick_Warning_CrossesRouteLegsFormat => Get(nameof(Pick_Warning_CrossesRouteLegsFormat));
    public static string Pick_Warning_NearRouteLegsFormat => Get(nameof(Pick_Warning_NearRouteLegsFormat));
    public static string Pick_Warning_CrossesRoute => Get(nameof(Pick_Warning_CrossesRoute));
    public static string Pick_Warning_NearRoute => Get(nameof(Pick_Warning_NearRoute));
    public static string Tooltip_WarningRouteFinding => Get(nameof(Tooltip_WarningRouteFinding));
}
