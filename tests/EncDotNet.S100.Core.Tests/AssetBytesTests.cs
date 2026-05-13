using System.Text;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Core.Tests;

public class AssetBytesTests
{
    [Fact]
    public void AsStream_RoundTripsBytes()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        var bytes = new AssetBytes(payload, "foo/bar.bin");

        using Stream stream = bytes.AsStream();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);

        Assert.Equal(payload, ms.ToArray());
        Assert.Equal("foo/bar.bin", bytes.RelativePath);
    }

    [Fact]
    public void AsStream_ReturnsFreshStreamEachCall()
    {
        byte[] payload = [10, 20, 30];
        var bytes = new AssetBytes(payload, "p.bin");

        using Stream a = bytes.AsStream();
        using Stream b = bytes.AsStream();

        // Reading from `a` should not advance `b`.
        Assert.Equal(10, a.ReadByte());
        Assert.Equal(10, b.ReadByte());
    }

    [Fact]
    public void AsStream_IsSeekable()
    {
        byte[] payload = [1, 2, 3, 4, 5];
        var bytes = new AssetBytes(payload, "p.bin");

        using Stream stream = bytes.AsStream();
        Assert.True(stream.CanSeek);
        Assert.Equal(5, stream.Length);
        stream.Seek(2, SeekOrigin.Begin);
        Assert.Equal(3, stream.ReadByte());
    }

    [Fact]
    public void AsString_DecodesUtf8ByDefault()
    {
        byte[] payload = Encoding.UTF8.GetBytes("héllo");
        var bytes = new AssetBytes(payload, "t.txt");

        Assert.Equal("héllo", bytes.AsString());
    }

    [Fact]
    public void AsString_UsesProvidedEncoding()
    {
        byte[] payload = Encoding.Unicode.GetBytes("héllo");
        var bytes = new AssetBytes(payload, "t.txt");

        Assert.Equal("héllo", bytes.AsString(Encoding.Unicode));
    }
}
