using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using Microsoft.AspNetCore.Http;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The <c>/datasets</c> document of <c>tiles serve</c> (issue #1504): what a
/// GIS client needs to know about each dataset the tiles are rendered from,
/// without opening the datasets itself. Built from the datasets served at
/// each request, so it follows a reload.
/// </summary>
/// <remarks>
/// Per dataset: an id and the file name, the product and its edition, the
/// producer code, the WGS-84 bounds and a coverage polygon (GeoJSON), the time
/// steps, the issue time and data dynamicity of time-varying data, and the
/// not-for-navigation flag. Bounds are per dataset, not only their union: the
/// union of a harbour cell and a distant water-level grid frames empty ocean.
/// </remarks>
internal static partial class DatasetsDocument
{
    /// <summary>The path of the document under the server's base URL.</summary>
    public const string Path = "datasets";

    private static readonly ProjNetCrsTransformFactory Crs = new();

    /// <summary>Answers a <c>GET /datasets</c>: the document, or 404 for a built tile set.</summary>
    public static Task ServeAsync(HttpContext context, ITileSource source)
    {
        var json = source.ToDatasetsJson();
        if (json is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return Task.CompletedTask;
        }

        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-cache";
        return context.Response.WriteAsync(json.ToJsonString(TileSetMetadata.JsonOptions), context.RequestAborted);
    }

    /// <summary>Describes the datasets of <paramref name="session"/>, in input order.</summary>
    /// <param name="session">The datasets served.</param>
    /// <param name="scene">A scene of all of them, for their declared data coverage; <see langword="null"/> to use their bounds alone.</param>
    /// <param name="layers">
    /// The WMTS layer each dataset is in besides the layer of every dataset
    /// (issue #1495), by input index; <see langword="null"/> when there are no such layers.
    /// </param>
    public static JsonObject Build(TileRenderSession session, TileScene? scene, IReadOnlyList<string?>? layers = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        var processors = session.Processors;
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var datasets = new JsonArray();
        for (int i = 0; i < processors.Count; i++)
        {
            var processor = processors[i];
            var file = session.Names[i];
            var bounds = GeographicExtent(processor);
            var declared = scene?.CoverageOf(processor) ?? [];
            var timeAware = processor as ITimeAwareDatasetProcessor;
            var times = timeAware?.AvailableTimes ?? [];

            datasets.Add((JsonNode)new JsonObject
            {
                ["id"] = UniqueId(ids, file),
                ["file"] = file,
                ["product"] = processor.Spec.Name,
                ["edition"] = processor.Spec.Edition == default ? null : processor.Spec.Edition.ToString(),
                ["producer"] = Producer(processor.Spec.Name, file),
                ["bounds"] = bounds is null
                    ? null
                    : new JsonArray(Round(bounds.WestLongitude), Round(bounds.SouthLatitude), Round(bounds.EastLongitude), Round(bounds.NorthLatitude)),
                ["coverage"] = declared.Count > 0 ? Coverage(declared) : bounds is null ? null : Coverage(bounds),
                ["coverageSource"] = declared.Count > 0 ? "dataCoverage" : bounds is null ? null : "bounds",
                ["times"] = new JsonArray([.. times.Select(TileRenderSession.AsUtc).Distinct().Order().Select(t => (JsonNode?)TileRenderSession.FormatTime(t))]),
                ["issueTime"] = timeAware?.IssueTime is { } issued ? TileRenderSession.FormatTime(issued) : null,
                ["dataDynamicity"] = timeAware?.DataDynamicity is { } kind ? Dynamicity(kind) : null,
                ["notForNavigation"] = true,
            });

            // Only when there are layers, so the document is otherwise as before.
            if (layers is not null)
                datasets[^1]!["layer"] = layers[i];
        }

        return new JsonObject
        {
            ["notForNavigation"] = true,
            ["datasets"] = datasets,
        };
    }

    /// <summary>
    /// The producer code a dataset's file name gives by its product's naming
    /// convention: the four characters after the product number for S-100
    /// products (<c>111US00_…</c> is <c>US00</c>), the first two for S-57
    /// cells (<c>US5MA1BO.000</c> is <c>US</c>); <see langword="null"/> for a
    /// name that doesn't follow it.
    /// </summary>
    /// <param name="product">The product specification, e.g. <c>S-104</c>.</param>
    /// <param name="fileName">The dataset's file name.</param>
    internal static string? Producer(string product, string fileName)
    {
        if (string.Equals(product, "S-57", StringComparison.OrdinalIgnoreCase))
            return S57Name().Match(fileName) is { Success: true } cell ? cell.Groups["producer"].Value.ToUpperInvariant() : null;

        var name = S100Name().Match(fileName);
        return name.Success && string.Equals("S-" + name.Groups["product"].Value, product, StringComparison.OrdinalIgnoreCase)
            ? name.Groups["producer"].Value.ToUpperInvariant()
            : null;
    }

    /// <summary>The data dynamicity: the S-104 code, its name and the label hosts show for it.</summary>
    internal static JsonObject Dynamicity(DataDynamicity kind) => new()
    {
        ["code"] = (int)kind,
        ["value"] = kind switch
        {
            DataDynamicity.Observation => "observation",
            DataDynamicity.AstronomicalPrediction => "astronomicalPrediction",
            DataDynamicity.AnalysisOrHybrid => "analysisOrHybrid",
            DataDynamicity.HydrodynamicHindcast => "hydrodynamicHindcast",
            _ => "hydrodynamicForecast",
        },
        // The viewer's labels (#1505), as SoundCharts for ArcGIS shows them.
        ["label"] = kind switch
        {
            DataDynamicity.Observation => "Observed",
            DataDynamicity.AstronomicalPrediction => "Predicted",
            DataDynamicity.AnalysisOrHybrid => "Analysis",
            DataDynamicity.HydrodynamicHindcast => "Hindcast",
            _ => "Forecast",
        },
    };

    /// <summary>The declared data coverage as a GeoJSON MultiPolygon, holes included.</summary>
    internal static JsonObject Coverage(IReadOnlyList<CoverageArea> areas) =>
        MultiPolygon(areas.Select(a => (IEnumerable<IReadOnlyList<GeoPosition>>)[a.ExteriorRing, .. a.InteriorRings]));

    /// <summary>
    /// Bounds as a GeoJSON MultiPolygon: one rectangle, or two when they cross
    /// the antimeridian (west east of east).
    /// </summary>
    internal static JsonObject Coverage(BoundingBox bounds)
    {
        double west = bounds.WestLongitude, south = bounds.SouthLatitude, east = bounds.EastLongitude, north = bounds.NorthLatitude;
        IEnumerable<IReadOnlyList<GeoPosition>> Box(double w, double e) =>
            [[new(south, w), new(south, e), new(north, e), new(north, w), new(south, w)]];

        return west > east
            ? MultiPolygon([Box(west, 180), Box(-180, east)])
            : MultiPolygon([Box(west, east)]);
    }

    private static JsonObject MultiPolygon(IEnumerable<IEnumerable<IReadOnlyList<GeoPosition>>> polygons) => new()
    {
        ["type"] = "MultiPolygon",
        ["coordinates"] = new JsonArray([.. polygons.Select(rings => (JsonNode?)new JsonArray([.. rings.Select(Ring)]))]),
    };

    /// <summary>A GeoJSON linear ring, <c>[longitude, latitude]</c> positions, closed.</summary>
    private static JsonNode? Ring(IReadOnlyList<GeoPosition> ring)
    {
        var positions = new JsonArray([.. ring.Select(p => (JsonNode?)new JsonArray(Round(p.Longitude), Round(p.Latitude)))]);
        if (ring.Count > 0 && ring[0] != ring[^1])
            positions.Add((JsonNode)new JsonArray(Round(ring[0].Longitude), Round(ring[0].Latitude)));
        return positions;
    }

    private static BoundingBox? GeographicExtent(IDatasetProcessor processor)
    {
        try
        {
            return processor.Metadata.GetGeographicExtent(Crs);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The file name without its extension, with <c>-2</c>, <c>-3</c>, … after names already taken.</summary>
    private static string UniqueId(HashSet<string> taken, string file)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(file);
        if (stem.Length == 0)
            stem = file;

        var id = stem;
        for (int n = 2; !taken.Add(id); n++)
            id = stem + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return id;
    }

    // Seven decimals is about a centimetre.
    private static double Round(double degrees) => Math.Round(degrees, 7);

    [GeneratedRegex(@"^(?<product>[0-9]{3})(?<producer>[A-Z0-9]{4})", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex S100Name();

    [GeneratedRegex(@"^(?<producer>[A-Z0-9]{2})[1-6][A-Z0-9]{5}\.[0-9]{3}$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex S57Name();
}
