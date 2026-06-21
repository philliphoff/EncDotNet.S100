using System.IO;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Features.Tests;

/// <summary>
/// Tests that <see cref="FeatureCatalogueReader"/> reads the
/// <c>&lt;S100FC:uom&gt;</c> unit of measure of simple attributes, so
/// downstream consumers can expose units (e.g. metres on depth-valued
/// S-101 attributes) without inference (issue #334).
/// </summary>
public class SimpleAttributeUomTests
{
    private static FeatureCatalogue LoadS101Catalogue()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "features", "S101FeatureCatalogue.xml");
        return FeatureCatalogueReader.Read(Path.GetFullPath(path));
    }

    [Fact]
    public void DepthValuedAttribute_HasMetreUom()
    {
        var fc = LoadS101Catalogue();
        var depth = fc.SimpleAttributes.First(sa =>
            string.Equals(sa.Code, "depthRangeMinimumValue", StringComparison.Ordinal));

        Assert.NotNull(depth.Uom);
        Assert.Equal("metre", depth.Uom!.Name);
        Assert.Equal("m", depth.Uom.Symbol);
    }

    [Fact]
    public void EnumerationAttribute_HasNoUom()
    {
        var fc = LoadS101Catalogue();
        var enumerated = fc.SimpleAttributes.First(sa =>
            string.Equals(sa.ValueType, "enumeration", StringComparison.Ordinal));

        Assert.Null(enumerated.Uom);
    }
}
