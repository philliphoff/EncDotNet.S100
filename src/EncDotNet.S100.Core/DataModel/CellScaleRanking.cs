namespace EncDotNet.S100.DataModel;

/// <summary>
/// Ranks overlapping chart cells by scale, finest first. One rule for every
/// consumer that has to choose between cells covering the same place: the
/// display's cross-cell overlap suppression (issue #438), the headless
/// compositor (#859) and the charted depth used by the passage check and the
/// pick report (#938).
/// </summary>
/// <remarks>
/// <para>
/// A cell's ranking scale is its compilation scale denominator when it has one
/// (S-57 DSPM CSCL, Appendix B.1 §7.3.1.1), and otherwise its
/// minimum display scale (S-101 FC §3.1.1 <c>DataCoverage.minimumDisplayScale</c>,
/// the coarsest scale at which the cell is meant to be shown). A smaller
/// denominator is the finer cell. A cell with no ranking scale ranks below every
/// cell that has one.
/// </para>
/// <para>
/// Only a strictly finer cell beats another: cells of the same scale are peers,
/// so consumers combine them (the display draws both; the depth rules take the
/// shoalest).
/// </para>
/// </remarks>
public static class CellScaleRanking
{
    /// <summary>
    /// The scale denominator a cell is ranked by: its compilation scale when
    /// present, otherwise its minimum display scale.
    /// </summary>
    /// <param name="compilationScale">The compilation scale denominator, or <see langword="null"/>.</param>
    /// <param name="minimumDisplayScale">The minimum display scale denominator, or <see langword="null"/>.</param>
    /// <returns>The ranking denominator, or <see langword="null"/> when neither is usable.</returns>
    public static int? RankingScale(int? compilationScale, int? minimumDisplayScale) =>
        compilationScale > 0 ? compilationScale
        : minimumDisplayScale > 0 ? minimumDisplayScale
        : null;

    /// <summary>
    /// Whether a cell ranked at <paramref name="candidate"/> is strictly finer
    /// than one ranked at <paramref name="other"/>. An unranked cell is never
    /// finer, and every ranked cell is finer than an unranked one.
    /// </summary>
    /// <param name="candidate">The candidate cell's ranking scale.</param>
    /// <param name="other">The other cell's ranking scale.</param>
    /// <returns><see langword="true"/> when the candidate is strictly finer.</returns>
    public static bool IsFiner(int? candidate, int? other) =>
        candidate is int c && (other is not int o || c < o);

    /// <summary>
    /// Orders ranking scales finest first, with unranked cells last.
    /// </summary>
    /// <param name="x">The first ranking scale.</param>
    /// <param name="y">The second ranking scale.</param>
    /// <returns>A negative value when <paramref name="x"/> is finer, zero for the same rank.</returns>
    public static int Compare(int? x, int? y) =>
        (x, y) switch
        {
            (int a, int b) => a.CompareTo(b),
            (int, null) => -1,
            (null, int) => 1,
            _ => 0,
        };

    /// <summary>
    /// The cells that share the finest ranking scale among
    /// <paramref name="cells"/>, in their original order.
    /// </summary>
    /// <typeparam name="T">The cell type.</typeparam>
    /// <param name="cells">The candidate cells, for example those covering one point.</param>
    /// <param name="rankingScale">Reads a cell's ranking scale (see <see cref="RankingScale"/>).</param>
    /// <returns>The finest cells; empty when <paramref name="cells"/> is empty.</returns>
    public static IReadOnlyList<T> FinestBand<T>(IEnumerable<T> cells, Func<T, int?> rankingScale)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(rankingScale);

        var finest = new List<T>();
        int? best = null;
        foreach (var cell in cells)
        {
            var rank = rankingScale(cell);
            var order = finest.Count == 0 ? -1 : Compare(rank, best);
            if (order < 0)
            {
                finest.Clear();
                best = rank;
            }

            if (order <= 0)
            {
                finest.Add(cell);
            }
        }

        return finest;
    }
}
