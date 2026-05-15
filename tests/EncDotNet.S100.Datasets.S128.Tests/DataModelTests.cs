using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S128.DataModel;

namespace EncDotNet.S100.Datasets.S128.Tests;

/// <summary>
/// Tests for the strongly-typed projection
/// <see cref="S128ProductCatalogue"/>.
/// </summary>
public class DataModelTests
{
    private const string TestDataDir = "TestData";
    private const string SampleFile = "S128_TDS_sample.gml";

    private static S128Dataset LoadSample() =>
        S128Dataset.Open(Path.Combine(TestDataDir, SampleFile));

    private static S128ProductCatalogue ProjectSample(out IReadOnlyList<ProjectionDiagnostic> diagnostics) =>
        S128ProductCatalogue.From(LoadSample(), out diagnostics);

    private static S128Dataset OpenGml(string gml)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(gml));
        return S128Dataset.Open(stream);
    }

    [Fact]
    public void From_OfficialSample_ProjectsProductSubclassesAndSupersedesLink()
    {
        var catalogue = ProjectSample(out _);

        Assert.Equal("S-128", catalogue.ProductIdentifier);
        Assert.Equal(5, catalogue.Products.Length);

        var electronic = catalogue.Products.OfType<S128ElectronicProduct>().ToList();
        var physical = catalogue.Products.OfType<S128PhysicalProduct>().ToList();
        var services = catalogue.Products.OfType<S128Service>().ToList();
        Assert.Equal(2, electronic.Count);
        Assert.Equal(2, physical.Count);
        Assert.Single(services);

        // CNP00005 (PhysicalProduct) supersedes ID0002 (ElectronicProduct)
        // via theReference with categoryOfProductMapping=1
        // ("Higher Priority Alternative").
        var cnp5 = catalogue.Products.Single(p => p.Id == "CNP00005");
        var id0002 = catalogue.Products.Single(p => p.Id == "ID0002");

        Assert.Contains(cnp5.Supersedes, t => t.Id == "ID0002");
        Assert.Contains(id0002.SupersededBy, t => t.Id == "CNP00005");
    }

    [Fact]
    public void From_OfficialSample_TypesMetadataFeaturesCorrectly()
    {
        var catalogue = ProjectSample(out _);

        Assert.Single(catalogue.Producers);
        Assert.Equal("CNP00010", catalogue.Producers[0].Id);
        Assert.Equal("KHOA", catalogue.Producers[0].AgencyResponsibleForProduction);

        Assert.Single(catalogue.Distributors);
        Assert.Equal("CNP00007", catalogue.Distributors[0].Id);
        Assert.Equal("KHOA", catalogue.Distributors[0].DistributorName);

        Assert.Single(catalogue.Contacts);
        Assert.Equal("CNP00009", catalogue.Contacts[0].Id);

        Assert.Single(catalogue.SectionHeaders);
        Assert.Equal("CNP00008", catalogue.SectionHeaders[0].Id);
        Assert.Equal("5005", catalogue.SectionHeaders[0].CatalogueSectionNumber);
    }

    [Fact]
    public void From_OfficialSample_ServiceCarriesReleasedStatus()
    {
        var catalogue = ProjectSample(out _);
        var service = catalogue.Products.OfType<S128Service>().Single();
        Assert.Equal("CNP00006", service.Id);
        Assert.Equal(S128ServiceStatus.Released, service.ServiceStatus);
    }

    [Fact]
    public void From_OfficialSample_DistributorHasNoGeometry()
    {
        // Metadata feature surfaces with no geometry — projection must not throw.
        var catalogue = ProjectSample(out _);
        var distributor = catalogue.Distributors.Single();
        Assert.NotNull(distributor.Source);
        Assert.True(distributor.Source.ExteriorRing.IsDefaultOrEmpty);
    }

    [Fact]
    public void From_TwoLinkSupersedes_ResolvesBothDirections()
    {
        // B supersedes A.
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/5.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:ElectronicProduct gml:id="A">
                  <S128:productNumber>A</S128:productNumber>
                </S128:ElectronicProduct>
                <S128:ElectronicProduct gml:id="B">
                  <S128:productNumber>B</S128:productNumber>
                  <S128:theReference xlink:href="#A">
                    <ProductMapping>
                      <categoryOfProductMapping code="1">Higher Priority Alternative</categoryOfProductMapping>
                    </ProductMapping>
                  </S128:theReference>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;
        var catalogue = S128ProductCatalogue.From(OpenGml(gml), out var diagnostics);

        var a = catalogue.Products.Single(p => p.Id == "A");
        var b = catalogue.Products.Single(p => p.Id == "B");

        Assert.Contains(b.Supersedes, t => t.Id == "A");
        Assert.Contains(a.SupersededBy, t => t.Id == "B");
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void From_ThreeLinkChain_ResolvesEachStepBothDirections()
    {
        // C supersedes B; B supersedes A. No transitive flattening.
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/5.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:ElectronicProduct gml:id="A">
                  <S128:productNumber>A</S128:productNumber>
                </S128:ElectronicProduct>
                <S128:ElectronicProduct gml:id="B">
                  <S128:productNumber>B</S128:productNumber>
                  <S128:theReference xlink:href="#A">
                    <ProductMapping>
                      <categoryOfProductMapping code="1">Higher Priority Alternative</categoryOfProductMapping>
                    </ProductMapping>
                  </S128:theReference>
                </S128:ElectronicProduct>
                <S128:ElectronicProduct gml:id="C">
                  <S128:productNumber>C</S128:productNumber>
                  <S128:theReference xlink:href="#B">
                    <ProductMapping>
                      <categoryOfProductMapping code="1">Higher Priority Alternative</categoryOfProductMapping>
                    </ProductMapping>
                  </S128:theReference>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;
        var catalogue = S128ProductCatalogue.From(OpenGml(gml), out _);

        var a = catalogue.Products.Single(p => p.Id == "A");
        var b = catalogue.Products.Single(p => p.Id == "B");
        var c = catalogue.Products.Single(p => p.Id == "C");

        // No transitive flattening.
        Assert.Single(b.Supersedes);
        Assert.Equal("A", b.Supersedes[0].Id);
        Assert.Single(c.Supersedes);
        Assert.Equal("B", c.Supersedes[0].Id);

        Assert.Single(a.SupersededBy);
        Assert.Equal("B", a.SupersededBy[0].Id);
        Assert.Single(b.SupersededBy);
        Assert.Equal("C", b.SupersededBy[0].Id);
        Assert.Empty(c.SupersededBy);
    }

    [Fact]
    public void From_UnresolvedTheReference_EmitsDiagnostic_DoesNotThrow()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/5.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:ElectronicProduct gml:id="B">
                  <S128:productNumber>B</S128:productNumber>
                  <S128:theReference xlink:href="#NONEXISTENT">
                    <ProductMapping>
                      <categoryOfProductMapping code="1">Higher Priority Alternative</categoryOfProductMapping>
                    </ProductMapping>
                  </S128:theReference>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;
        var catalogue = S128ProductCatalogue.From(OpenGml(gml), out var diagnostics);

        var b = catalogue.Products.Single(p => p.Id == "B");
        Assert.Empty(b.Supersedes);
        Assert.Empty(b.SupersededBy);
        Assert.Contains(diagnostics, d => d.Code == "xlink.unresolved");
    }

    [Fact]
    public void From_NonSupersedesMapping_LandsInRelatedProducts()
    {
        // A theReference whose category text does NOT classify as "Higher
        // Priority Alternative" lands in RelatedProducts, not Supersedes.
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/5.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:ElectronicProduct gml:id="A">
                  <S128:productNumber>A</S128:productNumber>
                </S128:ElectronicProduct>
                <S128:ElectronicProduct gml:id="B">
                  <S128:productNumber>B</S128:productNumber>
                  <S128:theReference xlink:href="#A">
                    <ProductMapping>
                      <categoryOfProductMapping code="99">Some Future Mapping</categoryOfProductMapping>
                    </ProductMapping>
                  </S128:theReference>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;
        var catalogue = S128ProductCatalogue.From(OpenGml(gml), out _);

        var a = catalogue.Products.Single(p => p.Id == "A");
        var b = catalogue.Products.Single(p => p.Id == "B");

        Assert.Empty(b.Supersedes);
        Assert.Empty(a.SupersededBy);
        Assert.Single(b.RelatedProducts);
        Assert.Equal("A", b.RelatedProducts[0].Target.Id);
        Assert.Equal(S128ProductMappingCategory.Other, b.RelatedProducts[0].Category);
        Assert.Equal("Some Future Mapping", b.RelatedProducts[0].RawCategoryText);
    }

    [Fact]
    public void From_UnknownAttribute_RoundTripsThroughExtraAttributes()
    {
        const string gml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                          xmlns:gml="http://www.opengis.net/gml/3.2"
                          xmlns:S100="http://www.iho.int/s100gml/5.0"
                          xmlns:xlink="http://www.w3.org/1999/xlink"
                          gml:id="TEST">
              <S100:DatasetIdentificationInformation>
                <S100:productIdentifier>S-128</S100:productIdentifier>
              </S100:DatasetIdentificationInformation>
              <S128:members>
                <S128:ElectronicProduct gml:id="X">
                  <S128:productNumber>X</S128:productNumber>
                  <S128:editionNumber>3</S128:editionNumber>
                  <S128:vendorSpecificFlag>experimental-value</S128:vendorSpecificFlag>
                </S128:ElectronicProduct>
              </S128:members>
            </S128:Dataset>
            """;
        var catalogue = S128ProductCatalogue.From(OpenGml(gml), out _);
        var x = catalogue.Products.Single();
        Assert.Equal(3, x.EditionNumber);
        Assert.True(x.ExtraAttributes.ContainsKey("vendorSpecificFlag"));
        Assert.Equal("experimental-value", x.ExtraAttributes["vendorSpecificFlag"]);
        // Known columns must NOT be duplicated into ExtraAttributes.
        Assert.False(x.ExtraAttributes.ContainsKey("productNumber"));
        Assert.False(x.ExtraAttributes.ContainsKey("editionNumber"));
    }

    [Fact]
    public void From_EmptyDataset_Throws()
    {
        var empty = new S128Dataset
        {
            ProductIdentifier = "S-128",
            DatasetIdentifier = "EMPTY",
            Features = ImmutableArray<S128Feature>.Empty,
            InformationTypes = ImmutableArray<S128InformationType>.Empty,
        };
        Assert.Throws<InvalidOperationException>(() => S128ProductCatalogue.From(empty, out _));
    }
}
