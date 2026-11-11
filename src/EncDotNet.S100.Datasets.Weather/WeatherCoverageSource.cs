using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Coverage.Pyramid;

namespace EncDotNet.S100.Datasets.Weather;

/// <summary>
/// Serves one parameter group of a <see cref="WeatherGridSet"/> (for example
/// the 10 m wind, or the combined sea state) to the coverage pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Each parameter of the group is a value field named by its id
/// (<c>"wind.u"</c>, <c>"wave.hs"</c>). When the group has the wind U and V
/// components but no stored speed or direction, <see cref="Sample"/> adds
/// <c>"wind.speed"</c> and <c>"wind.from_direction"</c> as
/// <see cref="SampledCoverage.ComputedFields"/>, derived cell by cell (see
/// <see cref="WindComponents"/>); the surface current group gets
/// <c>"current.speed"</c> and <c>"current.to_direction"</c> the same way
/// (<see cref="CurrentComponents"/>). Those aren't in
/// <see cref="CoverageMetadata.ValueFields"/>, which lists stored fields;
/// <see cref="FieldNames"/> lists both.
/// </para>
/// <para>
/// A <em>combined</em> source (<see cref="IsCombined"/>) reads parameters of
/// several groups to derive one field from them: the freezing spray
/// predictor (<see cref="CreateFreezingSpray"/>). Its
/// <see cref="FieldNames"/> lists only that derived field, so only the styles
/// drawing it apply to it, and the wind and temperatures it reads are drawn
/// by their own groups' sources.
/// </para>
/// <para>
/// Time steps are decoded only when <see cref="Sample"/> first needs them,
/// through each step's <see cref="WeatherStepDecoder"/>, and the most recently
/// used steps are kept (<see cref="CacheCapacity"/>). Missing values are
/// <see cref="float.NaN"/>, which is also <see cref="CoverageMetadata.NoDataValue"/>.
/// </para>
/// <para>
/// Overview levels 1 and 2 are the base grid decimated 2× and 4× per axis:
/// they keep every second or fourth point, starting at the origin, so they
/// need no extra memory and directions are never averaged. A stride in the
/// sampled region decimates the same way.
/// </para>
/// <para>
/// Like other sources, this one isn't meant to be shared across threads
/// without serialising select-then-sample; the step cache is safe to use
/// from more than one thread.
/// </para>
/// </remarks>
public sealed class WeatherCoverageSource : ICoverageSource
{
    /// <summary>The default number of decoded time steps kept, <c>2</c>.</summary>
    public const int DefaultCacheCapacity = 2;

    /// <summary>The coarsest overview level: level 2, the grid decimated 4× per axis.</summary>
    public const int MaxOverviewLevel = 2;

    private const string HorizontalCrs = "EPSG:4326";

    private readonly WeatherGridSet _gridSet;
    private readonly WeatherParameter[] _parameters;
    private readonly DateTime[] _times;
    // For each parameter, the index of its step at each time (or -1).
    private readonly int[][] _stepIndexByTime;
    private readonly ComputedCoverageField[] _derivedFields;
    private readonly CoverageOverviewLevel[] _levels;
    private readonly object _cacheGate = new();
    // Most recently used first.
    private readonly LinkedList<(int TimeIndex, float[][] Values)> _cache = new();
    private int _selectedTimeIndex;
    private int _selectedLevel;

