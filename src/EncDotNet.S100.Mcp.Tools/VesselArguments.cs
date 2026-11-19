using System.ComponentModel;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// The vessel and its safety settings, as the route tools take them: the
/// draft and allowances the clearance is judged against, the safety contour
/// and depth the chart check uses, and the speed the ETAs come from.
/// </summary>
/// <remarks>
/// One shape for every tool that judges water against a boat
/// (<c>check_route</c> now, route planning later), so a plan and its check
/// use the same values. Each value is optional: a host route keeps its own
/// leg values for anything left unset.
/// </remarks>
/// <param name="DraftMeters">A draft for every leg; with one, the clearance check runs and dangers are also judged against each leg's keel.</param>
/// <param name="SquatMeters">A fixed squat allowance for every leg.</param>
/// <param name="RequiredUkcMeters">A required under-keel clearance for every leg.</param>
/// <param name="SafetyContourMeters">The safety contour for the depth checks; with <paramref name="SafetyDepthMeters"/>, turns them on.</param>
/// <param name="SafetyDepthMeters">The safety depth for the sounding check.</param>
/// <param name="SpeedKnots">A speed over ground for every leg; speeds give the ETAs.</param>
public sealed record VesselArguments(
    [property: Description("Draft in metres, for every leg (a route's own otherwise). With a draft on every leg the clearance check runs, and each leg's dangers are also judged against its draft + squat + required UKC.")] double? DraftMeters = null,
    [property: Description("Fixed squat allowance in metres, for every leg (a route's own otherwise; default 0).")] double? SquatMeters = null,
    [property: Description("Required under-keel clearance in metres, for every leg (a route's own otherwise). Unset checks against 0 m and sets clearance.noMarginSet.")] double? RequiredUkcMeters = null,
    [property: Description("Safety contour in metres. With safetyDepthMeters, turns on the depth checks: depth, dredged and drying areas and S-102 cells shallower than it, and charted dangers. SoundCharts' findings list uses the mariner's settings (get_mariner_settings).")] double? SafetyContourMeters = null,
    [property: Description("Safety depth in metres, for soundings. Goes with safetyContourMeters.")] double? SafetyDepthMeters = null,
    [property: Description("Speed over ground in knots, for every leg (a route's own otherwise). Speeds give the ETAs that warnings, seasonal areas and S-104 water levels are judged at. S-111 currents are not applied.")] double? SpeedKnots = null)
{
    /// <summary>
    /// The first invalid value, as <c>(name, problem)</c> with the name
    /// relative to this object, or <c>null</c> when every value is valid.
    /// </summary>
    /// <returns>The problem, or <c>null</c>.</returns>
    public (string Name, string Problem)? Validate()
    {
        if ((SafetyContourMeters is null) != (SafetyDepthMeters is null)) return ("safetyDepthMeters", "safetyContourMeters and safetyDepthMeters go together");
        if (Arguments.Negative(SafetyContourMeters)) return ("safetyContourMeters", "must be zero or more metres");
        if (Arguments.Negative(SafetyDepthMeters)) return ("safetyDepthMeters", "must be zero or more metres");
        if (Arguments.NotPositive(DraftMeters)) return ("draftMeters", "must be a positive number of metres");
        if (Arguments.NotPositive(SpeedKnots)) return ("speedKnots", "must be a positive number of knots");
        if (Arguments.Negative(SquatMeters)) return ("squatMeters", "must be zero or more metres");
        if (Arguments.Negative(RequiredUkcMeters)) return ("requiredUkcMeters", "must be zero or more metres");
        return null;
    }
}

/// <summary>Range checks shared by the grouped tool arguments.</summary>
internal static class Arguments
{
    /// <summary>Set and not a finite number of zero or more.</summary>
    public static bool Negative(double? value) => value is { } x && (!double.IsFinite(x) || x < 0);

    /// <summary>Set and not a finite positive number.</summary>
    public static bool NotPositive(double? value) => value is { } x && (!double.IsFinite(x) || x <= 0);
}
