using System.IO;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

public class CatalogueSpecDetectionTests
{
    [Fact]
    public void DetectPortrayalCatalogueSpec_MissingFolder_ReturnsNull()
    {
        Assert.Null(CatalogueSpecDetection.DetectPortrayalCatalogueSpec(
            Path.Combine(Path.GetTempPath(), "no-such-folder-" + System.Guid.NewGuid())));
    }

    [Fact]
    public void DetectPortrayalCatalogueSpec_MissingXml_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), "encdotnet-pc-" + System.Guid.NewGuid());
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(CatalogueSpecDetection.DetectPortrayalCatalogueSpec(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void DetectFeatureCatalogueSpec_NonexistentFile_ReturnsNull()
    {
        Assert.Null(CatalogueSpecDetection.DetectFeatureCatalogueSpec(
            Path.Combine(Path.GetTempPath(), "no-such-fc-" + System.Guid.NewGuid() + ".xml")));
    }

    [Fact]
    public void DetectFeatureCatalogueSpec_GarbageContent_ReturnsNull()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "not xml at all");
            Assert.Null(CatalogueSpecDetection.DetectFeatureCatalogueSpec(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-104")]
    [InlineData("S-411")]
    public void ReadBuiltInFeatureCatalogueVersion_ReturnsValueOrNull(string spec)
    {
        // Bundled FCs may or may not be present for every spec — the helper
        // must never throw and must return null on miss / non-empty on hit.
        var version = CatalogueSpecDetection.ReadBuiltInFeatureCatalogueVersion(spec);
        if (version is not null)
        {
            Assert.NotEmpty(version);
        }
    }

    [Theory]
    [InlineData("S-101")]
    [InlineData("S-411")]
    public void ReadBuiltInPortrayalCatalogueVersion_ReturnsValueOrNull(string spec)
    {
        var version = CatalogueSpecDetection.ReadBuiltInPortrayalCatalogueVersion(spec);
        if (version is not null)
        {
            Assert.NotEmpty(version);
        }
    }

    [Fact]
    public void ReadBuiltInFeatureCatalogueVersion_UnknownSpec_ReturnsNull()
    {
        Assert.Null(CatalogueSpecDetection.ReadBuiltInFeatureCatalogueVersion("S-999"));
    }
}
