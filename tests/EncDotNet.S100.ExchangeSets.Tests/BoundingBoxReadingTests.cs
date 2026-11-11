using System.Text;

namespace EncDotNet.S100.ExchangeSets.Tests;

/// <summary>
/// How <c>boundingBox</c> elements are read, including the non-conformant
/// encodings real catalogues use (IC-ENC test data): bounds as plain text in
/// the catalogue's own namespace, boxes only inside <c>dataCoverage</c>, and
/// projected coordinates.
/// </summary>
public class BoundingBoxReadingTests
{
    private const string Namespaces =
        "xmlns:S100XC=\"http://www.iho.int/s100/xc/5.2\" "
        + "xmlns:gex=\"http://standards.iso.org/iso/19115/-3/gex/1.0\" "
        + "xmlns:gco=\"http://standards.iso.org/iso/19115/-3/gco/1.0\"";

    private static DatasetDiscoveryMetadata ReadDataset(string datasetBody)
    {
        var xml = $"""
            <S100XC:S100_ExchangeCatalogue {Namespaces}>
              <S100XC:datasetDiscoveryMetadata>
                <S100XC:S100_DatasetDiscoveryMetadata>
                  <S100XC:fileName>file:/S-101/DATASET_FILES/101XX00TEST.000</S100XC:fileName>
                  {datasetBody}
                </S100XC:S100_DatasetDiscoveryMetadata>
              </S100XC:datasetDiscoveryMetadata>
            </S100XC:S100_ExchangeCatalogue>
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        return Assert.Single(ExchangeCatalogueReader.Read(stream, ExchangeCatalogueReadOptions.DiscoveryOnly).DatasetDiscoveryMetadata);
    }

    private static string GexBox(string west, string east, string south, string north) => $"""
        <S100XC:boundingBox>
          <gex:westBoundLongitude><gco:Decimal>{west}</gco:Decimal></gex:westBoundLongitude>
          <gex:eastBoundLongitude><gco:Decimal>{east}</gco:Decimal></gex:eastBoundLongitude>
          <gex:southBoundLatitude><gco:Decimal>{south}</gco:Decimal></gex:southBoundLatitude>
          <gex:northBoundLatitude><gco:Decimal>{north}</gco:Decimal></gex:northBoundLatitude>
        </S100XC:boundingBox>
        """;

    private static void AssertBox(BoundingBox? box, double west, double east, double south, double north)
    {
        Assert.NotNull(box);
        Assert.Equal(west, box.WestBoundLongitude);
        Assert.Equal(east, box.EastBoundLongitude);
        Assert.Equal(south, box.SouthBoundLatitude);
        Assert.Equal(north, box.NorthBoundLatitude);
    }

    [Fact]
    public void Reads_a_conformant_gex_box()
    {
        var dataset = ReadDataset(GexBox("2.2369421", "3.5820416", "50.9991654", "51.8753244"));

        AssertBox(dataset.BoundingBox, 2.2369421, 3.5820416, 50.9991654, 51.8753244);
        AssertBox(dataset.ResolveBoundingBox(), 2.2369421, 3.5820416, 50.9991654, 51.8753244);
    }

    [Fact]
    public void Reads_bounds_written_as_plain_text_in_the_catalogue_namespace()
    {
        // As IC-ENC's Belgian S-101 zip writes them; these used to read as 0, 0, 0, 0.
        var dataset = ReadDataset("""
            <S100XC:boundingBox>
              <S100XC:westBoundLongitude>2.2369421</S100XC:westBoundLongitude>
              <S100XC:eastBoundLongitude>3.5820416</S100XC:eastBoundLongitude>
              <S100XC:southBoundLatitude>50.9991654</S100XC:southBoundLatitude>
              <S100XC:northBoundLatitude>51.8753244</S100XC:northBoundLatitude>
            </S100XC:boundingBox>
            """);

        AssertBox(dataset.BoundingBox, 2.2369421, 3.5820416, 50.9991654, 51.8753244);
    }

    [Fact]
    public void Resolves_a_box_given_only_inside_data_coverage()
    {
        var dataset = ReadDataset($"""
            <S100XC:dataCoverage>{GexBox("-1.7", "-1.2", "50.4", "50.6")}</S100XC:dataCoverage>
            <S100XC:dataCoverage>{GexBox("-1.4", "-0.655", "50.5", "50.83")}</S100XC:dataCoverage>
            """);

        Assert.Null(dataset.BoundingBox);
        AssertBox(dataset.DataCoverages[0].BoundingBox, -1.7, -1.2, 50.4, 50.6);
        AssertBox(dataset.ResolveBoundingBox(), -1.7, -0.655, 50.4, 50.83);
    }

    [Theory]
    [InlineData("385232.0", "389432.0", "3723280.9", "3726808.9")] // projected metres
    [InlineData("2", "3", "", "51")]                                 // missing bound
    [InlineData("2", "3", "abc", "51")]                              // unparseable bound
    [InlineData("0", "0", "0", "0")]                                 // a point at 0°, 0°
    [InlineData("2", "3", "52", "51")]                               // south above north
    public void Rejects_boxes_that_are_not_a_geographic_extent(string west, string east, string south, string north)
    {
        var dataset = ReadDataset(GexBox(west, east, south, north));

        Assert.Null(dataset.BoundingBox);
        Assert.Null(dataset.ResolveBoundingBox());
    }
}
