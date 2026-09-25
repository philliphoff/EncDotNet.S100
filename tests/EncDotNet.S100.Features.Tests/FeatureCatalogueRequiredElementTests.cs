using System.Text;
using System.Xml;

namespace EncDotNet.S100.Features.Tests;

/// <summary>
/// Tests that <see cref="FeatureCatalogueReader"/> rejects a catalogue missing
/// an element or attribute that the model declares non-nullable with an
/// <see cref="XmlException"/> naming it, instead of leaving <see langword="null"/>
/// in a required property or failing with a <see cref="NullReferenceException"/>.
/// </summary>
public class FeatureCatalogueRequiredElementTests
{
    // A minimal but complete catalogue. Each test removes one required piece
    // by replacing its exact text with an empty string.
    private const string ValidCatalogue = """
        <?xml version="1.0" encoding="utf-8"?>
        <S100FC:S100_FC_FeatureCatalogue
            xmlns:S100FC="http://www.iho.int/S100FC/5.2"
            xmlns:S100Base="http://www.iho.int/S100Base/5.0"
            xmlns:S100CI="http://www.iho.int/S100CI/5.0">
          <S100FC:name>Test</S100FC:name>
          <S100FC:versionNumber>1.0.0</S100FC:versionNumber>
          <S100FC:versionDate>2024-10-16</S100FC:versionDate>
          <S100FC:S100_FC_SimpleAttributes>
            <S100FC:S100_FC_SimpleAttribute>
              <S100FC:name>Category</S100FC:name>
              <S100FC:code>category</S100FC:code>
              <S100FC:valueType>enumeration</S100FC:valueType>
              <S100FC:listedValues>
                <S100FC:listedValue>
                  <S100FC:label>first</S100FC:label>
                  <S100FC:code>1</S100FC:code>
                </S100FC:listedValue>
              </S100FC:listedValues>
            </S100FC:S100_FC_SimpleAttribute>
          </S100FC:S100_FC_SimpleAttributes>
          <S100FC:S100_FC_ComplexAttributes>
            <S100FC:S100_FC_ComplexAttribute>
              <S100FC:name>Group</S100FC:name>
              <S100FC:code>group</S100FC:code>
              <S100FC:subAttributeBinding>
                <S100FC:multiplicity><S100Base:lower>0</S100Base:lower><S100Base:upper>1</S100Base:upper></S100FC:multiplicity>
                <S100FC:attribute ref="category"/>
              </S100FC:subAttributeBinding>
            </S100FC:S100_FC_ComplexAttribute>
          </S100FC:S100_FC_ComplexAttributes>
          <S100FC:S100_FC_Roles>
            <S100FC:S100_FC_Role>
              <S100FC:name>Target</S100FC:name>
              <S100FC:code>theTarget</S100FC:code>
            </S100FC:S100_FC_Role>
          </S100FC:S100_FC_Roles>
          <S100FC:S100_FC_InformationAssociations>
            <S100FC:S100_FC_InformationAssociation>
              <S100FC:name>Info Link</S100FC:name>
              <S100FC:code>InfoLink</S100FC:code>
              <S100FC:role ref="infoTarget"/>
            </S100FC:S100_FC_InformationAssociation>
          </S100FC:S100_FC_InformationAssociations>
          <S100FC:S100_FC_FeatureAssociations>
            <S100FC:S100_FC_FeatureAssociation>
              <S100FC:name>Feature Link</S100FC:name>
              <S100FC:code>FeatureLink</S100FC:code>
              <S100FC:role ref="theTarget"/>
            </S100FC:S100_FC_FeatureAssociation>
          </S100FC:S100_FC_FeatureAssociations>
          <S100FC:S100_FC_InformationTypes>
            <S100FC:S100_FC_InformationType>
              <S100FC:name>Note</S100FC:name>
              <S100FC:code>Note</S100FC:code>
            </S100FC:S100_FC_InformationType>
          </S100FC:S100_FC_InformationTypes>
          <S100FC:S100_FC_FeatureTypes>
            <S100FC:S100_FC_FeatureType>
              <S100FC:name>Beacon</S100FC:name>
              <S100FC:code>Beacon</S100FC:code>
              <S100FC:attributeBinding>
                <S100FC:multiplicity><S100Base:lower>1</S100Base:lower><S100Base:upper>1</S100Base:upper></S100FC:multiplicity>
                <S100FC:attribute ref="group"/>
              </S100FC:attributeBinding>
              <S100FC:featureBinding>
                <S100FC:multiplicity><S100Base:lower>0</S100Base:lower><S100Base:upper infinite="true"/></S100FC:multiplicity>
                <S100FC:association ref="FeatureLink"/>
                <S100FC:role ref="theTarget"/>
                <S100FC:featureType ref="Beacon"/>
              </S100FC:featureBinding>
              <S100FC:informationBinding>
                <S100FC:multiplicity><S100Base:lower>0</S100Base:lower><S100Base:upper>1</S100Base:upper></S100FC:multiplicity>
                <S100FC:association ref="InfoLink"/>
                <S100FC:role ref="theTarget"/>
                <S100FC:informationType ref="Note"/>
              </S100FC:informationBinding>
            </S100FC:S100_FC_FeatureType>
          </S100FC:S100_FC_FeatureTypes>
        </S100FC:S100_FC_FeatureCatalogue>
        """;

