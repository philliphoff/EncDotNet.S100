using System.Globalization;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Writes tiles as <c>{z}/{x}/{y}.{ext}</c> files under a directory, plus a
/// <c>tiles.json</c> TileJSON document whose tile URL is relative to it.
/// </summary>
internal sealed class XyzDirectoryTileSink : ITileSink
{
    /// <summary>The TileJSON file written next to the zoom directories.</summary>
    public const string TileJsonFileName = "tiles.json";

    private readonly string _root;
    private readonly string _extension;

    public XyzDirectoryTileSink(string root, TileImageFormat format)
    {
        _root = Path.GetFullPath(root);
        _extension = TileSetMetadata.FormatToken(format);
        Directory.CreateDirectory(_root);
    }

    public void Write(int zoom, int x, int y, byte[] data)
    {
        var directory = Path.Combine(
            _root, zoom.ToString(CultureInfo.InvariantCulture), x.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, $"{y.ToString(CultureInfo.InvariantCulture)}.{_extension}"), data);
    }

    public void Complete(TileSetMetadata metadata)
    {
        var json = metadata.ToTileJson($"{{z}}/{{x}}/{{y}}.{metadata.Extension}");
        File.WriteAllText(
            Path.Combine(_root, TileJsonFileName),
            json.ToJsonString(TileSetMetadata.JsonOptions));
    }

    public void Dispose()
    {
    }
}
