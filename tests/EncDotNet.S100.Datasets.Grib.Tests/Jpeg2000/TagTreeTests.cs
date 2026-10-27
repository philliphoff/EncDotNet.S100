using EncDotNet.S100.Datasets.Grib.Internal.Jpeg2000;

namespace EncDotNet.S100.Datasets.Grib.Tests.Jpeg2000;

/// <summary>The tag tree and the packet header bit reader on their own.</summary>
public class TagTreeTests
{
    [Fact]
    public void Decodes_a_hand_coded_tree()
    {
        // A 6 × 3 tree (four levels: 6 × 3, 3 × 2, 2 × 1, 1 × 1) whose first
        // two leaves are 1 and 3 and whose minimum is 1. Leaf 0: the root is
        // 1, coded 0 1; its next three nodes down are 1 too, coded 1 each.
        // Leaf 1 shares those nodes; its own value 3 is coded 0 0 1 from the
        // parent's 1. So 01111 001.
        var tree = new TagTree(6, 3);
        var reader = new PacketHeaderReader([0b0111_1001], 0);

        Assert.Equal(1, tree.DecodeValue(ref reader, 0));
        Assert.Equal(3, tree.DecodeValue(ref reader, 1));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(5, 3, 2)]
    [InlineData(6, 3, 3)]
    [InlineData(17, 9, 4)]
    public void Decodes_what_the_reference_encoder_wrote(int width, int height, int seed)
    {
        var random = new Random(seed);
        var values = Enumerable.Range(0, width * height).Select(_ => random.Next(6)).ToArray();

        // Inclusion style: thresholds 1, 2, 3 … for each leaf not yet found
        // below them, then the full value of the zero bit-plane style.
        var encoder = new TagTreeTestEncoder(width, height, values);
        var writer = new HeaderTestWriter();
        var expected = new List<bool>();
        for (var threshold = 1; threshold <= 6; threshold++)
        {
            for (var leaf = 0; leaf < values.Length; leaf++)
            {
                if (values[leaf] < threshold - 1)
                    continue;
                encoder.Encode(writer, leaf, threshold);
                expected.Add(values[leaf] < threshold);
            }
        }

        var full = new TagTreeTestEncoder(width, height, values);
        for (var leaf = 0; leaf < values.Length; leaf++)
        {
            for (var threshold = 1; threshold <= values[leaf] + 1; threshold++)
                full.Encode(writer, leaf, threshold);
        }

        var bytes = writer.ToArray();
        var reader = new PacketHeaderReader(bytes, 0);
        var tree = new TagTree(width, height);
        var k = 0;
        for (var threshold = 1; threshold <= 6; threshold++)
        {
            for (var leaf = 0; leaf < values.Length; leaf++)
            {
                if (values[leaf] < threshold - 1)
                    continue;
                Assert.Equal(expected[k++], tree.Decode(ref reader, leaf, threshold));
            }
        }

        var second = new TagTree(width, height);
        for (var leaf = 0; leaf < values.Length; leaf++)
            Assert.Equal(values[leaf], second.DecodeValue(ref reader, leaf));
    }

    [Fact]
    public void Header_reader_skips_stuffed_bits_and_the_byte_after_a_final_0xFF()
    {
        // 0xFF, then 0x55 has 7 bits (its top bit is the stuffed 0): 1010101.
        var reader = new PacketHeaderReader([0xFF, 0x55, 0xFF, 0x00, 0x12], 0);
        Assert.Equal(0xFF, reader.Read(8));
        Assert.Equal(0b1010101, reader.Read(7));
        Assert.Equal(0xFF, reader.Read(8));

        // The header ends after a 0xFF: the stuffed byte that follows is the header's too.
        reader.Align();
        Assert.Equal(4, reader.Position);
    }

    [Fact]
    public void Header_reader_fails_at_the_end_of_the_data()
    {
        var ex = Assert.Throws<GribFormatException>(() =>
        {
            var reader = new PacketHeaderReader([0x80], 0);
            reader.Read(9);
        });
        Assert.Contains("ends inside a packet header", ex.Message);
    }

    /// <summary>A tag tree encoder (T.800 B.10.2), for the tests only.</summary>
    private sealed class TagTreeTestEncoder
    {
        private readonly int[] _value;
        private readonly int[] _low;
        private readonly bool[] _known;
        private readonly int[] _parent;

        public TagTreeTestEncoder(int width, int height, int[] leaves)
        {
            var levels = new List<(int Start, int W, int H)>();
            int w = width, h = height, total = 0;
            while (true)
            {
                levels.Add((total, w, h));
                total += w * h;
                if (w == 1 && h == 1)
                    break;
                w = (w + 1) / 2;
                h = (h + 1) / 2;
            }

            _value = new int[total];
            _low = new int[total];
            _known = new bool[total];
            _parent = new int[total];
            Array.Fill(_value, int.MaxValue);
            leaves.CopyTo(_value, 0);
            for (var l = 0; l < levels.Count; l++)
            {
                var (start, lw, lh) = levels[l];
                for (var y = 0; y < lh; y++)
                {
                    for (var x = 0; x < lw; x++)
                    {
                        var node = start + (y * lw) + x;
                        if (l + 1 < levels.Count)
                        {
                            var (ps, pw, _) = levels[l + 1];
                            var parent = ps + ((y / 2) * pw) + (x / 2);
                            _parent[node] = parent;
                            _value[parent] = Math.Min(_value[parent], _value[node]);
                        }
                        else
                        {
                            _parent[node] = -1;
                        }
                    }
                }
            }
        }

        public void Encode(HeaderTestWriter writer, int leaf, int threshold)
        {
            var path = new Stack<int>();
            for (var node = leaf; node >= 0; node = _parent[node])
                path.Push(node);

            var low = 0;
            while (path.Count > 0)
            {
                var node = path.Pop();
                if (low > _low[node])
                    _low[node] = low;
                else
                    low = _low[node];

                while (low < threshold)
                {
                    if (low >= _value[node])
                    {
                        if (!_known[node])
                        {
                            writer.Bit(1);
                            _known[node] = true;
                        }

                        break;
                    }

                    writer.Bit(0);
                    low++;
                }

                _low[node] = low;
            }
        }
    }

    /// <summary>Writes packet header bits with a 0 stuffed after each 0xFF.</summary>
    private sealed class HeaderTestWriter
    {
        private readonly List<byte> _bytes = [];
        private int _buffer;
        private int _count;
        private int _capacity = 8;

        public void Bit(int bit)
        {
            _buffer = (_buffer << 1) | bit;
            if (++_count == _capacity)
                Flush();
        }

        public byte[] ToArray()
        {
            if (_count > 0)
            {
                _buffer <<= _capacity - _count;
                Flush();
            }

            return [.. _bytes];
        }

        private void Flush()
        {
            _bytes.Add((byte)_buffer);
            _capacity = _buffer == 0xFF ? 7 : 8;
            _buffer = 0;
            _count = 0;
        }
    }
}
