using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Sampling;
using EncDotNet.S100.Datasets.Weather;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Weather for the route checks from loaded GRIB2 and NetCDF weather grids
/// (<see cref="WeatherGridDatasetData"/>): wind speed at 10 m (stored, or
/// from the U and V components) and significant wave height, at the grid
/// node nearest a position.
/// </summary>
/// <remarks>
/// <para>
/// A position is on a grid when it's within half a cell of a node, as the
/// picks and <c>sample_coverage</c> find it. Datasets are ranked as
/// <c>sample_coverage</c> ranks them: the finest grid first, then the newest
/// model run, then the latest last step, then catalog order. So a regional
/// model wins over a global one where both cover the route.
/// </para>
/// <para>
/// A series reads a step for every place a check has located on the grid at
/// once, from the grid's recent-steps cache, and keeps those values. So a
/// route reads each forecast step at most once, and a departure sweep across
/// the whole forecast decodes each step once rather than once per departure.
/// </para>
/// </remarks>
internal sealed class CatalogRouteWeatherSource : IRouteWeatherSource
{
    private readonly Field[][] _fields;

    public CatalogRouteWeatherSource(IReadOnlyList<LoadedDataset> datasets)
    {
        var wind = new List<Field>();
        var waves = new List<Field>();
        var order = 0;
        foreach (var dataset in datasets)
        {
            order++;
            if (dataset.Data is not WeatherGridDatasetData data)
            {
                continue;
            }

            var sampler = data.Sampler;
            var set = sampler.GridSet;
            var id = dataset.Id.Value;
            if (set.FindParameter(WeatherParameters.WindSpeed) is { Steps.Count: > 0 } speed)
            {
                wind.Add(new Field(id, sampler, [speed], order));
            }
            else if (set.FindParameter(WeatherParameters.WindU) is { Steps.Count: > 0 } u && set.FindParameter(WeatherParameters.WindV) is { Steps.Count: > 0 } v)
            {
                wind.Add(new Field(id, sampler, [u, v], order));
            }

            if (set.FindParameter(WeatherParameters.WaveHeight) is { Steps.Count: > 0 } hs)
            {
                waves.Add(new Field(id, sampler, [hs], order));
            }
        }

        wind.RemoveAll(f => f.Steps.Count == 0);
        waves.RemoveAll(f => f.Steps.Count == 0);
        wind.Sort(Field.Compare);
        waves.Sort(Field.Compare);
        _fields = [wind.ToArray(), waves.ToArray()];
    }

    /// <summary>Whether any of the datasets is a weather grid this source reads.</summary>
    /// <param name="datasets">The loaded datasets.</param>
    /// <returns><c>true</c> when one is.</returns>
    public static bool HasWeather(IEnumerable<LoadedDataset> datasets) => datasets.Any(d => d.Data is WeatherGridDatasetData);

    /// <inheritdoc />
    public bool Carries(WeatherQuantity quantity) => FieldsOf(quantity).Length > 0;

    /// <inheritdoc />
    public IReadOnlyList<IRouteWeatherSeries> Locate(GeoPosition position, WeatherQuantity quantity)
    {
        var fields = FieldsOf(quantity);
        if (fields.Length == 0)
        {
            return [];
        }

        var series = new List<IRouteWeatherSeries>(fields.Length);
        foreach (var field in fields)
        {
            if (field.Sampler.FindNode(position.Latitude, position.Longitude) is { } node)
            {
                series.Add(new Series(field, field.Register(node.Index), string.Create(CultureInfo.InvariantCulture, $"{node.Row},{node.Col}")));
            }
        }

        return series;
    }

    private Field[] FieldsOf(WeatherQuantity quantity) => (int)quantity is var i && i >= 0 && i < _fields.Length ? _fields[i] : [];

    /// <summary>One quantity of one dataset, with the values read so far at the nodes located on it.</summary>
    private sealed class Field
    {
        private readonly WeatherParameter[] _parameters;
        private readonly int[][] _stepIndex;
        private readonly object _gate = new();
        private readonly Dictionary<int, int> _slots = new();
        private readonly List<int> _nodes = new();
        private readonly float[]?[] _byStep;

