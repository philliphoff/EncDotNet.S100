using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace EncDotNet.S100.WebApp.Services;

/// <summary>Reads what <c>s100 tiles serve</c> publishes: its TileJSON and its <c>/datasets</c> document.</summary>
public sealed class ServeClient(HttpClient http)
{
    public async Task<TileJson> GetTileJsonAsync(Uri server, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync(new Uri(server, "tiles.json"), ServeJson.Default.TileJson, cancellationToken).ConfigureAwait(false)
        ?? throw new InvalidOperationException("tiles.json was empty.");

    /// <summary>The datasets document, or <see langword="null"/> for a built tile set (404).</summary>
    public async Task<DatasetsDoc?> GetDatasetsAsync(Uri server, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(new Uri(server, "datasets"), cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(ServeJson.Default.DatasetsDoc, cancellationToken).ConfigureAwait(false);
    }
}

public sealed record TileJson(
    string? Name,
    int Minzoom,
    int Maxzoom,
    double[]? Bounds,
    double[]? Center,
    TileJsonS100? S100);

public sealed record TileJsonS100(
    string? Palette,
    string[]? Palettes,
    string[]? Times,
    int[]? PixelRatios);

public sealed record DatasetsDoc(DatasetEntry[] Datasets);

public sealed record DatasetEntry(
    string Id,
    string File,
    string Product,
    string? Edition,
    string? Producer,
    double[]? Bounds,
    string[]? Times,
    string? IssueTime,
    string? Layer);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(TileJson))]
[JsonSerializable(typeof(DatasetsDoc))]
internal sealed partial class ServeJson : JsonSerializerContext;
