using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// Default portrayal catalogue for S-104 Water Level data.
/// Provides a standalone diverging height-band heatmap with mariner
/// Day / Dusk / Night palette variants.
/// </summary>
/// <remarks>
/// <para>
/// S-104 Ed 2.0.0 does <b>not</b> define an official portrayal
/// catalogue — the data is intended primarily for water-level
/// adjustment on ECDIS. The Day / Dusk / Night band tables exposed by
/// this catalogue are <b>synthesised for viewer parity</b> with the
/// other coverage products (S-102, S-111); the Day palette mirrors
/// ColorBrewer-style diverging blue ↔ green; Dusk is a desaturated,
/// dimmed variant; Night follows ECDIS night-mode conventions
/// (deep navy negatives, dim olive/green positives, low luminance).
/// Replace with an official IHO portrayal catalogue if one is published.
/// </para>
/// <para>
/// Cells whose value equals <see cref="S104CoverageSource.FillValue"/>
/// are painted with the active palette's <see cref="CoverageColorScheme.NoDataColor"/>
/// (transparent for Day; dim greys for Dusk / Night).
/// </para>
/// </remarks>
public class S104PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    /// <summary>
    /// Day-palette diverging blue (negative / below datum) → neutral
    /// → green (positive / above datum) bands, preserved byte-for-byte
    /// from the pre-PR-H hand-coded table for backward compatibility.
    /// </summary>
    private static readonly ColorBand[] DayBands =
    [
        new() { MinValue = -5.0f,  MaxValue = -2.0f, Color = "#08519C", Label = "\u22125 to \u22122 m" },
        new() { MinValue = -2.0f,  MaxValue = -1.0f, Color = "#3182BD", Label = "\u22122 to \u22121 m" },
        new() { MinValue = -1.0f,  MaxValue = -0.5f, Color = "#6BAED6", Label = "\u22121 to \u22120.5 m" },
        new() { MinValue = -0.5f,  MaxValue = -0.2f, Color = "#9ECAE1", Label = "\u22120.5 to \u22120.2 m" },
        new() { MinValue = -0.2f,  MaxValue =  0.0f, Color = "#C6DBEF", Label = "\u22120.2 to 0 m" },
        new() { MinValue =  0.0f,  MaxValue =  0.2f, Color = "#C7E9C0", Label = "0 to 0.2 m" },
        new() { MinValue =  0.2f,  MaxValue =  0.5f, Color = "#A1D99B", Label = "0.2 to 0.5 m" },
        new() { MinValue =  0.5f,  MaxValue =  1.0f, Color = "#74C476", Label = "0.5 to 1 m" },
        new() { MinValue =  1.0f,  MaxValue =  2.0f, Color = "#31A354", Label = "1 to 2 m" },
        new() { MinValue =  2.0f,  MaxValue =  5.0f, Color = "#006D2C", Label = "2 to 5 m" },
    ];

    /// <summary>
    /// Dusk palette: Day values converted via HLS with saturation × 0.70
    /// and lightness × 0.85, yielding the same diverging hue family
    /// with reduced punch suitable for low-light bridge use.
    /// </summary>
    private static readonly ColorBand[] DuskBands =
    [
        new() { MinValue = -5.0f,  MaxValue = -2.0f, Color = "#1A4572", Label = "\u22125 to \u22122 m" },
        new() { MinValue = -2.0f,  MaxValue = -1.0f, Color = "#3C6C8F", Label = "\u22122 to \u22121 m" },
        new() { MinValue = -1.0f,  MaxValue = -0.5f, Color = "#5994B7", Label = "\u22121 to \u22120.5 m" },
        new() { MinValue = -0.5f,  MaxValue = -0.2f, Color = "#81ADC5", Label = "\u22120.5 to \u22120.2 m" },
        new() { MinValue = -0.2f,  MaxValue =  0.0f, Color = "#9EBAD5", Label = "\u22120.2 to 0 m" },
        new() { MinValue =  0.0f,  MaxValue =  0.2f, Color = "#A4CE9C", Label = "0 to 0.2 m" },
        new() { MinValue =  0.2f,  MaxValue =  0.5f, Color = "#86BD80", Label = "0.2 to 0.5 m" },
        new() { MinValue =  0.5f,  MaxValue =  1.0f, Color = "#62A764", Label = "0.5 to 1 m" },
        new() { MinValue =  1.0f,  MaxValue =  2.0f, Color = "#387C4D", Label = "1 to 2 m" },
        new() { MinValue =  2.0f,  MaxValue =  5.0f, Color = "#0E4F28", Label = "2 to 5 m" },
    ];

    /// <summary>
    /// Night palette: ECDIS night-mode aesthetic — dark navy below
    /// datum, dim olive/green above, all with luminance well under 0.2
    /// so the surface does not destroy the mariner's dark adaptation.
    /// Hand-picked because no spec exists.
    /// </summary>
    private static readonly ColorBand[] NightBands =
    [
        new() { MinValue = -5.0f,  MaxValue = -2.0f, Color = "#050D1F", Label = "\u22125 to \u22122 m" },
        new() { MinValue = -2.0f,  MaxValue = -1.0f, Color = "#0B1A33", Label = "\u22122 to \u22121 m" },
        new() { MinValue = -1.0f,  MaxValue = -0.5f, Color = "#13253E", Label = "\u22121 to \u22120.5 m" },
        new() { MinValue = -0.5f,  MaxValue = -0.2f, Color = "#1A2F4A", Label = "\u22120.5 to \u22120.2 m" },
        new() { MinValue = -0.2f,  MaxValue =  0.0f, Color = "#243A55", Label = "\u22120.2 to 0 m" },
        new() { MinValue =  0.0f,  MaxValue =  0.2f, Color = "#1F2E1A", Label = "0 to 0.2 m" },
        new() { MinValue =  0.2f,  MaxValue =  0.5f, Color = "#28381F", Label = "0.2 to 0.5 m" },
        new() { MinValue =  0.5f,  MaxValue =  1.0f, Color = "#324324", Label = "0.5 to 1 m" },
        new() { MinValue =  1.0f,  MaxValue =  2.0f, Color = "#3A4D28", Label = "1 to 2 m" },
        new() { MinValue =  2.0f,  MaxValue =  5.0f, Color = "#43582C", Label = "2 to 5 m" },
    ];

    /// <summary>
    /// Per-palette band table. <see cref="SwitchPalette"/> selects
    /// which entry is returned by <see cref="ResolveColorScheme"/>.
    /// </summary>
    private static readonly IReadOnlyDictionary<PaletteType, IReadOnlyList<ColorBand>> Bands =
        new Dictionary<PaletteType, IReadOnlyList<ColorBand>>
        {
            [PaletteType.Day] = DayBands,
            [PaletteType.Dusk] = DuskBands,
            [PaletteType.Night] = NightBands,
        };

    /// <summary>
    /// Per-palette no-data fill colour. Day stays transparent (legacy
    /// behaviour); Dusk uses a dim cool grey; Night uses a darker dim
    /// grey to keep the cell visible but unobtrusive against the dark
    /// palette. All choices are synthesised — no spec defines them.
    /// </summary>
    private static readonly IReadOnlyDictionary<PaletteType, string> NoDataColors =
        new Dictionary<PaletteType, string>
        {
            [PaletteType.Day] = "#00000000",
            [PaletteType.Dusk] = "#4A4A4AFF",
            [PaletteType.Night] = "#1A1A1AFF",
        };

    private PaletteType _activePaletteType = PaletteType.Day;

    /// <summary>Gets the S-100 product specification identifier for this catalogue.</summary>
    public SpecRef Spec => new("S-104", default);

    /// <summary>Gets the edition of the portrayal catalogue.</summary>
    public string Edition => "2.0.0";

    /// <summary>Gets the currently active color palette.</summary>
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <summary>
    /// Switches the active palette. Unlike the previous implementation
    /// (which returned a stub <see cref="ColorPalette.FromType"/> and
    /// left the band table unchanged), this actually swaps the band
    /// table returned by <see cref="ResolveColorScheme"/>.
    /// </summary>
    public void SwitchPalette(PaletteType type)
    {
        _activePaletteType = type;
        ActivePalette = ColorPalette.FromType(type);
    }

    /// <summary>
    /// Returns a <see cref="CoverageColorScheme"/> for the
    /// <c>waterLevelHeight</c> field using the band table for the
    /// currently active palette. The scheme's
    /// <see cref="CoverageColorScheme.NoDataColor"/> is populated from
    /// the per-palette no-data table so the renderer paints S-104 fill
    /// cells (<see cref="S104CoverageSource.FillValue"/>).
    /// </summary>
    public CoverageColorScheme ResolveColorScheme(MarinerSettings settings)
    {
        var bands = Bands.TryGetValue(_activePaletteType, out var palette) ? palette : DayBands;
        var noData = NoDataColors.TryGetValue(_activePaletteType, out var hex) ? hex : "#00000000";

        return new CoverageColorScheme
        {
            FieldName = "waterLevelHeight",
            Bands = bands,
            NoDataColor = noData,
        };
    }

    /// <summary>S-104 does not define contours; returns an empty list.</summary>
    public IReadOnlyList<ContourStyle> Contours => [];
}
