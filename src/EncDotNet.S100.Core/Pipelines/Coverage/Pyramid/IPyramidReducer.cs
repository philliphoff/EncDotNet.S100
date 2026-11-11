namespace EncDotNet.S100.Pipelines.Coverage.Pyramid;

/// <summary>
/// Reduces a 2×2 (or partial) window of source-level cells to a single
/// coarser-level cell value, respecting the coverage's NODATA sentinel
/// (S-100 Part 10c §11 fill values; issue #486).
/// </summary>
/// <remarks>
/// <para>
/// Reducers are field-specific: S-102 <c>depth</c> uses a shoal-biased
/// <see cref="MinReducer"/> so pyramid cells are never <em>safer</em>
/// than any base cell they pool (never a shallower depth reported as
/// deeper); S-102 <c>uncertainty</c> uses <see cref="MaxReducer"/> to
/// report the worst-case pooled uncertainty; S-104 water-level heights
/// use <see cref="MeanReducer"/>; S-111 speed/direction pairs use
/// <see cref="VectorMeanReducer"/> which averages the (u, v)
/// decomposition so the resultant is correct at the 0°/360° branch
/// cut. The NODATA sentinel is passed as a value (not <c>NaN</c>) to
/// match the S-102 <c>1_000_000f</c> fill convention.
/// </para>
/// <para>
/// Windows near the base grid's east/south edges may span fewer than
/// four source cells when a dimension has an odd length; implementers
/// must therefore accept a variable-length span rather than a fixed
/// 4-tuple. See <see cref="CoveragePyramidBuilder"/> for how windows
/// are enumerated.
/// </para>
/// </remarks>
public interface IPyramidReducer
{
    /// <summary>
    /// Reduces a window of up to four source cells to one coarser-level
    /// cell value.
    /// </summary>
    /// <param name="window">
    /// The source cells in the current 2×2 window, in row-major order.
    /// Length is 1..4 (edge windows may be short).
    /// </param>
    /// <param name="noDataValue">
    /// The coverage's NODATA sentinel (e.g. S-102 <c>1_000_000f</c>).
    /// Source cells equal to this value are excluded from the
    /// reduction; if <em>every</em> cell in the window is NODATA the
    /// reducer returns the sentinel too.
    /// </param>
    float Reduce(ReadOnlySpan<float> window, float noDataValue);
}

/// <summary>
/// Marker interface for reducers that operate on a paired (u, v)
/// decomposition rather than a single scalar field. Used by
/// <see cref="VectorMeanReducer"/> for S-111 surface currents.
/// </summary>
/// <remarks>
/// A vector reducer is not a drop-in for <see cref="IPyramidReducer"/>
/// because the target and paired-field spans must be pooled together
/// (mean of directions computed via the pooled sin/cos). Coverage
/// sources that carry a vector-magnitude+angle pair invoke this
/// reducer explicitly (see <see cref="CoveragePyramidBuilder"/>).
/// </remarks>
public interface IVectorPyramidReducer
{
    /// <summary>
    /// Reduces two paired windows to a single (speed, direction) pair.
    /// </summary>
    /// <param name="speeds">Speed samples for this window (row-major).</param>
    /// <param name="directions">Direction samples for this window, in degrees clockwise from north (S-111 §10.5.1).</param>
    /// <param name="noDataSpeed">NODATA sentinel for the speed field.</param>
    /// <param name="noDataDirection">NODATA sentinel for the direction field.</param>
    /// <returns>
    /// The pooled speed and direction. NODATA in either paired field
    /// excludes the cell from the reduction; if every cell is NODATA
    /// in either field the corresponding NODATA sentinel is returned.
    /// </returns>
    (float Speed, float Direction) Reduce(
        ReadOnlySpan<float> speeds,
        ReadOnlySpan<float> directions,
        float noDataSpeed,
        float noDataDirection);
}
