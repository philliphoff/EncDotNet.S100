using EncDotNet.S100.Rendering.Engine;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The overscale inputs (issue #441) of the cells one map's session draws.
/// </summary>
internal static class MapOverscaleCells
{
    /// <summary>
    /// Every dataset <paramref name="session"/> draws that declares a coverage
    /// and a compilation scale. A dataset hidden in that map isn't drawing, so
    /// it isn't listed (split map step 2 §2.6).
    /// </summary>
    /// <param name="session">The map's session.</param>
    /// <returns>The cells, in paint order.</returns>
    public static IReadOnlyList<OverscaleCellInput> Of(IMapDatasetSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        List<OverscaleCellInput>? cells = null;
        foreach (var snapshot in session.GetDatasets())
        {
            if (snapshot.MaximumDisplayScale is not int compilationScale
                || compilationScale <= 0
                || !snapshot.IsDrawing
                || snapshot.CoverageGeometry is not { IsEmpty: false } coverage)
            {
                continue;
            }

            (cells ??= []).Add(new OverscaleCellInput
            {
                Name = snapshot.Dataset.Name,
                Coverage = coverage,
                CompilationScaleDenominator = compilationScale,
            });
        }

        return (IReadOnlyList<OverscaleCellInput>?)cells ?? [];
    }
}
