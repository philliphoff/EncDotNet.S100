using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for serving a PMTiles archive's whole file with range requests
/// (issue #1590): a PMTiles client reads tiles from <c>archive.pmtiles</c>
/// as it would from a static host, and gets the tiles the XYZ route serves.
/// </summary>
public sealed class TilesServePmTilesArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-archive-" + Guid.NewGuid().ToString("N"));

    public TilesServePmTilesArchiveTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly (int Zoom, int X, int Y)[] Tiles = [(1, 0, 0), (1, 1, 1), (2, 1, 0), (2, 1, 3), (2, 3, 2), (3, 5, 6)];

    private static byte[] Content(int zoom, int x, int y, string version) =>
        Encoding.ASCII.GetBytes($"tile {zoom}/{x}/{y}{version}");

    private static TileSetMetadata Metadata => new()
    {
        Name = "Archive",
        Description = "Tiles for the archive route",
        Format = TileImageFormat.Png,
        MinZoom = 1,
        MaxZoom = 3,
        Bounds = (-10, -5, 10, 5),
        TilePixelSize = 256,
        Settings = new Dictionary<string, string>(),
    };

    private string Write(TileContainer container, string version = "")
    {
        var path = Path.Combine(_root, container == TileContainer.PmTiles ? "set.pmtiles" : "set.mbtiles");
        using ITileSink sink = container == TileContainer.PmTiles ? new PmTilesTileSink(path) : new MbTilesTileSink(path);
        foreach (var (zoom, x, y) in Tiles)
            sink.Write(zoom, x, y, Content(zoom, x, y, version));
        sink.Complete(Metadata);
        return path;
    }

    [Fact]
    public async Task A_pmtiles_client_reads_the_tiles_the_xyz_route_serves()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Write(TileContainer.PmTiles);
        var source = TileSource.Open(path);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "secret-token", viewer: false, cancellationToken: ct);
        using var http = new HttpClient();
        var archive = new Uri(server.BaseUri, TileServer.PmTilesArchivePath);

        foreach (var (zoom, x, y) in Tiles)
        {
            var viaArchive = await ReadTileAsync(http, archive, zoom, x, y, ct);
            var viaXyz = await http.GetByteArrayAsync(new Uri(server.BaseUri, $"{zoom}/{x}/{y}.png"), ct);
            Assert.Equal(viaXyz, viaArchive);
        }

        Assert.Null(await ReadTileAsync(http, archive, 2, 0, 0, ct));

        // The whole file, and its headers without a body.
        using (var whole = await http.GetAsync(archive, ct))
        {
            whole.EnsureSuccessStatusCode();
            Assert.Equal(TileServer.PmTilesContentType, whole.Content.Headers.ContentType?.MediaType);
            Assert.Equal("bytes", whole.Headers.AcceptRanges.Single());
            Assert.Equal("*", whole.Headers.GetValues("Access-Control-Allow-Origin").Single());
            Assert.NotNull(whole.Headers.ETag);
            Assert.NotNull(whole.Content.Headers.LastModified);
            Assert.Equal(await File.ReadAllBytesAsync(path, ct), await whole.Content.ReadAsByteArrayAsync(ct));
        }

        using (var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, archive), ct))
        {
            head.EnsureSuccessStatusCode();
            Assert.Equal(new FileInfo(path).Length, head.Content.Headers.ContentLength);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync(ct));
        }

        // Under the token only.
        Assert.Equal(HttpStatusCode.NotFound,
            (await http.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/{TileServer.PmTilesArchivePath}"), ct)).StatusCode);
    }

    [Fact]
    public async Task The_tilejson_and_ready_line_point_at_the_archive()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = TileSource.Open(Write(TileContainer.PmTiles));
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, viewer: false, cancellationToken: ct);
        using var http = new HttpClient();
        var expected = $"http://127.0.0.1:{server.Port}/{TileServer.PmTilesArchivePath}";

        var json = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Equal(expected, (string?)json["s100"]!["pmtilesUrl"]);

        var line = JsonNode.Parse(ServeReadyLine.Format(
            IPAddress.Loopback, server.Port, token: null, source.Format, viewer: false, highDpi: false,
            pmTilesArchive: TileServer.PmTilesArchive(source) is not null))!;
        Assert.Equal(expected, (string?)line["pmtiles"]);
    }

    [Fact]
    public async Task Other_containers_have_no_archive_route()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = TileSource.Open(Write(TileContainer.MbTiles));
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, viewer: false, cancellationToken: ct);
        using var http = new HttpClient();

        Assert.Null(TileServer.PmTilesArchive(source));
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, TileServer.PmTilesArchivePath), ct)).StatusCode);
        var json = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Null(json["s100"]?["pmtilesUrl"]);
        var line = JsonNode.Parse(ServeReadyLine.Format(IPAddress.Loopback, server.Port, null, source.Format, false, false, pmTilesArchive: false))!;
        Assert.Equal(JsonValueKind.Null, line["pmtiles"]?.GetValueKind() ?? JsonValueKind.Null);
    }

    [Fact]
    public async Task A_replaced_archive_changes_its_etag_and_fails_if_range()
    {
        var ct = TestContext.Current.CancellationToken;
        var path = Write(TileContainer.PmTiles);
        var source = TileSource.Open(path);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, viewer: false, cancellationToken: ct);
        using var http = new HttpClient();
        var archive = new Uri(server.BaseUri, TileServer.PmTilesArchivePath);

        EntityTagHeaderValue before;
        using (var first = await GetRangeAsync(http, archive, 0, 126, ct))
            before = first.Headers.ETag!;

        // Revalidates while unchanged.
        using (var conditional = new HttpRequestMessage(HttpMethod.Get, archive))
        {
            conditional.Headers.IfNoneMatch.Add(before);
            using var notModified = await http.SendAsync(conditional, ct);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        }

        // Export again, with different (and longer) tiles.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-5));
        Write(TileContainer.PmTiles, version: " v2");

        // A range conditional on the old ETag gets the whole new file instead,
        // so a client never mixes bytes of the two archives.
        using (var ranged = new HttpRequestMessage(HttpMethod.Get, archive))
        {
            ranged.Headers.Range = new RangeHeaderValue(0, 126);
            ranged.Headers.IfRange = new RangeConditionHeaderValue(before);
            using var response = await http.SendAsync(ranged, ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotEqual(before, response.Headers.ETag);
            Assert.Equal(await File.ReadAllBytesAsync(path, ct), await response.Content.ReadAsByteArrayAsync(ct));
        }

        Assert.Equal(Content(2, 1, 3, " v2"), await ReadTileAsync(http, archive, 2, 1, 3, ct));
    }

    private static async Task<HttpResponseMessage> GetRangeAsync(HttpClient http, Uri uri, long from, long to, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(from, to);
        var response = await http.SendAsync(request, ct);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(to - from + 1, response.Content.Headers.ContentLength);
        return response;
    }

    private static async Task<byte[]> ReadRangeAsync(HttpClient http, Uri uri, long offset, long length, CancellationToken ct)
    {
        using var response = await GetRangeAsync(http, uri, offset, offset + length - 1, ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    /// <summary>
    /// Reads a tile the way a PMTiles v3 client does, from the spec rather than
    /// the server's own reader: the header, the root directory (and a leaf, if
    /// any), then the tile, each with a range request.
    /// </summary>
    private static async Task<byte[]?> ReadTileAsync(HttpClient http, Uri archive, int zoom, int x, int y, CancellationToken ct)
    {
        var header = await ReadRangeAsync(http, archive, 0, 127, ct);
        Assert.Equal("PMTiles", Encoding.ASCII.GetString(header, 0, 7));
        Assert.Equal(3, header[7]);
        ulong U64(int at) => BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(at));
        byte internalCompression = header[97];

        ulong tileId = TileId(zoom, x, y);
        ulong directoryOffset = U64(8), directoryLength = U64(16);
        for (int depth = 0; depth < 4; depth++)
        {
            var raw = await ReadRangeAsync(http, archive, (long)directoryOffset, (long)directoryLength, ct);
            var entries = ParseDirectory(internalCompression == 2 ? Gunzip(raw) : raw);
            var entry = entries.LastOrDefault(e => e.TileId <= tileId);
            if (entry.Length == 0)
                return null;

            if (entry.RunLength > 0)
            {
                return tileId < entry.TileId + entry.RunLength
                    ? await ReadRangeAsync(http, archive, (long)(U64(56) + entry.Offset), entry.Length, ct)
                    : null;
            }

            directoryOffset = U64(40) + entry.Offset;
            directoryLength = entry.Length;
        }

        throw new InvalidDataException("Too many leaf directories.");
    }

    private static byte[] Gunzip(byte[] data)
    {
        using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static List<(ulong TileId, ulong Offset, uint Length, uint RunLength)> ParseDirectory(byte[] data)
    {
        int at = 0;
        ulong Varint()
        {
            ulong value = 0;
            for (int shift = 0; ; shift += 7)
            {
                byte b = data[at++];
                value |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                    return value;
            }
        }

        int count = (int)Varint();
        var ids = new ulong[count];
        var runs = new uint[count];
        var lengths = new uint[count];
        var offsets = new ulong[count];
        ulong last = 0;
        for (int i = 0; i < count; i++)
            ids[i] = last += Varint();
        for (int i = 0; i < count; i++)
            runs[i] = (uint)Varint();
        for (int i = 0; i < count; i++)
            lengths[i] = (uint)Varint();
        for (int i = 0; i < count; i++)
        {
            ulong value = Varint();
            offsets[i] = value == 0 && i > 0 ? offsets[i - 1] + lengths[i - 1] : value - 1;
        }

        return [.. Enumerable.Range(0, count).Select(i => (ids[i], offsets[i], lengths[i], runs[i]))];
    }

    /// <summary>The PMTiles tile ID: the tiles of lower zooms, then the Hilbert index at <paramref name="zoom"/>.</summary>
    private static ulong TileId(int zoom, int x, int y)
    {
        ulong before = 0;
        for (int z = 0; z < zoom; z++)
            before += 1UL << (2 * z);

        ulong n = 1UL << zoom, d = 0, rx, ry, ux = (ulong)x, uy = (ulong)y;
        for (ulong s = n / 2; s > 0; s /= 2)
        {
            rx = (ux & s) > 0 ? 1UL : 0;
            ry = (uy & s) > 0 ? 1UL : 0;
            d += s * s * ((3 * rx) ^ ry);
            if (ry == 0)
            {
                if (rx == 1)
                {
                    ux = s - 1 - ux;
                    uy = s - 1 - uy;
                }

                (ux, uy) = (uy, ux);
            }
        }

        return before + d;
    }
}
