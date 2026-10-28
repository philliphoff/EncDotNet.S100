using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// One row of a colour fill's legend: a band colour and its label, e.g.
/// <c>#BDB2E3</c> and <c>"2–2.5 m"</c>. A viewer shows a
/// <see cref="GridCoverageSubLayer.Legend"/> next to the fill's layer stack
/// entry; the headless renderer doesn't draw legends.
/// </summary>
/// <param name="Color">The band colour as a hex string, resolved against the palette the fill was drawn in.</param>
/// <param name="Label">The band label, e.g. <c>"Force 3, 7–10 kn"</c>.</param>
public sealed record CoverageLegendEntry(string Color, string Label)
{
    /// <summary>
    /// The legend of a colour scheme: one entry per band, lowest first, or
    /// empty when any band has no <see cref="ColorBand.Label"/> (a legend with
    /// gaps would mislead).
    /// </summary>
    /// <param name="scheme">The colour scheme, with colours already resolved.</param>
    public static IReadOnlyList<CoverageLegendEntry> FromScheme(CoverageColorScheme scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        var entries = new CoverageLegendEntry[scheme.Bands.Count];
        for (int i = 0; i < entries.Length; i++)
        {
            var band = scheme.Bands[i];
            if (string.IsNullOrWhiteSpace(band.Label))
                return [];
            entries[i] = new CoverageLegendEntry(band.Color, band.Label);
        }

        return entries;
    }
}
