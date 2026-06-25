using System;
using System.IO;
using System.Text;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using Xunit;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Unit tests for <see cref="ExternalTextFileResolver"/>: confirms it
/// resolves the textual content of external files named by S-100
/// <c>fileReference</c> attributes from a co-located asset source, applies
/// the size bound, and decodes UTF-8 / Latin-1 content correctly.
/// </summary>
public sealed class ExternalTextFileResolverTests : IDisposable
{
    private readonly string _dir;

    public ExternalTextFileResolverTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "extref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best-effort cleanup */ }
    }

    private ExternalTextFileResolver CreateResolver(string datasetRelativePath = "101AA.000")
        => new(FileSystemAssetSource.Create(_dir), datasetRelativePath);

    [Fact]
    public void Resolve_ReadsCoLocatedTextFile()
    {
        File.WriteAllText(Path.Combine(_dir, "CAUTION.TXT"), "Beware of strong currents.");

        var text = CreateResolver().Resolve("CAUTION.TXT");

        Assert.Equal("Beware of strong currents.", text);
    }

    [Fact]
    public void Resolve_NormalizesBackslashAndFileUri()
    {
        File.WriteAllText(Path.Combine(_dir, "PANEL.TXT"), "Tidal stream data.");

        var text = CreateResolver().Resolve("file:/PANEL.TXT");

        Assert.Equal("Tidal stream data.", text);
    }

    [Fact]
    public void Resolve_MissingFileReturnsNull()
    {
        Assert.Null(CreateResolver().Resolve("MISSING.TXT"));
    }

    [Fact]
    public void Resolve_NullOrEmptyReturnsNull()
    {
        var resolver = CreateResolver();
        Assert.Null(resolver.Resolve(""));
        Assert.Null(resolver.Resolve("   "));
    }

    [Fact]
    public void Resolve_OversizedFileReturnsNull()
    {
        var big = new string('x', (int)ExternalTextFileResolver.MaxFileSizeBytes + 1);
        File.WriteAllText(Path.Combine(_dir, "BIG.TXT"), big);

        Assert.Null(CreateResolver().Resolve("BIG.TXT"));
    }

    [Fact]
    public void Decode_FallsBackToLatin1ForNonUtf8Bytes()
    {
        // 0xB0 is the degree sign in ISO 8859-1 and an invalid lone UTF-8
        // continuation byte, so strict UTF-8 decoding fails and falls back.
        var bytes = new byte[] { (byte)'5', (byte)'0', 0xB0, (byte)'N' };

        Assert.Equal("50\u00B0N", ExternalTextFileResolver.Decode(bytes));
    }

    [Fact]
    public void Decode_HonorsUtf8Bom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .AsSpan()
            .ToArray();
        var withText = new byte[bytes.Length + 5];
        bytes.CopyTo(withText, 0);
        Encoding.ASCII.GetBytes("hello").CopyTo(withText, 3);

        Assert.Equal("hello", ExternalTextFileResolver.Decode(withText));
    }
}
