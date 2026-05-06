using System.IO;
using EncDotNet.S100.Features;

namespace EncDotNet.S100.Features.Tests;

public class FeatureCatalogueDecoderTests
{
    private static FeatureCatalogueDecoder LoadS101Decoder()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "features", "S101FeatureCatalogue.xml");
        var fc = FeatureCatalogueReader.Read(Path.GetFullPath(path));
        return new FeatureCatalogueDecoder(fc);
    }

    [Fact]
    public void ResolveAttributeName_KnownSimpleAttribute_ReturnsName()
    {
        var decoder = LoadS101Decoder();
        var first = decoder.Catalogue.SimpleAttributes[0];
        var name = decoder.ResolveAttributeName(first.Code);
        Assert.Equal(first.Name, name);
    }

    [Fact]
    public void ResolveAttributeName_UnknownCode_ReturnsNull()
    {
        var decoder = LoadS101Decoder();
        Assert.Null(decoder.ResolveAttributeName("ZZZZNOPE"));
    }

    [Fact]
    public void ResolveAttributeName_EmptyOrNullCode_ReturnsNull()
    {
        var decoder = LoadS101Decoder();
        Assert.Null(decoder.ResolveAttributeName(""));
        Assert.Null(decoder.ResolveAttributeName(null!));
    }

    [Fact]
    public void ResolveListedValue_OnEnumeratedAttribute_ReturnsLabel()
    {
        var decoder = LoadS101Decoder();
        var fc = decoder.Catalogue;

        var enumerated = fc.SimpleAttributes.FirstOrDefault(sa => sa.ListedValues.Count > 0);
        Assert.NotNull(enumerated);
        var lv = enumerated!.ListedValues[0];

        var label = decoder.ResolveListedValue(enumerated.Code, lv.Code);
        Assert.Equal(lv.Label, label);
    }

    [Fact]
    public void ResolveListedValue_NonEnumeratedAttribute_ReturnsNull()
    {
        var decoder = LoadS101Decoder();
        var fc = decoder.Catalogue;

        var nonEnumerated = fc.SimpleAttributes.FirstOrDefault(sa => sa.ListedValues.Count == 0);
        Assert.NotNull(nonEnumerated);
        Assert.Null(decoder.ResolveListedValue(nonEnumerated!.Code, "any-value"));
    }

    [Fact]
    public void ResolveListedValue_UnknownEnumValue_ReturnsNull()
    {
        var decoder = LoadS101Decoder();
        var fc = decoder.Catalogue;

        var enumerated = fc.SimpleAttributes.FirstOrDefault(sa => sa.ListedValues.Count > 0);
        Assert.NotNull(enumerated);
        Assert.Null(decoder.ResolveListedValue(enumerated!.Code, "9999999"));
    }

    [Fact]
    public void ResolveListedValue_NullOrEmpty_ReturnsNull()
    {
        var decoder = LoadS101Decoder();
        Assert.Null(decoder.ResolveListedValue("OBJNAM", null));
        Assert.Null(decoder.ResolveListedValue("OBJNAM", ""));
        Assert.Null(decoder.ResolveListedValue("", "1"));
    }

    [Fact]
    public void ResolveFeatureTypeName_KnownCode_ReturnsName()
    {
        var decoder = LoadS101Decoder();
        var fc = decoder.Catalogue;

        var anyFeature = fc.FeatureTypes.FirstOrDefault();
        Assert.NotNull(anyFeature);
        Assert.Equal(anyFeature!.Name, decoder.ResolveFeatureTypeName(anyFeature.Code));
    }

    [Fact]
    public void ResolveFeatureTypeName_UnknownCode_ReturnsNull()
    {
        var decoder = LoadS101Decoder();
        Assert.Null(decoder.ResolveFeatureTypeName("NoSuchType"));
    }

    [Fact]
    public void IsEnumeratedAttribute_KnownEnumeratedAttribute_ReturnsTrue()
    {
        var decoder = LoadS101Decoder();
        var fc = decoder.Catalogue;

        var enumerated = fc.SimpleAttributes.FirstOrDefault(sa => sa.ListedValues.Count > 0);
        Assert.NotNull(enumerated);
        Assert.True(decoder.IsEnumeratedAttribute(enumerated!.Code));
    }

    [Fact]
    public void IsEnumeratedAttribute_UnknownCode_ReturnsFalse()
    {
        var decoder = LoadS101Decoder();
        Assert.False(decoder.IsEnumeratedAttribute("ZZZZNOPE"));
    }

    [Fact]
    public void Lookups_AreCaseInsensitive()
    {
        var decoder = LoadS101Decoder();
        var fc = decoder.Catalogue;

        var enumerated = fc.SimpleAttributes.FirstOrDefault(sa => sa.ListedValues.Count > 0);
        Assert.NotNull(enumerated);
        var lv = enumerated!.ListedValues[0];

        Assert.Equal(
            decoder.ResolveAttributeName(enumerated.Code),
            decoder.ResolveAttributeName(enumerated.Code.ToUpperInvariant()));
        Assert.Equal(
            decoder.ResolveAttributeName(enumerated.Code),
            decoder.ResolveAttributeName(enumerated.Code.ToLowerInvariant()));
        Assert.Equal(
            decoder.ResolveListedValue(enumerated.Code, lv.Code),
            decoder.ResolveListedValue(enumerated.Code.ToLowerInvariant(), lv.Code));
    }
}