    private static FeatureCatalogue Read(string xml) =>
        FeatureCatalogueReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static string Without(string fragment)
    {
        Assert.Equal(1, CountOccurrences(ValidCatalogue, fragment));
        return ValidCatalogue.Replace(fragment, "", StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string fragment) =>
        (text.Length - text.Replace(fragment, "", StringComparison.Ordinal).Length) / fragment.Length;

    [Fact]
    public void ValidCatalogue_Parses()
    {
        var fc = Read(ValidCatalogue);

        var beacon = Assert.Single(fc.FeatureTypes);
        Assert.Equal("Beacon", beacon.Code);
        Assert.Equal("group", Assert.Single(beacon.AttributeBindings).AttributeRef);
        Assert.Equal("FeatureLink", Assert.Single(beacon.FeatureBindings).AssociationRef);
        Assert.Equal("Note", Assert.Single(beacon.InformationBindings).InformationTypeRef);
    }

    [Theory]
    [InlineData("<S100FC:name>Test</S100FC:name>", "S100_FC_FeatureCatalogue", "name")]
    [InlineData("<S100FC:versionNumber>1.0.0</S100FC:versionNumber>", "S100_FC_FeatureCatalogue", "versionNumber")]
    [InlineData("<S100FC:name>Category</S100FC:name>", "S100_FC_SimpleAttribute", "name")]
    [InlineData("<S100FC:code>category</S100FC:code>", "S100_FC_SimpleAttribute", "code")]
    [InlineData("<S100FC:valueType>enumeration</S100FC:valueType>", "S100_FC_SimpleAttribute", "valueType")]
    [InlineData("<S100FC:label>first</S100FC:label>", "listedValue", "label")]
    [InlineData("<S100FC:code>1</S100FC:code>", "listedValue", "code")]
    [InlineData("<S100FC:code>group</S100FC:code>", "S100_FC_ComplexAttribute", "code")]
    [InlineData("<S100FC:code>theTarget</S100FC:code>", "S100_FC_Role", "code")]
    [InlineData("<S100FC:name>Info Link</S100FC:name>", "S100_FC_InformationAssociation", "name")]
    [InlineData("<S100FC:code>FeatureLink</S100FC:code>", "S100_FC_FeatureAssociation", "code")]
    [InlineData("<S100FC:code>Note</S100FC:code>", "S100_FC_InformationType", "code")]
    [InlineData("<S100FC:name>Beacon</S100FC:name>", "S100_FC_FeatureType", "name")]
    [InlineData("<S100FC:code>Beacon</S100FC:code>", "S100_FC_FeatureType", "code")]
    public void MissingRequiredTextElement_ThrowsXmlExceptionNamingIt(
        string fragment, string parent, string missing)
    {
        var exception = Assert.Throws<XmlException>(() => Read(Without(fragment)));

        Assert.Contains($"'{parent}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"missing required element '{missing}'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<S100FC:multiplicity><S100Base:lower>1</S100Base:lower><S100Base:upper>1</S100Base:upper></S100FC:multiplicity>""", "attributeBinding", "multiplicity")]
    [InlineData("""<S100FC:multiplicity><S100Base:lower>0</S100Base:lower><S100Base:upper infinite="true"/></S100FC:multiplicity>""", "featureBinding", "multiplicity")]
    [InlineData("""<S100FC:association ref="FeatureLink"/>""", "featureBinding", "association")]
    [InlineData("""<S100FC:featureType ref="Beacon"/>""", "featureBinding", "featureType")]
    [InlineData("""<S100FC:association ref="InfoLink"/>""", "informationBinding", "association")]
    [InlineData("""<S100FC:informationType ref="Note"/>""", "informationBinding", "informationType")]
    [InlineData("""<S100FC:attribute ref="group"/>""", "attributeBinding", "attribute")]
    [InlineData("""<S100FC:attribute ref="category"/>""", "subAttributeBinding", "attribute")]
    [InlineData("<S100Base:lower>1</S100Base:lower>", "multiplicity", "lower")]
    public void MissingRequiredBindingElement_ThrowsXmlExceptionNamingIt(
        string fragment, string parent, string missing)
    {
        var exception = Assert.Throws<XmlException>(() => Read(Without(fragment)));

        Assert.Contains($"'{parent}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains($"missing required element '{missing}'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingBindingElement_NamesEnclosingType()
    {
        var exception = Assert.Throws<XmlException>(() => Read(Without("""<S100FC:featureType ref="Beacon"/>""")));

        Assert.Contains("in 'Beacon'", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""<S100FC:attribute ref="group"/>""", "attribute")]
    [InlineData("""<S100FC:association ref="FeatureLink"/>""", "association")]
    [InlineData("""<S100FC:informationType ref="Note"/>""", "informationType")]
    [InlineData("""<S100FC:role ref="infoTarget"/>""", "role")]
    public void ReferenceWithoutRef_ThrowsXmlExceptionNamingAttribute(string fragment, string element)
    {
        Assert.Contains(fragment, ValidCatalogue, StringComparison.Ordinal);
        var xml = ValidCatalogue.Replace(fragment, $"<S100FC:{element}/>", StringComparison.Ordinal);

        var exception = Assert.Throws<XmlException>(() => Read(xml));

        Assert.Contains($"'{element}'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("missing required attribute 'ref'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingVersionDate_ReadsEmptyString()
    {
        var fc = Read(Without("<S100FC:versionDate>2024-10-16</S100FC:versionDate>"));

        Assert.Equal("", fc.VersionDate);
    }

    [Fact]
    public void MissingOptionalElements_StillParse()
    {
        // productId, scope, definition, alias, remarks, superType and the
        // binding collections are optional; the minimal header is enough.
        var fc = Read("""
            <?xml version="1.0" encoding="utf-8"?>
            <S100FC:S100_FC_FeatureCatalogue xmlns:S100FC="http://www.iho.int/S100FC/5.2">
              <S100FC:name>Test</S100FC:name>
              <S100FC:versionNumber>1.0.0</S100FC:versionNumber>
              <S100FC:versionDate>2024-10-16</S100FC:versionDate>
              <S100FC:S100_FC_FeatureTypes>
                <S100FC:S100_FC_FeatureType>
                  <S100FC:name>Beacon</S100FC:name>
                  <S100FC:code>Beacon</S100FC:code>
                </S100FC:S100_FC_FeatureType>
              </S100FC:S100_FC_FeatureTypes>
            </S100FC:S100_FC_FeatureCatalogue>
            """);

        var beacon = Assert.Single(fc.FeatureTypes);
        Assert.Null(beacon.Definition);
        Assert.Empty(beacon.AttributeBindings);
    }
}