    /// <summary>Creates a coverage source over some parameters of a grid set.</summary>
    /// <param name="gridSet">The grid set.</param>
    /// <param name="name">The group name, for example <see cref="WeatherParameters.WindGroup"/>.</param>
    /// <param name="parameters">
    /// The parameters to serve, all from <paramref name="gridSet"/>, with
    /// distinct ids. Unknown parameters are allowed; they're served under
    /// their reader's id.
    /// </param>
    /// <param name="spec">
    /// The product identity reported in <see cref="CoverageMetadata.Spec"/>
    /// and on the portrayal: the file format and its version, for example
    /// <c>SpecRef.Parse("NetCDF/4.0.0")</c>, <c>NetCDF/3.0.0</c> or
    /// <c>GRIB2/2.0.0</c> (<see cref="SpecName"/> accepts these non-S-100
    /// names). A processor always passes one; <c>default(SpecRef)</c>, whose
    /// <see cref="SpecRef.Name"/> is <see langword="null"/>, is only for a grid
    /// built in memory with no file behind it.
    /// </param>
    /// <param name="cacheCapacity">How many decoded time steps to keep; at least 1.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="parameters"/> is empty, holds a parameter from another
    /// set or a repeated id, or has no time steps at all.
    /// </exception>
    public WeatherCoverageSource(
        WeatherGridSet gridSet,
        string name,
        IReadOnlyList<WeatherParameter> parameters,
        SpecRef spec = default,
        int cacheCapacity = DefaultCacheCapacity)
        : this(gridSet, name, parameters, spec, cacheCapacity, combined: null)
    {
    }

    // combined: the derived fields of a combined source, or null to derive
    // speed and direction from wind and current components.
    private WeatherCoverageSource(
        WeatherGridSet gridSet,
        string name,
        IReadOnlyList<WeatherParameter> parameters,
        SpecRef spec,
        int cacheCapacity,
        ComputedCoverageField[]? combined)
    {
        ArgumentNullException.ThrowIfNull(gridSet);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentOutOfRangeException.ThrowIfLessThan(cacheCapacity, 1);
        if (parameters.Count == 0)
            throw new ArgumentException("A weather coverage source needs at least one parameter.", nameof(parameters));

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in parameters)
        {
            ArgumentNullException.ThrowIfNull(parameter, nameof(parameters));
            if (!gridSet.Parameters.Contains(parameter))
                throw new ArgumentException($"'{parameter}' isn't a parameter of the grid set.", nameof(parameters));
            if (!ids.Add(parameter.Id))
                throw new ArgumentException($"The source has two '{parameter.Id}' parameters.", nameof(parameters));
        }

        _gridSet = gridSet;
        _parameters = parameters.ToArray();
        Name = name;
        Spec = spec;
        CacheCapacity = cacheCapacity;

        _times = _parameters
            .SelectMany(p => p.Steps)
            .Select(s => s.ValidTime)
            .Distinct()
            .Order()
            .ToArray();
        if (_times.Length == 0)
            throw new ArgumentException("The parameters have no time steps.", nameof(parameters));

        _stepIndexByTime = new int[_parameters.Length][];
        for (int p = 0; p < _parameters.Length; p++)
        {
            var map = new int[_times.Length];
            Array.Fill(map, -1);
            var steps = _parameters[p].Steps;
            for (int s = 0; s < steps.Count; s++)
            {
                int t = Array.BinarySearch(_times, steps[s].ValidTime);
                // The first step at a time wins if a reader repeats one.
                if (map[t] < 0)
                    map[t] = s;
            }

            _stepIndexByTime[p] = map;
        }

