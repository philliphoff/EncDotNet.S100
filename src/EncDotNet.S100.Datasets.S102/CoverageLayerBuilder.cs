using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// Builds a <see cref="CoverageLayer"/> from an S-102 bathymetric coverage
/// and portrayal information.
/// </summary>
public static class CoverageLayerBuilder
{
    /// <summary>
    /// Builds a coverage layer using an explicit list of depth shadings.
    /// </summary>
    /// <param name="coverage">The bathymetric coverage to style.</param>
    /// <param name="shadings">The depth-to-colour mappings to apply.</param>
    public static CoverageLayer Build(BathymetryCoverage coverage, IReadOnlyList<DepthShading> shadings)
    {
        ArgumentNullException.ThrowIfNull(coverage);
        ArgumentNullException.ThrowIfNull(shadings);

        var cells = new CoverageCell[coverage.Values.Length];

        for (int i = 0; i < coverage.Values.Length; i++)
        {
            ref readonly var value = ref coverage.Values[i];
            int shadingIndex = FindShadingIndex(value.Depth, shadings);
            cells[i] = new CoverageCell(value.Depth, value.Uncertainty, shadingIndex);
        }

        return new CoverageLayer
        {
            OriginLatitude = coverage.OriginLatitude,
            OriginLongitude = coverage.OriginLongitude,
            SpacingLatitudinal = coverage.SpacingLatitudinal,
            SpacingLongitudinal = coverage.SpacingLongitudinal,
            NumPointsLatitudinal = coverage.NumPointsLatitudinal,
            NumPointsLongitudinal = coverage.NumPointsLongitudinal,
            Shadings = shadings,
            Cells = cells,
        };
    }

    /// <summary>
    /// Builds a coverage layer using depth shadings derived from a portrayal catalogue.
    /// </summary>
    /// <param name="coverage">The bathymetric coverage to style.</param>
    /// <param name="catalogue">The portrayal catalogue supplying colour and viewing group rules.</param>
    /// <param name="shadings">
    /// Optional explicit depth shadings. When <c>null</c>, the standard IHO depth colour
    /// bands are used as a default.
    /// </param>
    public static CoverageLayer Build(
        BathymetryCoverage coverage,
        PortrayalCatalogue catalogue,
        IReadOnlyList<DepthShading>? shadings = null)
    {
        ArgumentNullException.ThrowIfNull(catalogue);

        // TODO: extract depth shadings from the catalogue's context parameters
        // and colour profiles. For now, fall back to the default IHO bands.
        return Build(coverage, shadings ?? DefaultShadings);
    }

    /// <summary>
    /// Standard IHO depth colour bands commonly used for S-102 portrayal.
    /// </summary>
    public static IReadOnlyList<DepthShading> DefaultShadings { get; } =
    [
        new() { MinDepth = float.NegativeInfinity, MaxDepth = 0,    Color = "#98D898", Label = "Drying" },
        new() { MinDepth = 0,                      MaxDepth = 2,    Color = "#C6EBFF", Label = "0–2 m" },
        new() { MinDepth = 2,                      MaxDepth = 5,    Color = "#A3D1F0", Label = "2–5 m" },
        new() { MinDepth = 5,                      MaxDepth = 10,   Color = "#82B8E0", Label = "5–10 m" },
        new() { MinDepth = 10,                     MaxDepth = 20,   Color = "#619ED0", Label = "10–20 m" },
        new() { MinDepth = 20,                     MaxDepth = 50,   Color = "#4585C0", Label = "20–50 m" },
        new() { MinDepth = 50,                     MaxDepth = 100,  Color = "#2E6DB0", Label = "50–100 m" },
        new() { MinDepth = 100,                    MaxDepth = 200,  Color = "#1B56A0", Label = "100–200 m" },
        new() { MinDepth = 200,                    MaxDepth = 500,  Color = "#0E3F90", Label = "200–500 m" },
        new() { MinDepth = 500,                    MaxDepth = 1000, Color = "#052A7A", Label = "500–1000 m" },
        new() { MinDepth = 1000, MaxDepth = float.PositiveInfinity, Color = "#001960", Label = "> 1000 m" },
    ];

    private static int FindShadingIndex(float depth, IReadOnlyList<DepthShading> shadings)
    {
        for (int i = 0; i < shadings.Count; i++)
        {
            if (depth >= shadings[i].MinDepth && depth < shadings[i].MaxDepth)
                return i;
        }

        return -1;
    }
}
