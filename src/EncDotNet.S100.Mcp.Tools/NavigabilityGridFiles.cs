using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>Writes a <see cref="NavigabilityGrid"/> as a NumPy <c>.npy</c> file or raw bytes.</summary>
internal static class NavigabilityGridFiles
{
    /// <summary>The class's name in the layout notes.</summary>
    /// <param name="cellClass">The class.</param>
    /// <returns>The name.</returns>
    public static string ClassName(NavigabilityCellClass cellClass) => cellClass switch
    {
        NavigabilityCellClass.Uncharted => "uncharted",
        NavigabilityCellClass.Land => "land",
        NavigabilityCellClass.Navigable => "navigable",
        NavigabilityCellClass.Shoal => "shoal",
        NavigabilityCellClass.Hazard => "hazard",
        _ => "no_depth",
    };

    /// <summary>
    /// A NumPy 1.0 <c>.npy</c> file: a C-order structured array of shape
    /// (rows, columns) with fields <c>depth_dm</c> (little-endian int16) and
    /// <c>class</c> (uint8), row 0 south.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <returns>The file's bytes.</returns>
    public static byte[] Npy(NavigabilityGrid grid)
    {
        var dict = string.Create(
            CultureInfo.InvariantCulture,
            $"{{'descr': [('depth_dm', '<i2'), ('class', '|u1')], 'fortran_order': False, 'shape': ({grid.Rows}, {grid.Columns}), }}");

        // Magic (6) + version (2) + header length (2) + header, padded with
        // spaces so the data starts on a 64-byte boundary, ending in '\n'.
        var unpadded = 10 + dict.Length + 1;
        var header = dict + new string(' ', (64 - (unpadded % 64)) % 64) + "\n";
        var count = grid.Rows * grid.Columns;
        var bytes = new byte[10 + header.Length + (count * 3)];
        bytes[0] = 0x93;
        Encoding.ASCII.GetBytes("NUMPY", bytes.AsSpan(1));
        bytes[6] = 1;
        bytes[7] = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(8), (ushort)header.Length);
        Encoding.ASCII.GetBytes(header, bytes.AsSpan(10));

        var data = bytes.AsSpan(10 + header.Length);
        var depths = grid.LeastDepths;
        var classes = grid.Classes;
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(data[(i * 3)..], Decimetres(depths[i]));
            data[(i * 3) + 2] = (byte)classes[i];
        }

        return bytes;
    }

    /// <summary>Raw bytes: every depth as little-endian int16 decimetres, then every class byte; both row-major from the south-west.</summary>
    /// <param name="grid">The grid.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Raw(NavigabilityGrid grid)
    {
        var count = grid.Rows * grid.Columns;
        var bytes = new byte[count * 3];
        var depths = grid.LeastDepths;
        var classes = grid.Classes;
        for (var i = 0; i < count; i++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), Decimetres(depths[i]));
            bytes[(count * 2) + i] = (byte)classes[i];
        }

        return bytes;
    }

    private static short Decimetres(float metres) =>
        float.IsNaN(metres)
            ? NavigabilityGridTool.NoDepthValue
            : (short)Math.Clamp(Math.Round(metres * 10.0), short.MinValue + 1, short.MaxValue);
}

/// <summary>A PNG picture of a <see cref="NavigabilityGrid"/>, north up.</summary>
internal static class NavigabilityGridImage
{
    /// <summary>The longest side of the picture, in pixels; bigger grids are thinned.</summary>
    public const int MaxSide = 1024;

    /// <summary>Renders the grid: land tan, uncharted grey, shoal pale blue, navigable blue (darker deeper), hazards red, water with no depth light grey.</summary>
    /// <param name="grid">The grid.</param>
    /// <returns>The PNG's bytes.</returns>
    public static byte[] Render(NavigabilityGrid grid)
    {
        var scale = Math.Max(1.0, (double)Math.Max(grid.Rows, grid.Columns) / MaxSide);
        var width = Math.Max(1, (int)(grid.Columns / scale));
        var height = Math.Max(1, (int)(grid.Rows / scale));
        var minimum = grid.MinimumDepthMeters;
        var raw = new byte[height * ((width * 3) + 1)];
        for (var y = 0; y < height; y++)
        {
            // North up: the picture's top row is the grid's last row.
            var row = Math.Min(grid.Rows - 1, (int)((height - 1 - y) * scale));
            var line = y * ((width * 3) + 1);
            raw[line] = 0; // filter: none
            for (var x = 0; x < width; x++)
            {
                var col = Math.Min(grid.Columns - 1, (int)(x * scale));
                var (r, g, b) = Colour(grid.ClassAt(row, col), grid.LeastDepthAt(row, col), minimum);
                var p = line + 1 + (x * 3);
                raw[p] = r;
                raw[p + 1] = g;
                raw[p + 2] = b;
            }
        }

        return Png(width, height, raw);
    }

    private static (byte R, byte G, byte B) Colour(NavigabilityCellClass cellClass, double? depth, double minimum) => cellClass switch
    {
        NavigabilityCellClass.Land => (200, 180, 140),
        NavigabilityCellClass.Uncharted => (150, 150, 150),
        NavigabilityCellClass.Shoal => (170, 215, 240),
        NavigabilityCellClass.Hazard => (220, 40, 40),
        NavigabilityCellClass.NoDepth => (225, 225, 225),
        _ => Blue(depth ?? minimum, minimum),
    };

    // Lighter at the minimum depth, darkest 30 m deeper.
    private static (byte, byte, byte) Blue(double depth, double minimum)
    {
        var t = Math.Clamp((depth - minimum) / 30, 0, 1);
        return ((byte)(110 - (90 * t)), (byte)(170 - (110 * t)), (byte)(230 - (90 * t)));
    }

    private static byte[] Png(int width, int height, byte[] raw)
    {
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 2; // colour type: RGB
        Chunk(output, "IHDR", ihdr);

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            Chunk(output, "IDAT", compressed.ToArray());
        }

        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void Chunk(Stream output, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        var crc = Crc32(Crc32(0xFFFFFFFFu, typeBytes), data) ^ 0xFFFFFFFFu;
        Span<byte> crcBytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        output.Write(crcBytes);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private static uint Crc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }
}
