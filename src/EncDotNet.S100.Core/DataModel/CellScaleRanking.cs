namespace EncDotNet.S100.DataModel;

/// <summary>
/// Where a chart cell ranks among overlapping cells: its <see cref="Scale"/>
/// denominator, and for a cell ranked by its minimum display scale, its finest
/// intended display scale as a <see cref="TieBreak"/> between cells of the same
/// <see cref="Scale"/>. Build one with <see cref="CellScaleRanking.Rank"/>.
/// </summary>
/// <param name="Scale">
/// The ranking scale denominator: the compilation scale when the cell has one,
/// otherwise its minimum display scale. A smaller denominator is the finer cell.
/// </param>
/// <param name="TieBreak">
/// The cell's finest intended display scale denominator (S-101 FC §3.1.1
/// <c>DataCoverage.maximumDisplayScale</c>), or <see langword="null"/> when the
/// cell is ranked by its compilation scale or declares none. Only compared
/// between cells of the same <see cref="Scale"/>.
/// </param>
public readonly record struct CellRank(int Scale, int? TieBreak = null);

/// <summary>
/// Ranks overlapping chart cells by scale, finest first. One rule for every
/// consumer that has to choose between cells covering the same place: the
/// display's cross-cell overlap suppression (issue #438), the headless
/// compositor (#859), the lazy loader and the charted depth used by the
/// passage check and the pick report (#938).
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
/// S-101 producers often give cells of different navigational purpose the same
/// minimum display scale: the IC-ENC Cowes berthing cell 101GB00602793
/// (1:22 000 to 1:3 500) and the harbour cell 101GB00502038 (1:22 000 to
/// 1:12 000) overlap, and ranking them as peers draws both, doubling every
/// label. So between cells ranked by the same minimum display scale, the one
/// with the finer maximum display scale wins; a cell that declares no maximum
/// ranks below one that does.
/// </para>
/// <para>
/// Only a strictly finer cell beats another: cells of the same rank are peers,
/// so consumers combine them (the display draws both; the depth rules take the
/// shoalest).
/// </para>
/// </remarks>
public static class CellScaleRanking
{
    /// <summary>
    /// A cell's rank: its compilation scale when present, otherwise its minimum
    /// display scale with its maximum display scale as the tie-break.
    /// </summary>
    /// <param name="compilationScale">The compilation scale denominator, or <see langword="null"/>.</param>
    /// <param name="minimumDisplayScale">The minimum display scale denominator, or <see langword="null"/>.</param>
    /// <param name="maximumDisplayScale">
    /// The maximum (finest) display scale denominator, or <see langword="null"/>.
    /// Ignored when the cell is ranked by its compilation scale, or when it is not
    /// finer than <paramref name="minimumDisplayScale"/>.
    /// </param>
    /// <returns>The rank, or <see langword="null"/> when neither scale is usable.</returns>
    public static CellRank? Rank(int? compilationScale, int? minimumDisplayScale, int? maximumDisplayScale = null)
    {
        if (compilationScale is int compilation && compilation > 0)
            return new CellRank(compilation);

        if (minimumDisplayScale is not int minimum || minimum <= 0)
            return null;

        return new CellRank(
            minimum,
            maximumDisplayScale is int maximum && maximum > 0 && maximum < minimum ? maximum : null);
    }

    /// <summary>
    /// Whether a cell ranked at <paramref name="candidate"/> is strictly finer
    /// than one ranked at <paramref name="other"/>. An unranked cell is never
    /// finer, and every ranked cell is finer than an unranked one.
    /// </summary>
    /// <param name="candidate">The candidate cell's rank.</param>
    /// <param name="other">The other cell's rank.</param>
    /// <returns><see langword="true"/> when the candidate is strictly finer.</returns>
    public static bool IsFiner(CellRank? candidate, CellRank? other) =>
        candidate is not null && Compare(candidate, other) < 0;

    /// <summary>
    /// Orders ranks finest first: by <see cref="CellRank.Scale"/>, then by
    /// <see cref="CellRank.TieBreak"/> (a cell without one after a cell with
    /// one), with unranked cells last.
    /// </summary>
    /// <param name="x">The first rank.</param>
    /// <param name="y">The second rank.</param>
    /// <returns>A negative value when <paramref name="x"/> is finer, zero for the same rank.</returns>
    public static int Compare(CellRank? x, CellRank? y) =>
        (x, y) switch
        {
            (CellRank a, CellRank b) when a.Scale != b.Scale => a.Scale.CompareTo(b.Scale),
            (CellRank a, CellRank b) => CompareTieBreak(a.TieBreak, b.TieBreak),
            (CellRank, null) => -1,
            (null, CellRank) => 1,
            _ => 0,
        };

    private static int CompareTieBreak(int? x, int? y) =>
        (x, y) switch
        {
            (int a, int b) => a.CompareTo(b),
            (int, null) => -1,
            (null, int) => 1,
            _ => 0,
        };

    /// <summary>
    /// The cells that share the finest rank among <paramref name="cells"/>, in
    /// their original order.
    /// </summary>
    /// <typeparam name="T">The cell type.</typeparam>
    /// <param name="cells">The candidate cells, for example those covering one point.</param>
    /// <param name="rank">Reads a cell's rank (see <see cref="Rank"/>).</param>
    /// <returns>The finest cells; empty when <paramref name="cells"/> is empty.</returns>
    public static IReadOnlyList<T> FinestBand<T>(IEnumerable<T> cells, Func<T, CellRank?> rank)
    {
        ArgumentNullException.ThrowIfNull(cells);
        ArgumentNullException.ThrowIfNull(rank);

        var finest = new List<T>();
        CellRank? best = null;
        foreach (var cell in cells)
        {
            var cellRank = rank(cell);
            var order = finest.Count == 0 ? -1 : Compare(cellRank, best);
            if (order < 0)
            {
                finest.Clear();
                best = cellRank;
            }

            if (order <= 0)
            {
                finest.Add(cell);
            }
        }

        return finest;
    }
}
