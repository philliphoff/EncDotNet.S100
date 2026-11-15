using System.Net;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the legend graphics of <c>s100 tiles serve</c> (issue #1501): WMS
/// <c>GetLegendGraphic</c> and the <c>LegendURL</c> of each WMS and WMTS
/// style, drawn from the rows the viewer's Legend tab shows, in each style's
/// palette and mariner settings.
/// </summary>
public sealed class TilesServeLegendTests
{
    private static readonly XNamespace Wms = "http://www.opengis.net/wms";
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";
    private static readonly XNamespace Ows = "http://www.opengis.net/ows/1.1";
    private static readonly XNamespace Ogc = "http://www.opengis.net/ogc";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Sld = "http://www.opengis.net/sld";

    // A small synthetic S-102 grid: its sub-layers carry depth shades and the safety contour.
    private static string S102 => Path.Combine(AppContext.BaseDirectory, "TestData", "S102", "102US004MI1CI262227.h5");

    private static RenderedTileSource OpenRendered()
    {
        var settings = new TilesServeCommand.Settings { Input = S102, Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    private static SKBitmap Decode(byte[] png)
    {
        Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], png.Take(4));
        return SKBitmap.Decode(png);
    }

    [Fact]
    public async Task Each_style_has_a_legend_that_changes_with_the_palette_and_the_settings()
    {
        Assert.SkipUnless(File.Exists(S102), "S-102 fixture not present.");
        using var source = OpenRendered();
        var ct = TestContext.Current.CancellationToken;
        Assert.True(source.HasLegend);

        // The rows are the viewer's: one S-102 legend with the depth shades and the safety contour.
        var legends = source.Legend.Legends(null)!;
        var s102 = Assert.Single(legends, l => l.Spec == "S-102");
        Assert.Equal("Bathymetric surface", s102.Title);
        Assert.Contains(s102.Groups, g => g.Rows.Count > 1);
        Assert.Contains(s102.Rows, r => r.Label == "Safety contour");

        source.MarinerStyles.Register("shades", new MarinerStyle { FourShades = true, SafetyContour = 5, ShallowContour = 2, DeepContour = 10 });
        var images = new Dictionary<string, byte[]>();
        foreach (var style in source.Palettes)
        {
            var png = await source.ReadLegendAsync(style, default, ct);
            Assert.NotNull(png);
            using var bitmap = Decode(png);
            Assert.True(bitmap.Width > 40 && bitmap.Height > 40, $"{style}: {bitmap.Width} × {bitmap.Height}");
            images[style] = png;
        }

        Assert.Equal(["day", "dusk", "night", "day-shades", "dusk-shades", "night-shades"], images.Keys);
        Assert.Equal(images.Count, images.Values.Select(Convert.ToBase64String).Distinct().Count());

        // The default style is the --palette one; an unknown style has none.
        Assert.Equal(images["day"], await source.ReadLegendAsync(null, default, ct));
        Assert.Null(await source.ReadLegendAsync("day-unknown", default, ct));
        Assert.Null(await source.ReadLegendAsync("sepia", default, ct));

        // The settings change the rows, not only their colours.
        Assert.NotEqual(
            source.Legend.Legends("day")!.SelectMany(l => l.Rows).Select(r => r.Label),
            source.Legend.Legends("day-shades")!.SelectMany(l => l.Rows).Select(r => r.Label));
    }

    [Fact]
    public async Task The_advertised_size_is_the_graphic_size()
    {
        Assert.SkipUnless(File.Exists(S102), "S-102 fixture not present.");
        using var source = OpenRendered();
        var ct = TestContext.Current.CancellationToken;

        foreach (var style in source.Palettes)
        {
            using var bitmap = Decode((await source.ReadLegendAsync(style, default, ct))!);
            Assert.Equal((bitmap.Width, bitmap.Height), source.LegendSize(style));
        }

        // Bigger glyphs and a print DPI make a bigger graphic.
        using var normal = Decode((await source.ReadLegendAsync("day", default, ct))!);
        using var glyphs = Decode((await source.ReadLegendAsync("day", new TileLegend.Options(56, 28), ct))!);
        using var print = Decode((await source.ReadLegendAsync("day", new TileLegend.Options(Dpi: 192), ct))!);
        Assert.True(glyphs.Width > normal.Width && glyphs.Height > normal.Height);
        Assert.InRange(print.Width, (2 * normal.Width) - 4, (2 * normal.Width) + 4);
        Assert.InRange(print.Height, (2 * normal.Height) - 4, (2 * normal.Height) + 4);
    }

    [Fact]
    public async Task Capabilities_link_each_style_to_its_legend_graphic()
    {
        Assert.SkipUnless(File.Exists(S102), "S-102 fixture not present.");
        using var source = OpenRendered();
        source.MarinerStyles.Register("k1", new MarinerStyle { SafetyContour = 10 });
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        // WMS: a LegendURL per style, and GetLegendGraphic as an SLD extended operation.
        var wms = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct));
        var operation = wms.Descendants(Sld + "GetLegendGraphic").Single();
        Assert.Equal(["image/png"], operation.Elements(Wms + "Format").Select(f => f.Value));
        var wmsStyles = wms.Descendants(Wms + "Style").ToList();
        Assert.Equal(source.Palettes.Count, wmsStyles.Count);
        foreach (var style in wmsStyles)
        {
            var name = style.Element(Wms + "Name")!.Value;
            var legend = style.Element(Wms + "LegendURL")!;
            Assert.Equal("image/png", legend.Element(Wms + "Format")!.Value);
            var href = (string)legend.Element(Wms + "OnlineResource")!.Attribute(XLink + "href")!;
            Assert.StartsWith(server.BaseUri.AbsoluteUri + "wms?", href, StringComparison.Ordinal);
            Assert.Contains("REQUEST=GetLegendGraphic", href, StringComparison.Ordinal);
            Assert.Contains("STYLE=" + name, href, StringComparison.Ordinal);

            using var response = await http.GetAsync(new Uri(href), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            using var bitmap = Decode(await response.Content.ReadAsByteArrayAsync(ct));
            Assert.Equal(bitmap.Width, (int)legend.Attribute("width")!);
            Assert.Equal(bitmap.Height, (int)legend.Attribute("height")!);
        }

        // WMTS: a LegendURL per style, at the same graphics.
        var wmts = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        var wmtsStyles = wmts.Descendants(Wmts + "Style").ToList();
        Assert.Contains(wmtsStyles, s => s.Element(Ows + "Identifier")!.Value == "night-k1");
        foreach (var style in wmtsStyles)
        {
            var name = style.Element(Ows + "Identifier")!.Value;
            var legend = style.Element(Wmts + "LegendURL")!;
            Assert.Equal("image/png", (string?)legend.Attribute("format"));
            Assert.Equal(
                wmsStyles.Single(s => s.Element(Wms + "Name")!.Value == name).Element(Wms + "LegendURL")!.Element(Wms + "OnlineResource")!.Attribute(XLink + "href")!.Value,
                (string)legend.Attribute(XLink + "href")!);
        }

        Assert.NotEqual(
            await http.GetByteArrayAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetLegendGraphic&LAYER=s100&STYLE=day&FORMAT=image/png"), ct),
            await http.GetByteArrayAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetLegendGraphic&LAYER=s100&STYLE=night&FORMAT=image/png"), ct));
    }

    [Theory]
    [InlineData("STYLE=day", null)]
    [InlineData("LAYER=s100&STYLE=sepia", "StyleNotDefined")]
    [InlineData("LAYER=s100&STYLE=day-unknown", "StyleNotDefined")]
    [InlineData("LAYER=roads", "LayerNotDefined")]
    [InlineData("LAYER=s100&FORMAT=image/gif", "InvalidFormat")]
    [InlineData("LAYER=s100&WIDTH=0", null)]
    [InlineData("LAYER=s100&HEIGHT=9999", null)]
    [InlineData("LAYER=s100&DPI=5", null)]
    public async Task Bad_GetLegendGraphic_requests_answer_a_service_exception(string query, string? code)
    {
        Assert.SkipUnless(File.Exists(S102), "S-102 fixture not present.");
        using var source = OpenRendered();
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await http.GetAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetLegendGraphic&" + query), ct);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var exception = XDocument.Parse(await response.Content.ReadAsStringAsync(ct)).Root!.Element(Ogc + "ServiceException")!;
        Assert.Equal(code, (string?)exception.Attribute("code"));
        Assert.NotEmpty(exception.Value);

        // Parameters are optional where the spec allows: no FORMAT or STYLE draws the default.
        using var plain = await http.GetAsync(new Uri(server.BaseUri, "wms?REQUEST=GetLegendGraphic&LAYER=s100&WIDTH=20&HEIGHT=20&SLD_VERSION=1.1.0"), ct);
        Assert.Equal(HttpStatusCode.OK, plain.StatusCode);
    }

    [Fact]
    public async Task A_built_tile_set_has_no_legend()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiles-legend-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var sink = new XyzDirectoryTileSink(root, TileImageFormat.Png))
            {
                sink.Write(1, 1, 0, [1, 2, 3]);
                sink.Complete(new TileSetMetadata
                {
                    Name = "Built",
                    Description = "Built set",
                    Format = TileImageFormat.Png,
                    MinZoom = 1,
                    MaxZoom = 1,
                    Bounds = (0, 0, 10, 10),
                    TilePixelSize = 256,
                    Settings = new Dictionary<string, string>(),
                });
            }

            await using var server = await TileServer.StartAsync(TileSource.Open(root), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var ct = TestContext.Current.CancellationToken;

            var wms = await http.GetStringAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct);
            Assert.DoesNotContain("LegendURL", wms, StringComparison.Ordinal);
            Assert.DoesNotContain("GetLegendGraphic", wms, StringComparison.Ordinal);
            Assert.DoesNotContain("LegendURL", await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct), StringComparison.Ordinal);

            using var response = await http.GetAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetLegendGraphic&LAYER=s100"), ct);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Contains("OperationNotSupported", await response.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
