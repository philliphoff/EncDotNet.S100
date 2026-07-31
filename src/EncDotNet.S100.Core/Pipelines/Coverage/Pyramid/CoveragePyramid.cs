namespace EncDotNet.S100.Pipelines.Coverage.Pyramid;

/// <summary>
/// An in-memory coverage overview pyramid: a sequence of downsampled
/// grids keyed by level index, one flat row-major <c>float[]</c> per
/// (level, field name) pair (S-100 Part 10c HDF5 grids; issue #486).
/// </summary>
/// <remarks>
/// <para>
/// Level 0 is <em>not</em> stored — the base grid is already resident
/// in the coverage source. Level 1 is the first downsampled level
/// (half-resolution in each axis, quarter the cells); level 2 is
/// quarter-resolution, and so on.
/// </para>
/// <para>
/// Storage cost of a full mipmap chain is
/// <c>Σ (4ⁿ)⁻¹ for n≥1 = 1/3</c> of the base grid size, matching the
/// issue's storage estimate. A 1000×1000 float grid (base 4 MB per
/// field) grows by ~1.3 MB per field for the full chain.
/// </para>
/// </remarks>
public sealed class CoveragePyramid
{
    private readonly IReadOnlyList<CoverageOverviewLevel> _levels;
    private readonly IReadOnlyDictionary<int, IReadOnlyDictionary<string, float[]>> _levelData;

    /// <summary>
    /// Builds a new pyramid handle. The base grid (level 0) is
    /// represented in <paramref name="levels"/> for enumeration
    /// convenience but no data is stored for it in
    /// <paramref name="levelData"/>.
    /// </summary>
    /// <param name="levels">
    /// All available levels, in increasing order, starting with
    /// level 0. Must contain at least one entry.
    /// </param>
    /// <param name="levelData">
    /// Downsampled cells for levels ≥ 1, keyed by level index. Each
    /// per-level entry is a field-name → flat row-major
    /// <c>float[Rows*Cols]</c> dictionary. Level 0 must not be
    /// present.
    /// </param>
    public CoveragePyramid(
        IReadOnlyList<CoverageOverviewLevel> levels,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, float[]>> levelData)
    {
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(levelData);
        if (levels.Count == 0)
            throw new ArgumentException("Pyramid must include at least the base level.", nameof(levels));
        if (levels[0].Level != 0)
            throw new ArgumentException("First entry must be level 0 (the base grid).", nameof(levels));
        if (levelData.ContainsKey(0))
            throw new ArgumentException("Level 0 data must not be stored in the pyramid (the source owns it).", nameof(levelData));

        _levels = levels;
        _levelData = levelData;
    }

    /// <summary>
    /// All available levels, in increasing order. Level 0 (base) is
    /// always present; subsequent entries are downsampled.
    /// </summary>
    public IReadOnlyList<CoverageOverviewLevel> Levels => _levels;

    /// <summary>Returns the descriptor for the requested level.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> is outside the pyramid's range.
    /// </exception>
    public CoverageOverviewLevel GetLevel(int level)
    {
        if (level < 0 || level >= _levels.Count)
            throw new ArgumentOutOfRangeException(nameof(level), level,
                $"Level must be in [0, {_levels.Count - 1}].");
        return _levels[level];
    }

    /// <summary>
    /// Returns the downsampled cells for the requested (level, field).
    /// Callers must not mutate the returned array.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> is 0 (base grid; source owns it) or
    /// outside the pyramid's range.
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// The pyramid has no data for the requested field name at this level.
    /// </exception>
    public float[] GetField(int level, string fieldName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fieldName);
        if (level == 0)
            throw new ArgumentOutOfRangeException(nameof(level), level,
                "Level 0 is the base grid and is not stored in the pyramid.");
        if (level < 0 || level >= _levels.Count)
            throw new ArgumentOutOfRangeException(nameof(level), level,
                $"Level must be in [1, {_levels.Count - 1}].");

        var byField = _levelData[level];
        if (!byField.TryGetValue(fieldName, out var data))
            throw new KeyNotFoundException($"Pyramid has no data for field '{fieldName}' at level {level}.");
        return data;
    }
}