        _derivedFields = combined ?? BuildDerivedFields(ids);
        IsCombined = combined is not null;
        _levels = BuildOverviewLevels(gridSet.Grid);
        FieldNames = IsCombined
            ? [.. _derivedFields.Select(f => f.Name)]
            : [.. _parameters.Select(p => p.Id), .. _derivedFields.Select(f => f.Name)];
    }

    /// <summary>
    /// Creates one source per parameter group of the grid set's known
    /// parameters (<see cref="WeatherParameterDefinition.Group"/>), split by
    /// level, in the order the groups first appear, then the combined freezing
    /// spray source when the set has its inputs (<see cref="CreateFreezingSpray"/>).
    /// Unknown parameters aren't drawn, so they get no source.
    /// </summary>
    /// <param name="gridSet">The grid set.</param>
    /// <param name="spec">The product identity; see the constructor.</param>
    /// <param name="cacheCapacity">How many decoded time steps each source keeps.</param>
    /// <returns>The sources; empty when the set has no known parameters with time steps.</returns>
    public static IReadOnlyList<WeatherCoverageSource> CreateForGroups(
        WeatherGridSet gridSet,
        SpecRef spec = default,
        int cacheCapacity = DefaultCacheCapacity)
    {
        ArgumentNullException.ThrowIfNull(gridSet);

        var groups = new List<(string Group, WeatherLevel Level, List<WeatherParameter> Members)>();
        foreach (var parameter in gridSet.Parameters)
        {
            if (parameter.Definition is not { } definition || parameter.Steps.Count == 0)
                continue;

            var index = groups.FindIndex(g => g.Group == definition.Group && g.Level == parameter.Level);
            if (index < 0)
                groups.Add((definition.Group, parameter.Level, [parameter]));
            else
                groups[index].Members.Add(parameter);
        }

        var sources = groups
            .Select(g => new WeatherCoverageSource(gridSet, g.Group, g.Members, spec, cacheCapacity))
            .ToList();
        if (CreateFreezingSpray(gridSet, spec, cacheCapacity) is { } spray)
            sources.Add(spray);
        return sources;
    }

    /// <summary>
    /// Creates the combined source that derives the freezing spray predictor
    /// (<see cref="WeatherParameters.FreezingSprayPredictor"/>,
    /// <see cref="FreezingSpray"/>) cell by cell, when the grid set has what it
    /// needs: the wind (speed, or its U and V components), the 2 m air
    /// temperature, and the sea temperature, or else the surface (skin)
    /// temperature, which over water is the model's sea temperature. With the
    /// skin temperature, a land cover mask, when the set has one, leaves land
    /// out; without one, only skin temperatures under −2 °C are.
    /// </summary>
    /// <param name="gridSet">The grid set.</param>
    /// <param name="spec">The product identity; see the constructor.</param>
    /// <param name="cacheCapacity">How many decoded time steps the source keeps.</param>
    /// <returns>The source, named <see cref="WeatherParameters.FreezingSprayGroup"/>, or <see langword="null"/> when an input is missing.</returns>
    public static WeatherCoverageSource? CreateFreezingSpray(
        WeatherGridSet gridSet,
        SpecRef spec = default,
        int cacheCapacity = DefaultCacheCapacity)
    {
        ArgumentNullException.ThrowIfNull(gridSet);

        WeatherParameter? Find(string id) =>
            gridSet.Parameters.FirstOrDefault(p => p.Definition?.Id == id && p.Steps.Count > 0);

        var air = Find(WeatherParameters.AirTemperature);
        var water = Find(WeatherParameters.SeaSurfaceTemperature);
        var land = water is null ? Find(WeatherParameters.LandCover) : null;
        water ??= Find(WeatherParameters.SurfaceTemperature);
        var speed = Find(WeatherParameters.WindSpeed);
        var (u, v) = speed is null ? (Find(WeatherParameters.WindU), Find(WeatherParameters.WindV)) : (null, null);
        if (air is null || water is null || (speed is null && (u is null || v is null)))
            return null;

        var inputs = new List<WeatherParameter>();
        if (speed is not null)
            inputs.Add(speed);
        else
            inputs.AddRange([u!, v!]);
        inputs.Add(air);
        inputs.Add(water);
        if (land is not null)
            inputs.Add(land);

        bool components = speed is null;
        bool masked = land is not null;
        CoverageFieldComputation compute = (components, masked) switch
        {
            (false, false) => static i => FreezingSpray.PredictorCell(i[0], i[1], i[2], 0),
            (false, true) => static i => FreezingSpray.PredictorCell(i[0], i[1], i[2], i[3]),
            (true, false) => static i => FreezingSpray.PredictorCell(WindComponents.SpeedCell(i), i[2], i[3], 0),
            (true, true) => static i => FreezingSpray.PredictorCell(WindComponents.SpeedCell(i), i[2], i[3], i[4]),
        };
        var predictor = new ComputedCoverageField
        {
            Name = WeatherParameters.FreezingSprayPredictor,
            Inputs = [.. inputs.Select(p => p.Id)],
            Compute = compute,
            NoDataValue = float.NaN,
        };

        return new WeatherCoverageSource(gridSet, WeatherParameters.FreezingSprayGroup, inputs, spec, cacheCapacity, [predictor]);
    }

    /// <summary>
    /// Whether the source combines several groups' parameters to derive one
    /// field (<see cref="CreateFreezingSpray"/>), so lists only that field in
    /// <see cref="FieldNames"/>.
    /// </summary>
    public bool IsCombined { get; }

    /// <summary>The group name, for example <c>"wind"</c>.</summary>
    public string Name { get; }

    /// <summary>The grid set this source serves.</summary>
    public WeatherGridSet GridSet => _gridSet;

    /// <summary>The parameters served as stored value fields.</summary>
    public IReadOnlyList<WeatherParameter> Parameters => _parameters;

    /// <summary>
    /// The product identity reported in <see cref="CoverageMetadata.Spec"/>,
    /// for example <c>NetCDF/4.0.0</c>; <c>default(SpecRef)</c> for an
    /// in-memory grid (see the constructor).
    /// </summary>
    public SpecRef Spec { get; }

    /// <summary>How many decoded time steps are kept.</summary>
    public int CacheCapacity { get; }

    /// <summary>
    /// The fields derived at sampling time, added to every
    /// <see cref="SampledCoverage"/> as <see cref="SampledCoverage.ComputedFields"/>.
    /// </summary>
    public IReadOnlyList<ComputedCoverageField> DerivedFields => _derivedFields;

    /// <summary>
    /// The fields styles can draw: the stored parameters, then the derived
    /// fields; for a combined source (<see cref="IsCombined"/>) only its
    /// derived field.
    /// </summary>
    public IReadOnlyList<string> FieldNames { get; }

    /// <summary>The time step currently selected.</summary>
    public DateTime SelectedTime => _times[_selectedTimeIndex];

    /// <inheritdoc/>
    /// <remarks>
    /// The grid geometry reflects the selected overview level. The CRS is
    /// EPSG:4326, the "vertical datum" is the group's level description, and
    /// the no-data value is NaN.
    /// </remarks>
    public CoverageMetadata Metadata
    {
        get
        {
            var grid = _gridSet.Grid;
            var level = _levels[_selectedLevel];
            return new CoverageMetadata
            {
                Spec = Spec,
                NativeExtent = grid.Extent,
                GridMetadata = new GridMetadata
                {
                    NumRows = level.Rows,
                    NumColumns = level.Cols,
                    OriginLatitude = grid.OriginLatitude,
                    OriginLongitude = grid.OriginLongitude,
                    SpacingLatitudinal = level.SpacingLatitudinal,
                    SpacingLongitudinal = level.SpacingLongitudinal,
                },
                HorizontalCRS = HorizontalCrs,
                VerticalDatum = _parameters[0].LevelDescription,
                NoDataValue = float.NaN,
                ValueFields = _parameters
                    .Select(p => new CoverageValueField
                    {
                        Name = p.Id,
                        Type = CoverageValueType.Float,
                        Units = p.Units,
                        FillValue = float.NaN,
                    })
                    .ToArray(),
            };
        }
    }

    /// <inheritdoc/>
    /// <remarks>The union of the parameters' valid times, in time order.</remarks>
    public IReadOnlyList<DateTime> AvailableTimes => _times;

    /// <inheritdoc/>
    /// <remarks>
    /// Selecting a time doesn't decode anything; the step is decoded by the
    /// next <see cref="Sample"/>. A parameter with no step at the selected
    /// time samples as NaN.
    /// </remarks>
    public void SelectTime(DateTime time)
    {
        int exact = Array.BinarySearch(_times, time);
        if (exact >= 0)
        {
            _selectedTimeIndex = exact;
            return;
        }

        // Nearest; on a tie, the earlier step.
        int after = ~exact;
        if (after == 0)
            _selectedTimeIndex = 0;
        else if (after == _times.Length)
            _selectedTimeIndex = _times.Length - 1;
        else
            _selectedTimeIndex = (_times[after] - time) < (time - _times[after - 1]) ? after : after - 1;
    }

    /// <inheritdoc/>
    public IReadOnlyList<CoverageOverviewLevel> AvailableOverviewLevels => _levels;

    /// <inheritdoc/>
    public int SelectedOverviewLevel => _selectedLevel;

    /// <inheritdoc/>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> isn't in <see cref="AvailableOverviewLevels"/>
    /// (a grid too small to decimate has fewer levels).
    /// </exception>
    public void SelectOverviewLevel(int level)
    {
        if (level < 0 || level >= _levels.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(level), level,
                $"Level must be in [0, {_levels.Length - 1}].");
        }

        _selectedLevel = level;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Decodes the selected time step on first use (synchronous, CPU-bound
    /// work for an in-memory grid; I/O-bound for a reader that decodes from
    /// the file). <paramref name="cancellationToken"/> is passed to the
    /// decoder and checked per row of the copy. Cells skipped by a stride are
    /// dropped (plain decimation), as are those skipped by the overview level.
    /// </remarks>
    public SampledCoverage Sample(GridRegion region, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(region);

        var grid = _gridSet.Grid;
        var level = _levels[_selectedLevel];
        int factor = 1 << _selectedLevel;
        var (rowStart, rowEnd, colStart, colEnd) = region.Resolve(level.Rows, level.Cols);
        rowStart = Math.Clamp(rowStart, 0, level.Rows);
        rowEnd = Math.Clamp(rowEnd, rowStart, level.Rows);
        colStart = Math.Clamp(colStart, 0, level.Cols);
        colEnd = Math.Clamp(colEnd, colStart, level.Cols);
        int rowStride = Math.Max(1, region.RowStride);
        int colStride = Math.Max(1, region.ColStride);
        int rows = DivideRoundUp(rowEnd - rowStart, rowStride);
        int cols = DivideRoundUp(colEnd - colStart, colStride);

        var values = new Dictionary<string, float[]>(_parameters.Length, StringComparer.Ordinal);
        float[][]? step = rows * cols == 0 ? null : GetStep(_selectedTimeIndex, cancellationToken);
        for (int p = 0; p < _parameters.Length; p++)
        {
            var destination = new float[rows * cols];
            if (step is not null)
            {
                var source = step[p];
                int baseRowStride = rowStride * factor;
                int baseColStride = colStride * factor;
                for (int r = 0; r < rows; r++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int sourceRowBase = (rowStart * factor + r * baseRowStride) * grid.Columns;
                    int sourceCol = colStart * factor;
                    int destinationBase = r * cols;
                    for (int c = 0; c < cols; c++, sourceCol += baseColStride)
                        destination[destinationBase + c] = source[sourceRowBase + sourceCol];
                }
            }

            values[_parameters[p].Id] = destination;
        }

        return new SampledCoverage
        {
            Region = region,
            Metadata = new GridMetadata
            {
                NumRows = rows,
                NumColumns = cols,
                OriginLatitude = grid.OriginLatitude + rowStart * level.SpacingLatitudinal,
                OriginLongitude = grid.OriginLongitude + colStart * level.SpacingLongitudinal,
                SpacingLatitudinal = level.SpacingLatitudinal * rowStride,
                SpacingLongitudinal = level.SpacingLongitudinal * colStride,
            },
            Values = values,
            ComputedFields = _derivedFields,
        };
    }

    /// <summary>The time indexes (into <see cref="AvailableTimes"/>) of the cached steps, most recently used first.</summary>
    internal IReadOnlyList<int> CachedTimeIndexes
    {
        get
        {
            lock (_cacheGate)
                return _cache.Select(e => e.TimeIndex).ToArray();
        }
    }

    // Returns the normalised full-resolution values of every parameter at a
    // time, decoding them on a cache miss and evicting the least recently used.
    private float[][] GetStep(int timeIndex, CancellationToken cancellationToken)
    {
        lock (_cacheGate)
        {
            for (var node = _cache.First; node is not null; node = node.Next)
            {
                if (node.Value.TimeIndex == timeIndex)
                {
                    _cache.Remove(node);
                    _cache.AddFirst(node);
                    return node.Value.Values;
                }
            }
        }

        var grid = _gridSet.Grid;
        var decoded = new float[_parameters.Length][];
        for (int p = 0; p < _parameters.Length; p++)
        {
            int stepIndex = _stepIndexByTime[p][timeIndex];
            if (stepIndex < 0)
            {
                var missing = new float[grid.CellCount];
                Array.Fill(missing, float.NaN);
                decoded[p] = missing;
            }
            else
            {
                decoded[p] = _gridSet.ReadStep(_parameters[p], stepIndex, cancellationToken);
            }
        }

        lock (_cacheGate)
        {
            // Another thread may have decoded the same step meanwhile.
            for (var node = _cache.First; node is not null; node = node.Next)
            {
                if (node.Value.TimeIndex == timeIndex)
                    return node.Value.Values;
            }

            _cache.AddFirst((timeIndex, decoded));
            while (_cache.Count > CacheCapacity)
                _cache.RemoveLast();
        }

        return decoded;
    }

    private static ComputedCoverageField[] BuildDerivedFields(HashSet<string> ids)
    {
        var derived = new List<ComputedCoverageField>(4);
        AddDerived(derived, ids, WeatherParameters.WindU, WeatherParameters.WindV,
            WeatherParameters.WindSpeed, WindComponents.SpeedCell,
            WeatherParameters.WindFromDirection, WindComponents.FromDirectionCell);
        AddDerived(derived, ids, WeatherParameters.CurrentU, WeatherParameters.CurrentV,
            WeatherParameters.CurrentSpeed, WindComponents.SpeedCell,
            WeatherParameters.CurrentToDirection, CurrentComponents.ToDirectionCell);
        return derived.ToArray();
    }

    // Speed and direction from a (u, v) pair, unless the group stores them.
    private static void AddDerived(
        List<ComputedCoverageField> derived,
        HashSet<string> ids,
        string u,
        string v,
        string speed,
        CoverageFieldComputation speedCell,
        string direction,
        CoverageFieldComputation directionCell)
    {
        if (!ids.Contains(u) || !ids.Contains(v))
            return;

        string[] inputs = [u, v];
        if (!ids.Contains(speed))
            derived.Add(new ComputedCoverageField { Name = speed, Inputs = inputs, Compute = speedCell, NoDataValue = float.NaN });
        if (!ids.Contains(direction))
            derived.Add(new ComputedCoverageField { Name = direction, Inputs = inputs, Compute = directionCell, NoDataValue = float.NaN });
    }

    // Level 0 is the grid; levels 1 and 2 keep every second and fourth point,
    // and are offered only while both axes still have at least two points.
    private static CoverageOverviewLevel[] BuildOverviewLevels(WeatherGrid grid)
    {
        var levels = new List<CoverageOverviewLevel>
        {
            new(0, grid.Rows, grid.Columns, grid.SpacingLatitudinal, grid.SpacingLongitudinal),
        };
        for (int level = 1; level <= MaxOverviewLevel; level++)
        {
            int factor = 1 << level;
            int rows = DivideRoundUp(grid.Rows, factor);
            int cols = DivideRoundUp(grid.Columns, factor);
            if (rows < 2 || cols < 2)
                break;
            levels.Add(new(level, rows, cols, grid.SpacingLatitudinal * factor, grid.SpacingLongitudinal * factor));
        }

        return levels.ToArray();
    }

    private static int DivideRoundUp(int value, int divisor) =>
        value <= 0 ? 0 : (value - 1) / divisor + 1;
}
