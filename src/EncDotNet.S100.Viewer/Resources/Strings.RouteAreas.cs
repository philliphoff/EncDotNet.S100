namespace EncDotNet.S100.Viewer.Resources;

// Route checks against restricted and protected areas (#956). Kept in their
// own part so concurrent route-check work doesn't collide.
internal static partial class Strings
{
    public static string RouteArea_EntersFormat => Get(nameof(RouteArea_EntersFormat));
    public static string RouteArea_CrossesFormat => Get(nameof(RouteArea_CrossesFormat));
    public static string RouteArea_InCorridorFormat => Get(nameof(RouteArea_InCorridorFormat));
    public static string RouteArea_NotInForceFormat => Get(nameof(RouteArea_NotInForceFormat));
    public static string RouteArea_NotInForceAtFormat => Get(nameof(RouteArea_NotInForceAtFormat));
    public static string RouteArea_RestrictedArea => Get(nameof(RouteArea_RestrictedArea));
    public static string RouteArea_MilitaryPracticeArea => Get(nameof(RouteArea_MilitaryPracticeArea));
    public static string RouteArea_CautionArea => Get(nameof(RouteArea_CautionArea));
    public static string RouteArea_AnchorageArea => Get(nameof(RouteArea_AnchorageArea));
    public static string RouteArea_SeparationZone => Get(nameof(RouteArea_SeparationZone));
    public static string RouteArea_PrecautionaryArea => Get(nameof(RouteArea_PrecautionaryArea));
    public static string RouteArea_InshoreTrafficZone => Get(nameof(RouteArea_InshoreTrafficZone));
    public static string RouteArea_SubmarineTransitLane => Get(nameof(RouteArea_SubmarineTransitLane));
    public static string RouteArea_MarineProtectedArea => Get(nameof(RouteArea_MarineProtectedArea));
    public static string RouteArea_FromFormat => Get(nameof(RouteArea_FromFormat));
    public static string RouteArea_UntilFormat => Get(nameof(RouteArea_UntilFormat));
    public static string RouteArea_RangeFormat => Get(nameof(RouteArea_RangeFormat));
}
