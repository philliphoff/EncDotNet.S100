using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Adds datasets to and removes them from a running <c>tiles serve</c>
/// (<c>--manage-datasets</c>, issue #1494), through <c>POST</c> and
/// <c>DELETE</c> <c>datasets</c>. A <c>GET</c> there is the dataset
/// metadata document (issue #1504), which describes whichever set serves.
/// Thread-safe: changes are made one at a time. Datasets can be added to a
/// WMTS layer named in the request (issue #1495).
/// </summary>
/// <remarks>
/// <para>
/// A change opens and portrays the new set of datasets while the current one
/// keeps serving, then switches requests over (<see cref="RefreshingTileSource.ReplaceAsync"/>),
/// so the request that made it returns once the new set serves. If the new
/// set can't be opened, the current one keeps serving. The TileJSON, WMTS and
/// WMS documents describe whichever set serves when they are asked for.
/// Every dataset can be removed: the server then serves none
/// (<see cref="NoDatasetsTileSource"/>, issue #1601) until one is added again.
/// </para>
/// <para>
/// Tiles are keyed by the datasets that reach them (<see cref="DatasetTileKeys"/>)
/// in memory and on disk, and the latitude scales are measured at is kept
/// from the first set, so the tiles of datasets still served stay cached.
/// </para>
/// </remarks>
internal sealed class ServedDatasets
{
    /// <summary>The path of the datasets under the server's base URL.</summary>
    public const string Path = "datasets";

    /// <summary>The largest request body accepted, in bytes.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    private readonly RefreshingTileSource _source;
    private readonly Func<DatasetChanges, RenderedTileSource?> _open;
    private readonly Func<DatasetChanges, string> _fingerprint;
    private readonly SemaphoreSlim _editing = new(1, 1);
    private DatasetChanges _changes = DatasetChanges.None;

    /// <param name="source">The source serving the datasets.</param>
    /// <param name="open">Opens the start-up datasets with changes; <see langword="null"/> when they can't be (having said why).</param>
    /// <param name="fingerprint">The fingerprint of the start-up datasets with changes.</param>
    public ServedDatasets(RefreshingTileSource source, Func<DatasetChanges, RenderedTileSource?> open, Func<DatasetChanges, string> fingerprint)
    {
        _source = source;
        _open = open;
        _fingerprint = fingerprint;
    }

    /// <summary>The changes to the start-up datasets that serve now.</summary>
    public DatasetChanges Changes => Volatile.Read(ref _changes);

    /// <summary>The datasets served now, in input order.</summary>
    public IReadOnlyList<(string Path, string Name, string Spec)> Datasets
    {
        get
        {
            if (_source.Current?.Session is not { } session)
                return [];
            return [.. session.Paths.Select((path, i) => (path, session.Names[i], session.Specs[i]))];
        }
    }

    /// <summary>The outcome of a change: an HTTP status, and why it failed.</summary>
    public readonly record struct Result(int Status, string? Error = null)
    {
        public bool Succeeded => Error is null;
    }

