namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// Glyphs for AML classes that are also chart classes (AML design handoff
/// decision 19): a chart-like shape drawn inside the AML frame, in the
/// family's ink, so a light or a buoy reads as one but never as the chart's
/// own symbol. SoundCharts' own drawings; nothing comes from the S-52 or the
/// official AML portrayal.
/// </summary>
public static class ChartLikeGlyphs
{
    /// <summary>A filled flare: a light.</summary>
    public static AmlGlyph Light { get; } = new(
        "LIGHT",
        "A filled flare: a light.",
        """<path class="f{ink}" d="M 0 1.9 Q 1.3 -0.2 0 -2.0 Q -1.3 -0.2 0 1.9 Z"/>""");

    /// <summary>A hull with a mast and a flare: a light vessel or light float.</summary>
    public static AmlGlyph LightVessel { get; } = new(
        "LTVESSEL",
        "A hull with a mast and a flare: a light vessel or light float.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" d="M -1.9 0.8 L 1.9 0.8 L 1.2 1.9 L -1.2 1.9 Z M 0 0.8 L 0 -0.6"/><path class="f{ink}" d="M 0 -0.5 Q 0.8 -1.3 0 -2.1 Q -0.8 -1.3 0 -0.5 Z"/>""");

    /// <summary>A pillar buoy's outline on its waterline: a buoy.</summary>
    public static AmlGlyph Buoy { get; } = new(
        "BUOY",
        "A pillar buoy on its waterline: a buoy.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" d="M -1.3 1.4 L 1.3 1.4 L 0.5 -1.8 L -0.5 -1.8 Z M -2.0 1.9 L 2.0 1.9"/>""");

    /// <summary>A stake on a base with a topmark: a beacon.</summary>
    public static AmlGlyph Beacon { get; } = new(
        "BEACON",
        "A stake on a base with a topmark: a beacon.",
        """<path class="s{ink} f0" stroke-width="0.5" stroke-linecap="round" d="M 0 1.9 L 0 -1.0 M -1.3 1.9 L 1.3 1.9"/><circle class="f{ink}" cx="0" cy="-1.5" r="0.55"/>""");

    /// <summary>A tower on a base: a landmark.</summary>
    public static AmlGlyph Landmark { get; } = new(
        "LANDMARK",
        "A tower on a base: a landmark.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" d="M -1.0 1.9 L -0.45 -1.8 L 0.45 -1.8 L 1.0 1.9 Z M -1.7 1.9 L 1.7 1.9"/>""");

    /// <summary>A dot with two waves above it: a radio station.</summary>
    public static AmlGlyph Radio { get; } = new(
        "RADIO",
        "A dot with two waves above it: a radio station.",
        """<circle class="f{ink}" cx="0" cy="1.0" r="0.55"/><path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" d="M -0.99 0.01 A 1.4 1.4 0 0 1 0.99 0.01 M -1.7 -0.7 A 2.4 2.4 0 0 1 1.7 -0.7"/>""");

    /// <summary>A flag on a staff: a station, a calling-in point or a checkpoint.</summary>
    public static AmlGlyph Flag { get; } = new(
        "FLAG",
        "A flag on a staff: a station or reporting point.",
        """<path class="s{ink} f0" stroke-width="0.5" stroke-linecap="round" d="M -1.1 1.9 L -1.1 -1.9"/><path class="f{ink}" d="M -1.1 -1.9 L 1.7 -1.1 L -1.1 -0.3 Z"/>""");

    /// <summary>A shank with a stock and arms: a harbour facility.</summary>
    public static AmlGlyph Harbour { get; } = new(
        "HARBOUR",
        "An anchor: a harbour facility.",
        """<circle class="s{ink} f0" stroke-width="0.4" cx="0" cy="-1.5" r="0.45"/><path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" d="M 0 -1.05 L 0 1.8 M -0.9 -0.4 L 0.9 -0.4 M -1.6 0.7 Q 0 2.5 1.6 0.7"/>""");

    /// <summary>A small square: a site, building or facility on land.</summary>
    public static AmlGlyph Site { get; } = new(
        "SITE",
        "A small square: a site, building or facility.",
        """<rect class="s{ink} f0" stroke-width="0.5" x="-1.3" y="-1.3" width="2.6" height="2.6"/>""");

