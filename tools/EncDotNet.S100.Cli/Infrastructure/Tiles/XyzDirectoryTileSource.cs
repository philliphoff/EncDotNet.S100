using System.Globalization;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Reads a directory of <c>{z}/{x}/{y}.{ext}</c> files. The tile format and
/// description come from its <c>tiles.json</c> when it has one; otherwise from
/// the files themselves.
/// </summary>
internal sealed class XyzDirectoryTileSource : ITileSetSource
{
    private static readonly string[] Extensions = ["png", "jpg", "jpeg", "webp"];

    private readonly string _extension;

    public XyzDirectoryTileSource(string root)
    {
        Path = System.IO.Path.GetFullPath(root);

        var tileJson = ReadTileJson();
        if (tileJson?["format"] is JsonValue format)
        {
            Format = TileSource.ParseFormat((string?)format, Path);
            _extension = TileSetMetadata.FormatToken(Format);
        }
        else
        {
            _extension = FindExtension()
                ?? throw new InvalidDataException(
                    $"'{Path}' has no tiles.json and no {{z}}/{{x}}/{{y}} PNG, JPEG or WebP tiles.");
            Format = TileSource.ParseFormat(_extension, Path);
        }
    }

    public string Path { get; }

    public TileContainer Container => TileContainer.Xyz;

    public TileImageFormat Format { get; }

    public byte[]? Read(int zoom, int x, int y)
    {
        var file = System.IO.Path.Combine(
            Path,
            zoom.ToString(CultureInfo.InvariantCulture),
            x.ToString(CultureInfo.InvariantCulture),
            y.ToString(CultureInfo.InvariantCulture) + "." + _extension);
        try
        {
            return File.ReadAllBytes(file);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public JsonObject ToTileJson()
    {
        var json = ReadTileJson() ?? new JsonObject();
        json.Remove("tiles");
        json["tilejson"] = "3.0.0";
        json["scheme"] = "xyz";
        json["format"] = TileSetMetadata.FormatToken(Format);
        json["name"] ??= System.IO.Path.GetFileName(Path);

        if (json["minzoom"] is null || json["maxzoom"] is null)
        {
            var zooms = ZoomLevels();
            if (zooms.Count > 0)
            {
                json["minzoom"] ??= zooms.Min();
                json["maxzoom"] ??= zooms.Max();
            }
        }

        return json;
    }

    private JsonObject? ReadTileJson()
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(Path, XyzDirectoryTileSink.TileJsonFileName))) as JsonObject;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new InvalidDataException($"'{Path}' has a tiles.json that isn't valid JSON: {e.Message}", e);
        }
    }

    private List<int> ZoomLevels() =>
        Directory.EnumerateDirectories(Path)
            .Select(d => int.TryParse(System.IO.Path.GetFileName(d), NumberStyles.None, CultureInfo.InvariantCulture, out int z) ? z : -1)
            .Where(z => z is >= 0 and <= 26)
            .ToList();

    /// <summary>The extension of the first tile found under the lowest zoom level.</summary>
    private string? FindExtension()
    {
        foreach (int zoom in ZoomLevels().Order())
        {
            var file = Directory
                .EnumerateFiles(System.IO.Path.Combine(Path, zoom.ToString(CultureInfo.InvariantCulture)), "*", SearchOption.AllDirectories)
                .FirstOrDefault(f => Extensions.Contains(System.IO.Path.GetExtension(f).TrimStart('.').ToLowerInvariant()));
            if (file is not null)
                return System.IO.Path.GetExtension(file).TrimStart('.');
        }

        return null;
    }
}
