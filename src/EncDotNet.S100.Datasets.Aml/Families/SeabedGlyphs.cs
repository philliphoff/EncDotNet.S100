namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The glyphs of the seabed family (AML design handoff decisions 17, 19 and
/// 20, frame B2c). SoundCharts' own, except the mine, which follows the
/// MIL-STD-2525C sea-mine shape; nothing comes from the restricted official
/// AML portrayal, and APP-6 isn't used.
/// </summary>
public static class SeabedGlyphs
{
    /// <summary>A contact: an open circle, framed.</summary>
    public static AmlGlyph Contact { get; } = new(
        "CONTACT",
        "An open circle: a seabed contact.",
        """<circle class="s{ink} f0" stroke-width="0.5" cx="0" cy="0" r="1.7"/>""");

    /// <summary>
    /// A mine or mine-like contact: a filled circle with four spikes, after the
    /// MIL-STD-2525C sea-mine shape, framed.
    /// </summary>
    public static AmlGlyph Mine { get; } = new(
        "MINE",
        "A filled circle with four spikes, after the MIL-STD-2525C sea mine: a mine or mine-like contact.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" d="M 0 -1.2 L 0 -2.2 M 1.2 0 L 2.2 0 M 0 1.2 L 0 2.2 M -1.2 0 L -2.2 0"/><circle class="f{ink}" cx="0" cy="0" r="1.25"/>""");

    /// <summary>A sensor anomaly: a dashed diamond, framed.</summary>
    public static AmlGlyph Anomaly { get; } = new(
        "ANOMALY",
        "A dashed diamond: a sensor anomaly.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-dasharray="0.7 0.45" stroke-linejoin="round" d="M 0 -2.0 L 2.0 0 L 0 2.0 L -2.0 0 Z"/>""");

    /// <summary>
    /// A scour: three short parallel strokes on a halo, unframed, north–south
    /// as drawn; the portrayal turns them to the scour's orientation.
    /// </summary>
    public static AmlGlyph Scour { get; } = new(
        "SCOUR",
        "Three short parallel strokes: a scour, along its orientation.",
        """<path class="s{halo} f0" stroke-width="1.1" stroke-linecap="round" d="M -0.9 -1.5 L -0.9 1.5 M 0 -2.0 L 0 2.0 M 0.9 -1.5 L 0.9 1.5"/><path class="s{ink} f0" stroke-width="0.4" stroke-linecap="round" d="M -0.9 -1.5 L -0.9 1.5 M 0 -2.0 L 0 2.0 M 0.9 -1.5 L 0.9 1.5"/>""")
    {
        Framed = false,
    };

    /// <summary>
    /// The ring round a standard chart class's object, such as an LBO wreck:
    /// the dash-dot signature on a halo, unframed, meaning "AML details on
    /// pick" (decision 19). The chart symbol inside comes from the ENC.
    /// </summary>
    public static AmlGlyph DetailsRing { get; } = new(
        "RING",
        "A dash-dot ring: AML details on pick, round a chart symbol.",
        """<circle class="s{halo} f0" stroke-width="1.0" cx="0" cy="0" r="3.4"/><circle class="s{ink} f0" stroke-width="0.4" stroke-dasharray="1.8 0.5 0.35 0.5" cx="0" cy="0" r="3.4"/>""")
    {
        Framed = false,
    };

    /// <summary>
    /// A cluster bubble: a circle in the halo with an ink rim and a dotted
    /// outer ring, unframed; the count is written in it (decision 20).
    /// </summary>
    public static AmlGlyph Cluster { get; } = new(
        "CLUSTER",
        "A bubble with a dotted outer ring: a cluster of points, its count inside.",
        """<circle class="s{halo} f0" stroke-width="1.2" cx="0" cy="0" r="4.0"/><circle class="f{halo} s{ink}" stroke-width="0.4" cx="0" cy="0" r="3.7"/><circle class="s{ink} f0" stroke-width="0.25" stroke-dasharray="0.25 0.45" stroke-linecap="round" cx="0" cy="0" r="4.5"/>""")
    {
        Framed = false,
    };
}