        public Field(string datasetId, WeatherGridSampler sampler, WeatherParameter[] parameters, int order)
        {
            Sampler = sampler;
            _parameters = parameters;
            Order = order;
            var grid = sampler.GridSet.Grid;
            CellArea = grid.SpacingLatitudinal * grid.SpacingLongitudinal;

            // The times every parameter has a step at (both components of the wind).
            var common = parameters
                .Select(p => p.Steps.Select(s => s.ValidTime).ToHashSet())
                .Aggregate((a, b) => { a.IntersectWith(b); return a; })
                .Order()
                .ToArray();
            Steps = common.Select(t => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Utc))).ToArray();
            _stepIndex = parameters
                .Select(p => common.Select(t => IndexOf(p, t)).ToArray())
                .ToArray();
            _byStep = new float[]?[common.Length];
            var run = sampler.Run is { } r ? new DateTimeOffset(DateTime.SpecifyKind(r, DateTimeKind.Utc)) : (DateTimeOffset?)null;
            Info = new RouteWeatherSourceInfo(datasetId, run);
        }

        public WeatherGridSampler Sampler { get; }

        public int Order { get; }

        public double CellArea { get; }

        public IReadOnlyList<DateTimeOffset> Steps { get; }

        public RouteWeatherSourceInfo Info { get; }

        // Finest grid, then newest run, then latest last step, then catalog order.
        public static int Compare(Field a, Field b)
        {
            var c = a.CellArea.CompareTo(b.CellArea);
            if (c != 0)
            {
                return c;
            }

            c = Nullable.Compare(b.Info.ModelRun, a.Info.ModelRun);
            if (c != 0)
            {
                return c;
            }

            c = b.Steps[^1].CompareTo(a.Steps[^1]);
            return c != 0 ? c : a.Order.CompareTo(b.Order);
        }

        public int Register(int node)
        {
            lock (_gate)
            {
                if (!_slots.TryGetValue(node, out var slot))
                {
                    slot = _nodes.Count;
                    _nodes.Add(node);
                    _slots[node] = slot;
                }

                return slot;
            }
        }

        public double? Value(int slot, int step)
        {
            int[] nodes;
            lock (_gate)
            {
                if (_byStep[step] is { } row && slot < row.Length)
                {
                    return ToValue(row[slot]);
                }

                nodes = _nodes.ToArray();
            }

            var values = Read(step, nodes);
            lock (_gate)
            {
                if (_byStep[step] is not { } row || row.Length < values.Length)
                {
                    _byStep[step] = values;
                }

                return ToValue(_byStep[step]![slot]);
            }
        }

        private static double? ToValue(float value) => float.IsFinite(value) ? value : null;

        // The quantity at each node at a step: the stored value, or the speed of the U and V components.
        private float[] Read(int step, int[] nodes)
        {
            var first = Sampler.ReadStep(_parameters[0], _stepIndex[0][step]);
            var values = new float[nodes.Length];
            if (_parameters.Length == 1)
            {
                for (var i = 0; i < nodes.Length; i++)
                {
                    values[i] = first[nodes[i]];
                }

                return values;
            }

            var second = Sampler.ReadStep(_parameters[1], _stepIndex[1][step]);
            for (var i = 0; i < nodes.Length; i++)
            {
                float u = first[nodes[i]], v = second[nodes[i]];
                values[i] = MathF.Sqrt((u * u) + (v * v));
            }

            return values;
        }

        private static int IndexOf(WeatherParameter parameter, DateTime time)
        {
            for (var i = 0; i < parameter.Steps.Count; i++)
            {
                if (parameter.Steps[i].ValidTime == time)
                {
                    return i;
                }
            }

            return -1;
        }
    }

    private sealed class Series(Field owner, int slot, string cell) : IRouteWeatherSeries
    {
        public RouteWeatherSourceInfo Source { get; } = owner.Info with { GridCell = cell };

        public IReadOnlyList<DateTimeOffset> Steps => owner.Steps;

        public double? ValueAt(int stepIndex) => owner.Value(slot, stepIndex);
    }
}
