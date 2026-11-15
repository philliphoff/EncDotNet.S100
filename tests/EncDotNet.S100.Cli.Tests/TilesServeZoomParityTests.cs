using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for WMS and WMTS zoom parity (issue #1502): at the same scale, WMS
/// GetMap draws the tiles of the tile matrix a WMTS client shows, so both
/// show the same features.
/// </summary>
public sealed class TilesServeZoomParityTests
{
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";

    /// <summary>Opaque tiles at zooms 0 to 18 that record the zooms read.</summary>
    private sealed class RecordingSource : ITileSource
    {
        private readonly byte[] _tile = Png();

        public string Path => "recording";

        public TileImageFormat Format => TileImageFormat.Png;

        public List<int> ZoomsRead { get; } = [];

        public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken)
        {
            lock (ZoomsRead)
                ZoomsRead.Add(zoom);
            return ValueTask.FromResult<byte[]?>(_tile);
        }

        public JsonObject ToTileJson(string? palette, DateTime? time = null) => new()
        {
            ["name"] = "Recording",
            ["minzoom"] = 0,
            ["maxzoom"] = 18,
            ["tileSize"] = 256,
        };

        private static byte[] Png()
        {
            using var bitmap = new SKBitmap(256, 256);
            bitmap.Erase(SKColors.Blue);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    [Theory]
    [InlineData(23374)] // ArcGIS Pro's scale in issue #1502: WMTS zoom 14.
    [InlineData(18056)] // ArcGIS Pro's zoom 15 scale, rounded.
    [InlineData(30000)]
    [InlineData(100000)]
    [InlineData(1500000)]
    public async Task GetMap_draws_the_tile_matrix_a_wmts_client_shows_at_the_same_scale(double scale)
    {
        var source = new RecordingSource();
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        // The tile matrices the WMTS capabilities advertise, as a client reads
        // them: ScaleDenominator × 0.28 mm a pixel, in metres per pixel.
        var capabilities = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "wmts/1.0.0/WMTSCapabilities.xml"), ct));
        var matrices = capabilities.Descendants(Wmts + "TileMatrixSet").Elements(Wmts + "TileMatrix")
            .Select(matrix => (
                Zoom: int.Parse(matrix.Element(XNamespace.Get("http://www.opengis.net/ows/1.1") + "Identifier")!.Value, CultureInfo.InvariantCulture),
                Resolution: double.Parse(matrix.Element(Wmts + "ScaleDenominator")!.Value, CultureInfo.InvariantCulture) * 0.00028))
            .ToList();
        Assert.NotEmpty(matrices);

        // A 96 DPI client at this scale shows the finest tile matrix no finer
        // than its view (allowing for a rounded scale), enlarged.
        double metresPerPixel = scale * 0.0254 / 96;
        int wmtsZoom = matrices.Where(matrix => matrix.Resolution >= metresPerPixel * 0.994).Max(matrix => matrix.Zoom);

        // The same view through WMS: 512 pixels across, in Web Mercator.
        const int size = 512;
        double half = metresPerPixel * size / 2;
        var box = string.Create(CultureInfo.InvariantCulture, $"{-half:R},{-half:R},{half:R},{half:R}");
        using var response = await http.GetAsync(new Uri(server.BaseUri,
            $"wms?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=&CRS=EPSG:3857&BBOX={box}&WIDTH={size}&HEIGHT={size}&FORMAT=image/png"), ct);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync(ct));

        Assert.NotEmpty(source.ZoomsRead);
        Assert.All(source.ZoomsRead, zoom => Assert.Equal(wmtsZoom, zoom));
    }
}
