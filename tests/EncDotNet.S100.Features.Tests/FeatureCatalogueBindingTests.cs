using System.Text;

namespace EncDotNet.S100.Features.Tests;

public class FeatureCatalogueBindingTests
{
    private const string Catalogue = """
        <?xml version="1.0" encoding="utf-8"?>
        <S100FC:S100_FC_FeatureCatalogue xmlns:S100FC="http://www.iho.int/S100FC/5.2" xmlns:S100Base="http://www.iho.int/S100Base/5.0" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
          <S100FC:name>Test</S100FC:name>
          <S100FC:versionNumber>1.0.0</S100FC:versionNumber>
          <S100FC:versionDate>2026-01-01</S100FC:versionDate>
          <S100FC:S100_FC_FeatureTypes>
            <S100FC:S100_FC_FeatureType isAbstract="false">
              <S100FC:name>Lock basin</S100FC:name>
              <S100FC:code>LockBasin</S100FC:code>
              <S100FC:informationBinding roleType="association">
                <S100FC:multiplicity>
                  <S100Base:lower>0</S100Base:lower>
                  <S100Base:upper xsi:nil="false" infinite="false">1</S100Base:upper>
                </S100FC:multiplicity>
                <S100FC:association ref="AdditionalInformation"/>
                <S100FC:role ref="theInformation"/>
                <S100FC:informationType ref="NauticalInformation"/>
                <S100FC:informationType ref="TimeScheduleInGeneral"/>
              </S100FC:informationBinding>
              <S100FC:featureUseType>geographic</S100FC:featureUseType>
              <S100FC:featureBinding roleType="association">
                <S100FC:multiplicity>
                  <S100Base:lower>0</S100Base:lower>
                  <S100Base:upper xsi:nil="true" infinite="true"/>
                </S100FC:multiplicity>
                <S100FC:association ref="LockAggregation"/>
                <S100FC:role ref="theComponent"/>
                <S100FC:featureType ref="Gate"/>
                <S100FC:featureType ref="LockBasinPart"/>
              </S100FC:featureBinding>
            </S100FC:S100_FC_FeatureType>
          </S100FC:S100_FC_FeatureTypes>
        </S100FC:S100_FC_FeatureCatalogue>
        """;

    private static FeatureType ReadLockBasin()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Catalogue));
        return Assert.Single(FeatureCatalogueReader.Read(stream).FeatureTypes);
    }

    [Fact]
    public void Read_InformationBindingListingSeveralTypes_KeepsEveryType()
    {
        var binding = Assert.Single(ReadLockBasin().InformationBindings);

        Assert.Equal("NauticalInformation", binding.InformationTypeRef);
        Assert.Equal(["NauticalInformation", "TimeScheduleInGeneral"], binding.InformationTypeRefs);
        Assert.Equal(1, binding.Multiplicity.Upper);
    }

    [Fact]
    public void Read_FeatureBindingListingSeveralTypes_KeepsEveryType()
    {
        var binding = Assert.Single(ReadLockBasin().FeatureBindings);

        Assert.Equal("Gate", binding.FeatureTypeRef);
        Assert.Equal(["Gate", "LockBasinPart"], binding.FeatureTypeRefs);
    }
}
