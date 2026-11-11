namespace EncDotNet.S100.Viewer.Resources;

// Check route's map layer and legend (#959, handoff C1–C8). Kept in their
// own part so concurrent Routes work doesn't collide.
internal static partial class Strings
{
    public static string Map_LegendChip => Get(nameof(Map_LegendChip));
    public static string Map_LegendFindings => Get(nameof(Map_LegendFindings));
    public static string Map_LegendMustReview => Get(nameof(Map_LegendMustReview));
    public static string Map_LegendCheck => Get(nameof(Map_LegendCheck));
    public static string Map_LegendInfo => Get(nameof(Map_LegendInfo));
    public static string Map_LegendSleeve => Get(nameof(Map_LegendSleeve));
    public static string Map_LegendCorridor => Get(nameof(Map_LegendCorridor));
    public static string Map_LegendNoDraft => Get(nameof(Map_LegendNoDraft));
    public static string Tooltip_MapLegendFold => Get(nameof(Tooltip_MapLegendFold));
    public static string Tooltip_MapLegendOpen => Get(nameof(Tooltip_MapLegendOpen));

    // The route check's section of the Legend tab (#1408).
    public static string Legend_RouteCheckSpec => Get(nameof(Legend_RouteCheckSpec));
    public static string Legend_RouteCheckTitle => Get(nameof(Legend_RouteCheckTitle));
    public static string Legend_RouteCheckUkc => Get(nameof(Legend_RouteCheckUkc));
    public static string Legend_RouteCheckFindings => Get(nameof(Legend_RouteCheckFindings));
    public static string Legend_RouteCheckWeather => Get(nameof(Legend_RouteCheckWeather));
}
