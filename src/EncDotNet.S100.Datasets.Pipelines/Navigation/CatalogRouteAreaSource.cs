using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S122.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// The area check's regulated areas (#956) from loaded S-101 cells (and
/// S-401, and translated S-57) and S-122 datasets.
/// </summary>
/// <remarks>
/// <para>
/// S-101 areas come from each cell's spatial index by the queried box, as
/// the chart features do (<see cref="CatalogRouteChartSource"/>), and their
/// attributes from the feature records, which keep every value of
/// <c>restriction</c> and the category lists and the sub-attributes of
/// <c>featureName</c>, <c>fixedDateRange</c> and <c>periodicDateRange</c>.
/// A translated S-57 cell's acronyms (<c>RESTRN</c>, <c>CATREA</c>,
/// <c>OBJNAM</c>, <c>DATSTA</c>/<c>DATEND</c>, <c>PERSTA</c>/<c>PEREND</c>)
/// are read where the S-101 codes are absent.
/// </para>
/// <para>
/// The S-101 feature types checked: <c>RestrictedArea</c> (and the 1.x
/// <c>RestrictedAreaNavigational</c> and <c>RestrictedAreaRegulatory</c>),
/// <c>MilitaryPracticeArea</c>, <c>CautionArea</c>, <c>AnchorageArea</c>,
/// <c>SeparationZoneOrLine</c> surfaces (the 1.x <c>TrafficSeparationZone</c>),
/// <c>PrecautionaryArea</c>, <c>InshoreTrafficZone</c> and
/// <c>SubmarineTransitLane</c>. Cable and pipeline areas, dumping and spoil
/// grounds, fairways and traffic lanes aren't: they restrict anchoring or
/// fishing, or are where a route is meant to go, so on a passage they'd be
/// noise.
/// </para>
/// <para>
/// S-122 areas are the <c>MarineProtectedArea</c> and <c>RestrictedArea</c>
/// surfaces of the typed model (<see cref="S122MarineProtectedAreaDataset"/>),
/// with their holes. Its <c>restriction</c> holds one value.
/// </para>
/// </remarks>
internal sealed class CatalogRouteAreaSource : IRouteAreaSource
{
    private readonly CatalogRouteChartSource _charts;
    private readonly Lazy<List<(RouteArea Area, BoundingBox Extent)>> _s122;

    public CatalogRouteAreaSource(CatalogRouteChartSource charts, IReadOnlyList<LoadedDataset> datasets)
    {
        _charts = charts;
        var s122 = datasets.Where(d => d.Data is S122DatasetData).OrderBy(d => d.Id.Value, StringComparer.Ordinal).ToList();
        _s122 = new Lazy<List<(RouteArea, BoundingBox)>>(() => ReadS122(s122), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public IReadOnlyList<RouteArea> AreasNear(BoundingBox extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        var result = new List<RouteArea>();
        _charts.AddAreas(extent, result);
        foreach (var (area, bounds) in _s122.Value)
        {
            if (bounds.SouthLatitude <= extent.NorthLatitude && bounds.NorthLatitude >= extent.SouthLatitude
                && bounds.WestLongitude <= extent.EastLongitude && bounds.EastLongitude >= extent.WestLongitude)
            {
                result.Add(area);
            }
        }

        return result;
    }

    public IReadOnlyList<RouteChartCell> CellsNear(BoundingBox extent) => _charts.CellsNear(extent);

    /// <summary>What an S-101 feature type is to the area check, or <c>null</c> when it isn't checked.</summary>
    internal static RouteAreaKind? Classify(string featureType) => featureType switch
    {
        "RestrictedArea" or "RestrictedAreaNavigational" or "RestrictedAreaRegulatory" => RouteAreaKind.RestrictedArea,
        "MilitaryPracticeArea" => RouteAreaKind.MilitaryPracticeArea,
        "CautionArea" => RouteAreaKind.CautionArea,
        "AnchorageArea" => RouteAreaKind.AnchorageArea,
        "SeparationZoneOrLine" or "TrafficSeparationZone" => RouteAreaKind.SeparationZone,
        "PrecautionaryArea" => RouteAreaKind.PrecautionaryArea,
        "InshoreTrafficZone" => RouteAreaKind.InshoreTrafficZone,
        "SubmarineTransitLane" => RouteAreaKind.SubmarineTransitLane,
        _ => null,
    };

    /// <summary>An S-101 area, with its attributes read from its feature record.</summary>
    internal static RouteArea S101Area(
        RouteAreaKind kind,
        string featureType,
        IReadOnlyList<SurfacePart> surfaces,
        RouteFindingFeature feature,
        RouteChartCell cell,
        S101Document document,
        S101FeatureRecord? record)
    {
        var rows = record is null ? [] : Rows(document, record);
        var categoryCodes = kind switch
        {
            RouteAreaKind.MilitaryPracticeArea => new[] { "categoryOfMilitaryPracticeArea", "CATMPA" },
            RouteAreaKind.AnchorageArea => ["categoryOfAnchorage", "CATACH"],
            RouteAreaKind.RestrictedArea => ["categoryOfRestrictedArea", "CATREA"],
            _ => [],
        };
        return new RouteArea(RouteAreaProduct.S101, kind, featureType, surfaces, feature)
        {
            Name = Name(rows),
            Restrictions = Codes(rows, "restriction", "RESTRN"),
            Categories = categoryCodes.Length == 0 ? [] : Codes(rows, categoryCodes),
            FixedDateRange = Ranges(rows, "fixedDateRange", "DATSTA", "DATEND").FirstOrDefault(),
            PeriodicDateRanges = Ranges(rows, "periodicDateRange", "PERSTA", "PEREND"),
            Cell = cell,
        };
    }

    // One attribute row: its code, value and parent row (1-based; 0 for top level).
    private readonly record struct Row(string Code, string Value, int Parent);

    private static List<Row> Rows(S101Document document, S101FeatureRecord record) =>
        record.Attributes
            .Select(a => new Row(
                document.AttributeTypeCatalogue.TryGetValue(a.NumericCode, out var name) ? name : a.NumericCode.ToString(CultureInfo.InvariantCulture),
                a.Value ?? string.Empty,
                a.ParentIndex))
            .ToList();

    private static string? Name(List<Row> rows)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Code != "featureName" || rows[i].Parent != 0)
            {
                continue;
            }

            if (Sub(rows, i, "name") is { Length: > 0 } name)
            {
                return name;
            }
        }

