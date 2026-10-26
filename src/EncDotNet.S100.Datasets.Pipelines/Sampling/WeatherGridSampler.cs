using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Quantities;

namespace EncDotNet.S100.Datasets.Pipelines.Sampling;

/// <summary>
/// Samples a weather grid (<see cref="WeatherGridSet"/>): every known
/// parameter at the grid node nearest the point and the time step nearest the
/// requested time, as a <see cref="CoverageFieldsSample"/> whose fields carry
/// their quantity and unit. Wind speed and direction are derived from U and V
/// when the file has only the components. Shared by
/// <see cref="WeatherGridProcessorBase"/> (picks) and its catalog payload
/// (<c>sample_coverage</c>).
/// </summary>
/// <remarks>
/// The grid is regular latitude/longitude, so the nearest node is found
/// directly: a point is on the grid when it's within half a cell of a node.
/// A sample decodes the whole step of each parameter (a file read, then
/// normalisation) and keeps the most recently used steps, so neighbouring
/// samples and a route's vertices share the reads.
/// </remarks>
internal sealed class WeatherGridSampler : ICoverageSampler
{
    private const int CacheCapacity = 8;

    private readonly WeatherGridSet _gridSet;
    private readonly WeatherParameter[] _parameters;
    private readonly DateTime[] _times;
    private readonly int[][] _stepIndexByTime;
    private readonly DateTime? _run;
    private readonly bool _deriveWindSpeed;
    private readonly bool _deriveWindDirection;
    private readonly object _cacheGate = new();
    private readonly LinkedList<(WeatherParameter Parameter, int Step, float[] Values)> _cache = new();

    /// <summary>Creates a sampler over the known parameters of a grid set.</summary>
    /// <param name="gridSet">The grid set.</param>
    /// <param name="run">The model run's reference time, or <see langword="null"/> when the data doesn't give one.</param>
    public WeatherGridSampler(WeatherGridSet gridSet, DateTime? run)
    {
        ArgumentNullException.ThrowIfNull(gridSet);
        _gridSet = gridSet;
        _run = run;
        _parameters = gridSet.Parameters.Where(p => p.IsKnown && p.Steps.Count > 0).ToArray();
        _times = _parameters.SelectMany(p => p.Steps).Select(s => s.ValidTime).Distinct().Order().ToArray();
        _stepIndexByTime = _parameters
            .Select(p =>
            {
                var map = new int[_times.Length];
                Array.Fill(map, -1);
                for (int s = 0; s < p.Steps.Count; s++)
                {
                    int t = Array.BinarySearch(_times, p.Steps[s].ValidTime);
                    if (map[t] < 0)
                        map[t] = s;
                }

                return map;
            })
            .ToArray();

        bool has(string id) => _parameters.Any(p => p.Id == id);
        bool components = has(WeatherParameters.WindU) && has(WeatherParameters.WindV);
        _deriveWindSpeed = components && !has(WeatherParameters.WindSpeed);
        _deriveWindDirection = components && !has(WeatherParameters.WindFromDirection);
    }

    /// <summary>The union of the parameters' valid times, in time order.</summary>
    public IReadOnlyList<DateTime> Times => _times;

    /// <inheritdoc />
    public CoverageSiteSearch FindSites(CoverageSampleQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_times.Length == 0)
            return CoverageSiteSearch.None;

        if (FindNode(query.Latitude, query.Longitude) is not { } node)
        {
            return CoverageSiteSearch.None with
            {
                OutsideGrid = true,
                WindowUnsupportedReason = "the point is outside every weather grid",
            };
        }

