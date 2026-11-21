namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The operational classes of the AML Routes, Areas and Limits (RAL) product,
/// in the <see cref="AmlFamily.Operational"/> family (AML design handoff frame
/// B2b).
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Class</term><description>Drawn as</description></listheader>
/// <item><term><c>MIPARE</c> military practice area</term><description>Ticked boundary and name. A chart class (decision 19): the ENC keeps its own symbol, AML adds only the boundary.</description></item>
/// <item><term><c>qroute</c> Q-route leg</term><description>Heavier dash-dot with chevrons in the direction of the leg.</description></item>
/// <item><term><c>turnpt</c> turning point</term><description>Framed ringed dot.</description></item>
/// <item><term><c>patare</c> patrol area</term><description>Ticked boundary and name.</description></item>
/// <item><term><c>intwtr</c> internal waters</term><description>Ticked boundary and name.</description></item>
/// </list>
/// </remarks>
public static class RalOperationalStyles
{
    /// <summary>Military practice areas: a chart class.</summary>
    public static AmlClassStyle PracticeArea { get; } = new("MIPARE", AmlFamily.Operational, AmlClassKind.Area, "Military practice area") { IsChartClass = true };

    /// <summary>Q-route legs.</summary>
    public static AmlClassStyle QRoute { get; } = new("qroute", AmlFamily.Operational, AmlClassKind.Route, "Q-route leg");

    /// <summary>Turning points.</summary>
    public static AmlClassStyle TurningPoint { get; } = new("turnpt", AmlFamily.Operational, AmlClassKind.Point, "Turning point") { Glyph = AmlGlyph.TurningPoint };

    /// <summary>Patrol areas.</summary>
    public static AmlClassStyle PatrolArea { get; } = new("patare", AmlFamily.Operational, AmlClassKind.Area, "Patrol area");

    /// <summary>Internal waters.</summary>
    public static AmlClassStyle InternalWaters { get; } = new("intwtr", AmlFamily.Operational, AmlClassKind.Area, "Internal waters");

    /// <summary>The module of the operational classes, in legend order.</summary>
    public static IAmlStyleModule Module { get; } = new AmlStyleModule(
        "RAL operational",
        [PracticeArea, QRoute, TurningPoint, PatrolArea, InternalWaters]);
}
