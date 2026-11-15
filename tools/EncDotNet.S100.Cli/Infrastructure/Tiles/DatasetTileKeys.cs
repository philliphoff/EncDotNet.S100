using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// Keys a block's tiles by the datasets that reach it, rather than by every
/// dataset served (issue #1494), so that <c>tiles serve --manage-datasets</c>
/// keeps the tiles of datasets that are still served cached when another one
/// is added or removed.
/// </summary>
/// <remarks>
/// <para>
/// A block's key is a fingerprint of the settings that change pixels, the
/// latitude scales are measured at, the zoom level, and the files of each
/// dataset whose extent comes within a tile of the block, in input order. A
/// dataset whose extent isn't known, or crosses the antimeridian, is taken to
/// reach every block. A block clipped by the edge of the tiled area adds its
/// position and size, as the edge moves when datasets change; and one that a
/// coverage dataset (gridded data, sampled for the whole area) reaches adds
/// the area.
/// </para>
/// <para>
/// Each key is 32 lowercase hexadecimal characters, like a
/// <see cref="TileRenderSession.Fingerprint()"/>, and names the disk cache
/// folder of its tiles (<see cref="DiskTileCache"/>).
/// </para>
/// </remarks>
internal sealed class DatasetTileKeys
{
    private readonly string _settings;
    private readonly int _blockSize;
    private readonly TileArea _area;
    private readonly Dataset[] _datasets;
    private readonly ConcurrentDictionary<TileBlock, string> _keys = new();

    /// <param name="session">The opened datasets.</param>
    /// <param name="layout">The area and scale latitude served.</param>
    /// <param name="blockSize">The edge length of a rendered block, in tiles.</param>
    public DatasetTileKeys(TileRenderSession session, TileLayout layout, int blockSize)
    {
        _settings = session.SettingsFingerprint() + "|" + layout.ReferenceLatitude.ToString("R", CultureInfo.InvariantCulture);
        _blockSize = blockSize;
        _area = layout.Area;
        var crs = new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory();
        _datasets = [.. session.Processors.Select((processor, i) => new Dataset(
            TileRenderSession.DatasetFingerprint(session.Paths[i]),
            Extent(processor, crs),
            processor is not IVectorPortrayalSource))];
    }

    /// <summary>The key of <paramref name="block"/>'s tiles.</summary>
    public string For(TileBlock block) => _keys.GetOrAdd(block, Compute);

    /// <summary>The indices of the datasets that reach <paramref name="block"/>, in input order.</summary>
    internal IEnumerable<int> Reaching(TileBlock block)
    {
        double size = XyzTileGrid.TileWorldSize(block.Zoom);
        var (minX, _, _, maxY) = XyzTileGrid.TileWorldBounds(block.Zoom, block.X, block.Y);
        double maxX = minX + (block.Columns * size), minY = maxY - (block.Rows * size);

        // One tile around the block, for symbols and text drawn past a dataset's extent.
        minX -= size;
        minY -= size;
        maxX += size;
        maxY += size;

        for (int i = 0; i < _datasets.Length; i++)
        {
            if (_datasets[i].Extent is not { } extent || Overlaps(extent, minX, minY, maxX, maxY))
                yield return i;
        }
    }

    private string Compute(TileBlock block)
    {
        var text = new StringBuilder(_settings).Append('\n')
            .Append("zoom=").Append(block.Zoom.ToString(CultureInfo.InvariantCulture)).Append('\n');

        bool clipped = block.Columns != _blockSize || block.Rows != _blockSize
            || block.X % _blockSize != 0 || block.Y % _blockSize != 0;
        if (clipped)
            text.Append(CultureInfo.InvariantCulture, $"block={block.X},{block.Y},{block.Columns},{block.Rows}\n");

        bool coverage = false;
        foreach (int i in Reaching(block))
        {
            text.Append("dataset=").Append(_datasets[i].Fingerprint).Append('\n');
            coverage |= _datasets[i].IsCoverage;
        }

        if (coverage)
            text.Append(CultureInfo.InvariantCulture, $"area={_area.MinX:R},{_area.MinY:R},{_area.MaxX:R},{_area.MaxY:R}\n");

        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()));
        return Convert.ToHexStringLower(hash, 0, 16);
    }

    /// <summary>Whether a dataset's EPSG:3857 extent overlaps the box, on either side of the antimeridian.</summary>
    private static bool Overlaps((double MinX, double MinY, double MaxX, double MaxY) extent, double minX, double minY, double maxX, double maxY)
    {
        if (extent.MaxY < minY || extent.MinY > maxY)
            return false;

        double world = 2.0 * XyzTileGrid.Extent;
        for (int shift = -1; shift <= 1; shift++)
        {
            double offset = shift * world;
            if (extent.MaxX + offset >= minX && extent.MinX + offset <= maxX)
                return true;
        }

        return false;
    }

    /// <summary>A dataset's EPSG:3857 extent, or <see langword="null"/> when it may reach anywhere.</summary>
    private static (double MinX, double MinY, double MaxX, double MaxY)? Extent(
        IDatasetProcessor processor, ICrsTransformFactory crs)
    {
        BoundingBox? extent;
        try
        {
            extent = processor.Metadata.GetGeographicExtent(crs);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }

        if (extent is null
            || extent.WestLongitude > extent.EastLongitude
            || extent.WestLongitude < -180 || extent.EastLongitude > 180
            || double.IsNaN(extent.SouthLatitude) || double.IsNaN(extent.NorthLatitude))
        {
            return null;
        }

        var (minX, minY) = WebMercator.FromLonLat(extent.WestLongitude, extent.SouthLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(extent.EastLongitude, extent.NorthLatitude);
        return (minX, minY, maxX, maxY);
    }

    private sealed record Dataset(string Fingerprint, (double MinX, double MinY, double MaxX, double MaxY)? Extent, bool IsCoverage);
}