    /// <summary>A filled dot: an islet or a small land area.</summary>
    public static AmlGlyph Islet { get; } = new(
        "ISLET",
        "A filled dot: an islet.",
        """<circle class="f{ink}" cx="0" cy="0" r="1.0"/>""");

    /// <summary>A square with a centre dot: an offshore platform.</summary>
    public static AmlGlyph Platform { get; } = new(
        "PLATFORM",
        "A square with a centre dot: an offshore platform or production site.",
        """<rect class="s{ink} f0" stroke-width="0.5" x="-1.6" y="-1.6" width="3.2" height="3.2"/><circle class="f{ink}" cx="0" cy="0" r="0.55"/>""");

    /// <summary>A fish: a fishing facility or marine farm.</summary>
    public static AmlGlyph Fish { get; } = new(
        "FISH",
        "A fish: a fishing facility or marine farm.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" d="M -1.9 0 Q -0.4 -1.5 1.0 0 Q -0.4 1.5 -1.9 0 Z M 1.0 0 L 2.0 -0.9 L 2.0 0.9 Z"/>""");

    /// <summary>A short pipe: a pipeline at a point.</summary>
    public static AmlGlyph Pipe { get; } = new(
        "PIPE",
        "A short pipe: a pipeline at a point.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" d="M -1.9 -0.6 L 1.9 -0.6 M -1.9 0.6 L 1.9 0.6 M -1.9 -1.1 L -1.9 1.1 M 1.9 -1.1 L 1.9 1.1"/>""");

    /// <summary>A diamond with a waterline: a tide or tidal stream station.</summary>
    public static AmlGlyph Tide { get; } = new(
        "TIDE",
        "A diamond with a waterline: a tide or tidal stream station.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" d="M 0 -2.0 L 1.6 0 L 0 2.0 L -1.6 0 Z M -1.6 0 L 1.6 0"/>""");

    /// <summary>A compass needle, its north half filled: a magnetic anomaly or variation.</summary>
    public static AmlGlyph Magnetic { get; } = new(
        "MAG",
        "A compass needle: a magnetic anomaly or variation.",
        """<path class="s{ink} f0" stroke-width="0.4" stroke-linejoin="round" d="M 0 -2.0 L 0.75 0 L 0 2.0 L -0.75 0 Z"/><path class="f{ink}" d="M 0 -2.0 L 0.75 0 L -0.75 0 Z"/>""");

    /// <summary>Three strands: weed or kelp.</summary>
    public static AmlGlyph Weed { get; } = new(
        "WEED",
        "Three strands: weed or kelp.",
        """<path class="s{ink} f0" stroke-width="0.4" stroke-linecap="round" d="M -1.2 1.9 Q -1.9 0 -1.2 -1.9 M 0 1.9 Q -0.7 0 0 -1.9 M 1.2 1.9 Q 0.5 0 1.2 -1.9"/>""");

    /// <summary>A circle crossed by a runway: an airfield.</summary>
    public static AmlGlyph Airfield { get; } = new(
        "AIRFIELD",
        "A circle crossed by a runway: an airport or airfield.",
        """<circle class="s{ink} f0" stroke-width="0.45" cx="0" cy="0" r="1.5"/><path class="s{ink} f0" stroke-width="0.7" stroke-linecap="round" d="M -2.1 0.9 L 2.1 -0.9"/>""");

    /// <summary>A runway's outline with its centreline: a runway.</summary>
    public static AmlGlyph Runway { get; } = new(
        "RUNWAY",
        "A runway's outline: a runway.",
        """<rect class="s{ink} f0" stroke-width="0.45" x="-0.7" y="-2.0" width="1.4" height="4.0"/><path class="s{ink} f0" stroke-width="0.3" stroke-dasharray="0.5 0.4" d="M 0 -1.6 L 0 1.6"/>""");

    /// <summary>A circle with a sweep: a radar station.</summary>
    public static AmlGlyph Radar { get; } = new(
        "RADAR",
        "A circle with a sweep: a radar station.",
        """<circle class="s{ink} f0" stroke-width="0.45" cx="0" cy="0" r="1.7"/><path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" d="M 0 0 L 1.2 -1.2"/><circle class="f{ink}" cx="0" cy="0" r="0.45"/>""");
}
