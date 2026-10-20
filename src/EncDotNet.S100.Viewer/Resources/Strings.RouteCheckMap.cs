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
}
