using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Names a place on a passage from the loaded S-101 cells, such as
/// "Thorn Channel · off Calshot", for leg rows and the verdict's "where".
/// </summary>
/// <remarks>
/// <para>
/// A best-effort label, not a gazetteer. The water name is the smallest
/// named <c>Fairway</c> or <c>SeaAreaNamedWaterArea</c> that contains the
/// point. The "off" name is the nearest named <c>LandRegion</c>,
/// <c>BuiltUpArea</c> or <c>Landmark</c> within
/// <see cref="MaxOffDistanceMeters"/>, measured to its nearest vertex. Names
/// come from <see cref="FeatureNames"/> (<c>featureName</c>,
/// <c>objectName</c> and their S-57 acronyms).
/// </para>
/// <para>
/// Each cell is indexed the first time a point falls inside its bounds.
/// Instances are safe to share across threads.
/// </para>
/// </remarks>
public sealed class PassagePlaceNames
{
    /// <summary>How far an "off" name may be from the point, in metres.</summary>
    public const double MaxOffDistanceMeters = 4_000;

    private static readonly HashSet<string> WaterTypes = new(StringComparer.Ordinal) { "Fairway", "SeaAreaNamedWaterArea" };
    private static readonly HashSet<string> LandTypes = new(StringComparer.Ordinal) { "LandRegion", "BuiltUpArea", "Landmark" };

    private readonly List<Cell> _cells = new();

    private PassagePlaceNames()
    {
    }

    /// <summary>Builds the names from the S-101 (and S-401, and translated S-57) cells among <paramref name="datasets"/>.</summary>
    /// <param name="datasets">The loaded datasets.</param>
    /// <returns>The place names.</returns>
    public static PassagePlaceNames FromDatasets(IEnumerable<LoadedDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        var names = new PassagePlaceNames();
        foreach (var dataset in datasets)
        {
            if (dataset.Data is S101DatasetData s101)
            {
                names._cells.Add(new Cell(dataset.Bounds, s101.Dataset));
            }
        }

        return names;
    }

    /// <summary>
    /// The name at <paramref name="position"/>: "{water} · off {land}", or
    /// either part alone, or <c>null</c> when nothing named is close.
    /// </summary>
    /// <param name="position">The position (WGS-84).</param>
    /// <returns>The name, or <c>null</c>.</returns>
    public string? NameAt(GeoPosition position)
    {
        Named? water = null;
        Named? land = null;
        var landDistance = MaxOffDistanceMeters;
        var latPad = MaxOffDistanceMeters / PassageGeodesy.MetersPerDegreeLatitude;
        foreach (var cell in _cells)
        {
            var b = cell.Bounds;
            if (position.Latitude < b.SouthLatitude - latPad || position.Latitude > b.NorthLatitude + latPad
                || position.Longitude < b.WestLongitude - 2 * latPad || position.Longitude > b.EastLongitude + 2 * latPad)
            {
                continue;
            }

            foreach (var area in cell.Index.Water)
            {
                if ((water is null || area.Size < water.Size) && area.Contains(position))
                {
                    water = area;
                }
            }

            foreach (var named in cell.Index.Land)
            {
                if (named.DistanceTo(position, landDistance) is { } d && d < landDistance)
                {
                    landDistance = d;
                    land = named;
                }
            }
        }

        return (water?.Name, land?.Name) switch
        {
            ({ } w, { } l) when !string.Equals(w, l, StringComparison.OrdinalIgnoreCase) => $"{w} · off {l}",
            ({ } w, _) => w,
            (null, { } l) => $"off {l}",
            _ => null,
        };
    }

    private sealed class Cell(BoundingBox bounds, S101Dataset dataset)
    {
        private readonly object _gate = new();
        private Built? _built;

        public BoundingBox Bounds { get; } = bounds;

        public Built Index
        {
            get
            {
                if (_built is { } built)
                {
                    return built;
                }

                lock (_gate)
                {
                    return _built ??= Build();
                }
            }
        }

        private Built Build()
        {
            var water = new List<Named>();
            var land = new List<Named>();
            foreach (var feature in new S101VectorSource(dataset).GetFeatures())
            {
                var isWater = feature.GeometryType == GeometryType.Surface && WaterTypes.Contains(feature.FeatureType);
                var isLand = LandTypes.Contains(feature.FeatureType);
                if (!isWater && !isLand)
                {
                    continue;
                }

                var name = FeatureNames.Enumerate(feature).Select(n => n.Value).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
                if (name is null)
                {
                    continue;
                }

                var named = new Named(name.Trim(), feature);
                (isWater ? water : land).Add(named);
            }

            return new Built(water, land);
        }
    }

    private sealed record Built(List<Named> Water, List<Named> Land);

    private sealed class Named
    {
        private readonly IReadOnlyList<SurfacePart> _surfaces;
        private readonly IReadOnlyList<GeoPosition> _points;
        private readonly double _south;
        private readonly double _west;
        private readonly double _north;
        private readonly double _east;

        public Named(string name, Feature feature)
        {
            Name = name;
            var s100 = (IS100Feature)feature;
            _surfaces = s100.Surfaces;
            _points = feature.Coordinates;
            _south = _west = double.MaxValue;
            _north = _east = double.MinValue;
            foreach (var p in _points)
            {
                _south = Math.Min(_south, p.Latitude);
                _north = Math.Max(_north, p.Latitude);
                _west = Math.Min(_west, p.Longitude);
                _east = Math.Max(_east, p.Longitude);
            }

            Size = (_north - _south) * (_east - _west);
        }

        public string Name { get; }

        public double Size { get; }

        public bool Contains(GeoPosition p)
        {
            if (p.Latitude < _south || p.Latitude > _north || p.Longitude < _west || p.Longitude > _east)
            {
                return false;
            }

            foreach (var surface in _surfaces)
            {
                if (InRing(surface.ExteriorRing, p) && !surface.InteriorRings.Any(hole => InRing(hole, p)))
                {
                    return true;
                }
            }

            return false;
        }

        // Distance to the nearest vertex, or null when the bounding box is already farther than the limit.
        public double? DistanceTo(GeoPosition p, double limit)
        {
            var pad = limit / PassageGeodesy.MetersPerDegreeLatitude;
            if (p.Latitude < _south - pad || p.Latitude > _north + pad
                || p.Longitude < _west - 2 * pad || p.Longitude > _east + 2 * pad)
            {
                return null;
            }

            var cosLat = Math.Cos(p.Latitude * Math.PI / 180.0);
            var best = double.PositiveInfinity;
            foreach (var v in _points)
            {
                var dy = (v.Latitude - p.Latitude) * PassageGeodesy.MetersPerDegreeLatitude;
                var dx = (v.Longitude - p.Longitude) * PassageGeodesy.MetersPerDegreeLatitude * cosLat;
                best = Math.Min(best, Math.Sqrt(dx * dx + dy * dy));
            }

            return best;
        }

        private static bool InRing(IReadOnlyList<GeoPosition> ring, GeoPosition p)
        {
            var inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                var a = ring[i];
                var b = ring[j];
                if ((a.Latitude > p.Latitude) != (b.Latitude > p.Latitude)
                    && p.Longitude < (b.Longitude - a.Longitude) * (p.Latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