    /// <summary>
    /// Serves <paramref name="paths"/> as well; those already served are left
    /// as they are, except that with <paramref name="layer"/> every one of
    /// them is put in that WMTS layer (issue #1495).
    /// </summary>
    public async Task<Result> AddAsync(IReadOnlyList<string> paths, string? layer = null)
    {
        if (layer is not null && !TileLayers.IsValidId(layer))
        {
            return new Result(StatusCodes.Status400BadRequest,
                $"'{layer}' can't name a layer: use 1 to 64 letters, digits, '_', '-' and '.', starting with a letter or digit, other than '{WmtsService.LayerIdentifier}'.");
        }

        var full = new List<string>();
        foreach (var path in paths)
        {
            string resolved;
            try
            {
                resolved = System.IO.Path.GetFullPath(path);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return new Result(StatusCodes.Status400BadRequest, $"'{path}' is not a valid path.");
            }

            if (!File.Exists(resolved))
                return new Result(StatusCodes.Status400BadRequest, $"'{path}' is not a file that exists.");
            if (DatasetPipelineFactory.DetectProductSpec(resolved) is null)
                return new Result(StatusCodes.Status400BadRequest, $"'{path}' is not a dataset this tool can read.");
            full.Add(resolved);
        }

        return await ChangeAsync(changes =>
        {
            var served = Datasets.Select(d => d.Path).ToHashSet(DatasetChanges.PathComparer);
            foreach (var path in full.Where(p => !served.Contains(p)))
                changes = changes.WithAdded(path);
            if (layer is not null)
            {
                foreach (var path in full)
                    changes = changes.WithLayer(path, layer);
            }

            return (changes, null, false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Stops serving <paramref name="paths"/>, which must be served now. With
    /// every dataset removed the server keeps running and serves none (issue #1601).
    /// </summary>
    public async Task<Result> RemoveAsync(IReadOnlyList<string> paths)
    {
        return await ChangeAsync(changes =>
        {
            var served = Datasets.Select(d => d.Path).ToHashSet(DatasetChanges.PathComparer);
            var removing = new HashSet<string>(DatasetChanges.PathComparer);
            foreach (var path in paths)
            {
                string full;
                try
                {
                    full = System.IO.Path.GetFullPath(path);
                }
                catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
                {
                    return (changes, new Result(StatusCodes.Status400BadRequest, $"'{path}' is not a valid path."), false);
                }

                if (!served.Contains(full))
                    return (changes, new Result(StatusCodes.Status404NotFound, $"'{path}' is not served."), false);
                removing.Add(full);
            }

            foreach (var path in removing)
                changes = changes.WithRemoved(path);
            return (changes, null, removing.Count >= served.Count);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Works out new changes from the current ones, then opens the datasets
    /// with them and serves those, or serves none when they leave none
    /// (<c>ServesNone</c>); nothing changes on failure.
    /// </summary>
    private async Task<Result> ChangeAsync(Func<DatasetChanges, (DatasetChanges Changes, Result? Refused, bool ServesNone)> change)
    {
        await _editing.WaitAsync().ConfigureAwait(false);
        try
        {
            var current = Changes;
            var (next, refused, removesAll) = change(current);
            if (refused is { } r)
                return r;
            if (next == current || next.SameAs(current))
                return new Result(StatusCodes.Status200OK);

            // Fingerprinted before opening, as at start-up: a file changed
            // meanwhile is then seen by --refresh as a change.
            var fingerprint = _fingerprint(next);
            bool replaced;
            try
            {
                // With every dataset removed, nothing is opened: the server
                // serves none until one is added (issue #1601).
                replaced = removesAll
                    ? await _source.ServeNothingAsync(fingerprint, () => Volatile.Write(ref _changes, next)).ConfigureAwait(false)
                    : await _source.ReplaceAsync(() => _open(next), fingerprint, () => Volatile.Write(ref _changes, next)).ConfigureAwait(false);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                return new Result(StatusCodes.Status422UnprocessableEntity, $"The datasets couldn't be opened: {e.Message}");
            }

            // The published revision (issue #1497) follows: it is the new
            // set's fingerprint, which includes the changes.
            return replaced
                ? new Result(StatusCodes.Status200OK)
                : new Result(StatusCodes.Status422UnprocessableEntity, "The datasets couldn't be opened; the server's output says why. The previous datasets are still served.");
        }
        finally
        {
            _editing.Release();
        }
    }

    /// <summary>
    /// Maps <c>POST</c> (add) and <c>DELETE</c> (remove) <c>datasets</c>
    /// on <paramref name="app"/>. A change's body is JSON,
    /// <c>{"paths": ["…"]}</c> (or <c>{"path": "…"}</c>), sent as
    /// <c>application/json</c>; an addition may name the WMTS layer to put the
    /// datasets in, <c>"layer": "…"</c>. Every response lists the datasets then served.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <param name="datasets">The datasets served.</param>
    public static void Map(WebApplication app, ServedDatasets datasets)
    {
        app.MapPost("/" + Path, context => ChangeAsync(context, datasets, datasets.AddAsync));
        app.MapDelete("/" + Path, context => ChangeAsync(context, datasets, RemoveAsync));

        // A removal names no layer.
        Task<Result> RemoveAsync(IReadOnlyList<string> paths, string? layer) => datasets.RemoveAsync(paths);
    }

    private static async Task ChangeAsync(HttpContext context, ServedDatasets datasets, Func<IReadOnlyList<string>, string?, Task<Result>> change)
    {
        // JSON only: a browser can't send it from another site without asking first (CORS).
        if (!context.Request.HasJsonContentType())
        {
            await WriteAsync(context, datasets, StatusCodes.Status415UnsupportedMediaType, "Send the paths as application/json: {\"paths\": [\"…\"]}.").ConfigureAwait(false);
            return;
        }

        if (context.Request.ContentLength > MaxBodyBytes)
        {
            await WriteAsync(context, datasets, StatusCodes.Status413PayloadTooLarge, $"The request is larger than {MaxBodyBytes} bytes.").ConfigureAwait(false);
            return;
        }

        var body = new byte[MaxBodyBytes + 1];
        int read = 0, n;
        while (read < body.Length && (n = await context.Request.Body.ReadAsync(body.AsMemory(read), context.RequestAborted).ConfigureAwait(false)) > 0)
            read += n;
        if (read > MaxBodyBytes)
        {
            await WriteAsync(context, datasets, StatusCodes.Status413PayloadTooLarge, $"The request is larger than {MaxBodyBytes} bytes.").ConfigureAwait(false);
            return;
        }

        if (!TryParsePaths(body.AsSpan(0, read), out var paths, out var error))
        {
            await WriteAsync(context, datasets, StatusCodes.Status400BadRequest, error).ConfigureAwait(false);
            return;
        }

        if (!TryParseLayer(body.AsSpan(0, read), out var layer))
        {
            await WriteAsync(context, datasets, StatusCodes.Status400BadRequest, "The layer is a string: {\"paths\": [\"…\"], \"layer\": \"…\"}.").ConfigureAwait(false);
            return;
        }

        var result = await change(paths, layer).ConfigureAwait(false);
        await WriteAsync(context, datasets, result.Status, result.Error).ConfigureAwait(false);
    }

    /// <summary>Reads <c>{"paths": ["…", …]}</c> or <c>{"path": "…"}</c>.</summary>
    internal static bool TryParsePaths(ReadOnlySpan<byte> json, out IReadOnlyList<string> paths, out string error)
    {
        paths = [];
        error = "The body is {\"paths\": [\"…\"]}, the dataset files' paths on the server.";
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject body)
            return false;

        var list = new List<string>();
        if (body["path"] is JsonValue single && single.TryGetValue<string>(out var one))
            list.Add(one);
        if (body["paths"] is JsonArray many)
        {
            foreach (var item in many)
            {
                if (item is not JsonValue value || !value.TryGetValue<string>(out var path))
                    return false;
                list.Add(path);
            }
        }

        if (list.Count == 0 || list.Any(string.IsNullOrWhiteSpace))
            return false;

        paths = list;
        return true;
    }

    /// <summary>Reads the optional <c>"layer"</c> of a body <see cref="TryParsePaths"/> read: a string, or absent or null.</summary>
    internal static bool TryParseLayer(ReadOnlySpan<byte> json, out string? layer)
    {
        layer = null;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return false;
        }

        if (node is not JsonObject body || body["layer"] is null)
            return true;
        return body["layer"] is JsonValue value && value.TryGetValue(out layer);
    }

    private static Task WriteAsync(HttpContext context, ServedDatasets datasets, int status, string? error)
    {
        var json = new JsonObject();
        if (error is not null)
            json["error"] = error;
        var current = datasets._source.Current;
        var layers = current is { Layers.Count: > 0 } ? current.DatasetLayers : null;
        json["datasets"] = new JsonArray([.. datasets.Datasets.Select((d, i) =>
        {
            var dataset = new JsonObject
            {
                ["path"] = d.Path,
                ["name"] = d.Name,
                ["spec"] = d.Spec,
            };
            if (layers is not null && i < layers.Count)
                dataset["layer"] = layers[i];
            return (JsonNode?)dataset;
        })]);
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        context.Response.Headers.CacheControl = "no-cache";
        return context.Response.WriteAsync(json.ToJsonString(TileSetMetadata.JsonOptions), context.RequestAborted);
    }
}

/// <summary>
/// What the tiles of <c>tiles serve --manage-datasets</c> share across changes
/// to the datasets (issue #1494): the memory cache, and the latitude the zoom
/// levels' scales are measured at, kept from the first datasets so adding one
/// elsewhere doesn't change how the others draw.
/// </summary>
/// <param name="memoryBytes">The most encoded tile bytes kept in memory.</param>
internal sealed class ManagedDatasetTiles(long memoryBytes)
{
    /// <summary>The memory cache every opening of the datasets uses.</summary>
    public TileCache Memory { get; } = new(memoryBytes);

    /// <summary>The scale latitude of the first datasets opened; <see langword="null"/> before.</summary>
    public double? ReferenceLatitude { get; set; }
}