        var grid = _gridSet.Grid;
        return new CoverageSiteSearch([new Site(this, node, grid.SpacingLatitudinal * grid.SpacingLongitudinal)]);
    }

    /// <summary>The grid node nearest a WGS-84 point, or <see langword="null"/> when the point is more than half a cell from every node.</summary>
    /// <param name="latitude">Latitude, degrees.</param>
    /// <param name="longitude">Longitude, degrees.</param>
    public GridNode? FindNode(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude))
            return null;

        var grid = _gridSet.Grid;
        double rowPosition = (latitude - grid.OriginLatitude) / grid.SpacingLatitudinal;
        // Degrees east of the origin, in [-spacing/2, 360 - spacing/2), so a
        // longitude written in either convention finds its column.
        double east = ((longitude - grid.OriginLongitude) % 360 + 360) % 360;
        if (east >= 360 - grid.SpacingLongitudinal / 2)
            east -= 360;
        double columnPosition = east / grid.SpacingLongitudinal;

        int row = (int)Math.Round(rowPosition, MidpointRounding.AwayFromZero);
        int column = (int)Math.Round(columnPosition, MidpointRounding.AwayFromZero);
        bool global = grid.Columns * grid.SpacingLongitudinal >= 360 - 1e-6;
        if (global)
            column = ((column % grid.Columns) + grid.Columns) % grid.Columns;

        if (row < 0 || row >= grid.Rows || column < 0 || column >= grid.Columns)
            return null;

        return new GridNode(
            row,
            column,
            row * grid.Columns + column,
            grid.OriginLatitude + row * grid.SpacingLatitudinal,
            grid.OriginLongitude + column * grid.SpacingLongitudinal);
    }

    /// <summary>Reads every field at a node and time step; null fields carry no data there.</summary>
    /// <param name="node">The node.</param>
    /// <param name="timeIndex">Index into <see cref="Times"/>.</param>
    /// <param name="requestedTime">The time the caller asked for, echoed in the sample.</param>
    /// <returns>The sample, or <see langword="null"/> when no field has data at the node.</returns>
    public CoverageFieldsSample? Read(GridNode node, int timeIndex, DateTimeOffset? requestedTime)
    {
        var fields = new List<SampledField>(_parameters.Length + 3);
        float? u = null, v = null;
        bool any = false;
        for (int p = 0; p < _parameters.Length; p++)
        {
            var parameter = _parameters[p];
            int step = _stepIndexByTime[p][timeIndex];
            float value = step < 0 ? float.NaN : GetStep(parameter, step)[node.Index];
            double? reading = float.IsNaN(value) ? null : value;
            any |= reading is not null;
            if (parameter.Id == WeatherParameters.WindU)
                u = reading is null ? null : value;
            if (parameter.Id == WeatherParameters.WindV)
                v = reading is null ? null : value;

            var (quantity, unit) = Describe(parameter.Id);
            fields.Add(new SampledField(parameter.Id, quantity, unit, reading));
            if (parameter.Id == WeatherParameters.WindSpeed)
                fields.Add(Knots(reading));
        }

        if (_deriveWindSpeed)
        {
            double? speed = u is { } su && v is { } sv
                ? WindComponents.GetSpeed(Speed.FromMetresPerSecond(su), Speed.FromMetresPerSecond(sv)).TotalMetresPerSecond
                : null;
            fields.Add(new SampledField(WeatherParameters.WindSpeed, "wind_speed", "m/s", speed));
            fields.Add(Knots(speed));
        }

        if (_deriveWindDirection)
        {
            double? direction = u is { } du && v is { } dv
                ? WindComponents.GetFromDirection(Speed.FromMetresPerSecond(du), Speed.FromMetresPerSecond(dv)).TotalDegrees
                : null;
            fields.Add(new SampledField(WeatherParameters.WindFromDirection, "wind_from_direction", "deg", direction));
        }

        if (!any)
            return null;

        return new CoverageFieldsSample(
            fields,
            DateTime.SpecifyKind(_times[timeIndex], DateTimeKind.Utc),
            requestedTime,
            node.Row,
            node.Col,
            node.Latitude,
            node.Longitude);
    }

    /// <summary>
    /// The quantity name (snake case) and unit symbol <see cref="SampledField"/>
    /// reports a parameter with. Values are in the parameter's stored unit.
    /// </summary>
    /// <param name="id">A canonical parameter id.</param>
    public static (string Quantity, string? Unit) Describe(string id) => id switch
    {
        WeatherParameters.WindU => ("eastward_wind", "m/s"),
        WeatherParameters.WindV => ("northward_wind", "m/s"),
        WeatherParameters.WindSpeed => ("wind_speed", "m/s"),
        WeatherParameters.WindFromDirection => ("wind_from_direction", "deg"),
        WeatherParameters.WindGust => ("wind_speed_of_gust", "m/s"),
        WeatherParameters.WaveHeight => ("significant_wave_height", "m"),
        WeatherParameters.WaveDirection => ("wave_from_direction", "deg"),
        WeatherParameters.WavePeriod => ("wave_period", "s"),
        WeatherParameters.WindSeaHeight => ("wind_wave_significant_height", "m"),
        WeatherParameters.SwellHeight => ("swell_wave_significant_height", "m"),
        WeatherParameters.PressureMsl => ("air_pressure_at_mean_sea_level", "Pa"),
        _ => (id, null),
    };

    // The wind speed again in knots, the unit mariners read it in.
    private static SampledField Knots(double? metresPerSecond) =>
        new(WeatherParameters.WindSpeed + ".kn", "wind_speed", "kn",
            metresPerSecond is { } s ? Speed.FromMetresPerSecond(s).TotalKnots : null);

    private float[] GetStep(WeatherParameter parameter, int step)
    {
        lock (_cacheGate)
        {
            for (var entry = _cache.First; entry is not null; entry = entry.Next)
            {
                if (ReferenceEquals(entry.Value.Parameter, parameter) && entry.Value.Step == step)
                {
                    _cache.Remove(entry);
                    _cache.AddFirst(entry);
                    return entry.Value.Values;
                }
            }
        }

        var values = _gridSet.ReadStep(parameter, step);
        lock (_cacheGate)
        {
            _cache.AddFirst((parameter, step, values));
            while (_cache.Count > CacheCapacity)
                _cache.RemoveLast();
        }

        return values;
    }

    private sealed class Site : CoverageSampleSite
    {
        private readonly WeatherGridSampler _owner;
        private readonly GridNode _node;

        public Site(WeatherGridSampler owner, GridNode node, double cellArea)
            : base(CoverageSiteKind.Grid, cellArea, owner._run, CoverageStepWindow.FromSteps(owner._times))
        {
            _owner = owner;
            _node = node;
        }

        public override IReadOnlyList<DateTime> Steps => _owner._times;

        public override CoverageReading Read(DateTimeOffset? requestedTime)
        {
            int step = CoverageSampling.SelectTimeStep(_owner._times, requestedTime);
            return _owner.Read(_node, step, requestedTime) is { } sample
                ? new CoverageReading.Value(sample)
                : new CoverageReading.NoData(_node.Row, _node.Col, GridNodes.Utc(_owner._times[step]));
        }

        public override CoverageReading ReadSteps(IReadOnlyList<CoverageStepRequest> steps)
        {
            var entries = new List<CoverageTimedSample>(steps.Count);
            foreach (var (requested, index) in steps)
                entries.Add(new CoverageTimedSample(GridNodes.Utc(_owner._times[index]), requested, _owner.Read(_node, index, requested)));

            var first = steps[0];
            var fallback = new CoverageFieldsSample(
                [],
                GridNodes.Utc(_owner._times[first.StepIndex]),
                first.RequestedTime,
                _node.Row,
                _node.Col,
                _node.Latitude,
                _node.Longitude);
            return new CoverageReading.Series(entries, fallback);
        }
    }
}