        return rows.FirstOrDefault(r => r.Code is "OBJNAM" && r.Value.Length > 0).Value;
    }

    // Every value of the first code present; S-57 lists ("7,8") are split.
    private static IReadOnlyList<int> Codes(List<Row> rows, params string[] codes)
    {
        foreach (var code in codes)
        {
            var values = rows
                .Where(r => r.Code == code && r.Parent == 0)
                .SelectMany(r => r.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Select(v => int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null)
                .OfType<int>()
                .Distinct()
                .ToList();
            if (values.Count > 0)
            {
                return values;
            }
        }

        return [];
    }

    private static List<RouteAreaDateRange> Ranges(List<Row> rows, string complex, string s57Start, string s57End)
    {
        var ranges = new List<RouteAreaDateRange>();
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Code != complex || rows[i].Parent != 0)
            {
                continue;
            }

            var range = new RouteAreaDateRange(Sub(rows, i, "dateStart"), Sub(rows, i, "dateEnd"), Sub(rows, i, "timeOfDayStart"), Sub(rows, i, "timeOfDayEnd"));
            if (range.DateStart is not null || range.DateEnd is not null)
            {
                ranges.Add(range);
            }
        }

        if (ranges.Count == 0)
        {
            var start = rows.FirstOrDefault(r => r.Code == s57Start && r.Value.Length > 0).Value;
            var end = rows.FirstOrDefault(r => r.Code == s57End && r.Value.Length > 0).Value;
            if (start is not null || end is not null)
            {
                ranges.Add(new RouteAreaDateRange(start, end));
            }
        }

        return ranges;
    }

    // A sub-attribute of the complex attribute at row index `parent`.
    private static string? Sub(List<Row> rows, int parent, string code)
    {
        foreach (var row in rows)
        {
            if (row.Parent == parent + 1 && row.Code == code && row.Value.Length > 0)
            {
                return row.Value;
            }
        }

        return null;
    }

    private static List<(RouteArea, BoundingBox)> ReadS122(List<LoadedDataset> datasets)
    {
        var result = new List<(RouteArea, BoundingBox)>();
        foreach (var dataset in datasets)
        {
            S122MarineProtectedAreaDataset typed;
            try
            {
                typed = S122MarineProtectedAreaDataset.From(((S122DatasetData)dataset.Data).Model, out _);
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (var feature in typed.Features)
            {
                if (feature is not S122FeatureBase { GeometryKind: S122GeometryKind.Surface, Coordinates.Count: >= 3 } surface)
                {
                    continue;
                }

                RouteAreaKind kind;
                int? restriction, category, iucn = null;
                switch (feature)
                {
                    case S122MarineProtectedArea mpa:
                        (kind, restriction, category, iucn) = (RouteAreaKind.MarineProtectedArea, mpa.Restriction, mpa.CategoryOfRestrictedArea, mpa.CategoryOfMarineProtectedArea);
                        break;
                    case S122RestrictedArea restricted:
                        (kind, restriction, category) = (RouteAreaKind.RestrictedArea, restricted.Restriction, restricted.CategoryOfRestrictedArea);
                        break;
                    default:
                        continue;
                }

                var area = new RouteArea(
                    RouteAreaProduct.S122,
                    kind,
                    surface.FeatureType,
                    [new SurfacePart(surface.Coordinates, surface.InteriorRings)],
                    new RouteFindingFeature(dataset.Id.Value, surface.Id))
                {
                    Name = surface.FeatureName,
                    Restrictions = restriction is { } code ? [code] : [],
                    Categories = category is { } cat ? [cat] : [],
                    CategoryOfMarineProtectedArea = iucn,
                    FixedDateRange = surface.FixedDateRange is { } f ? new RouteAreaDateRange(f.DateStart, f.DateEnd) : null,
                    PeriodicDateRanges = surface.PeriodicDateRanges.Select(p => new RouteAreaDateRange(p.DateStart, p.DateEnd)).ToList(),
                };
                result.Add((area, new BoundingBox(
                    surface.Coordinates.Min(p => p.Latitude),
                    surface.Coordinates.Min(p => p.Longitude),
                    surface.Coordinates.Max(p => p.Latitude),
                    surface.Coordinates.Max(p => p.Longitude))));
            }
        }

        return result;
    }
}
