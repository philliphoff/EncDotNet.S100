namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The air classes of the AML Routes, Areas and Limits (RAL) product, in the
/// <see cref="AmlFamily.Air"/> family (AML design handoff frame B2a). The AML
/// 3.0 air trials (FAI, MFI) use the same classes and draw the same way.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Class</term><description>Drawn as</description></listheader>
/// <item><term><c>atsctl</c> ATS route centreline</term><description>Dash-dot line, name at its middle.</description></item>
/// <item><term><c>awaypt</c> air waypoint (3.0)</term><description>Framed open triangle.</description></item>
/// <item><term><c>navaid</c> NAVAID</term><description>Framed hexagon with a centre dot.</description></item>
/// <item><term><c>airres</c> airspace restriction</term><description>Ticked boundary; name with limits as a stacked fraction.</description></item>
/// <item><term><c>ctlasp</c> controlled airspace</term><description>Ticked boundary; name with limits. Line features draw as lines.</description></item>
/// <item><term><c>mexasp</c> military exercise airspace</term><description>Ticked boundary; name with limits. Line features draw as lines.</description></item>
/// </list>
/// </remarks>
public static class RalAirStyles
{
    /// <summary>ATS route centrelines.</summary>
    public static AmlClassStyle AtsRoute { get; } = new("atsctl", AmlFamily.Air, AmlClassKind.Line, "ATS route");

    /// <summary>Air waypoints (AML 3.0).</summary>
    public static AmlClassStyle Waypoint { get; } = new("awaypt", AmlFamily.Air, AmlClassKind.Point, "Air waypoint") { Glyph = AmlGlyph.Waypoint };

    /// <summary>Radio navigation aids.</summary>
    public static AmlClassStyle Navaid { get; } = new("navaid", AmlFamily.Air, AmlClassKind.Point, "NAVAID") { Glyph = AmlGlyph.Navaid };

    /// <summary>Airspace restrictions: danger, restricted and prohibited areas.</summary>
    public static AmlClassStyle AirspaceRestriction { get; } = new("airres", AmlFamily.Air, AmlClassKind.Area, "Airspace restriction") { ShowsLimits = true };

    /// <summary>Controlled airspace.</summary>
    public static AmlClassStyle ControlledAirspace { get; } = new("ctlasp", AmlFamily.Air, AmlClassKind.Area, "Controlled airspace") { ShowsLimits = true };

    /// <summary>Military exercise airspace.</summary>
    public static AmlClassStyle ExerciseAirspace { get; } = new("mexasp", AmlFamily.Air, AmlClassKind.Area, "Military exercise airspace") { ShowsLimits = true };

    /// <summary>The module of the air classes, in legend order.</summary>
    public static IAmlStyleModule Module { get; } = new AmlStyleModule(
        "RAL air",
        [AtsRoute, Waypoint, Navaid, AirspaceRestriction, ControlledAirspace, ExerciseAirspace]);
}
