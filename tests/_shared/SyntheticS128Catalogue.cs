namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Writes a minimal S-128 Catalogue of Nautical Products with two overlapping
/// <c>ElectronicProduct</c> coverages: an approach cell and a harbour cell
/// nested inside it, the arrangement an S-100 exchange set's catalogue
/// describes. The coverages sit over the IHO S-101 test cell
/// (<c>tests/datasets/S101</c>, about 32.13–32.30°S, 62.83–63.00°E) so the two can
/// be loaded together.
/// </summary>
internal static class SyntheticS128Catalogue
{
    /// <summary>A point inside both coverages (longitude, latitude).</summary>
    public static (double Longitude, double Latitude) OverlapPoint => (62.915, -32.215);

    /// <summary>
    /// Writes the catalogue to <paramref name="directory"/> as
    /// <paramref name="fileName"/> and returns its full path.
    /// </summary>
    public static string Write(string directory, string fileName = "128XX00_SYNTHETIC.gml")
    {
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, Gml);
        return path;
    }

    private const string Gml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <S128:Dataset xmlns:S128="http://www.iho.int/S128/2.0"
                      xmlns:gml="http://www.opengis.net/gml/3.2"
                      xmlns:S100="http://www.iho.int/s100gml/5.0"
                      xmlns:xlink="http://www.w3.org/1999/xlink"
                      gml:id="SYNTHETIC">
          <gml:boundedBy>
            <gml:Envelope srsName="EPSG:4326">
              <gml:lowerCorner>-32.35 62.78</gml:lowerCorner>
              <gml:upperCorner>-32.08 63.05</gml:upperCorner>
            </gml:Envelope>
          </gml:boundedBy>
          <S100:DatasetIdentificationInformation>
            <S100:productIdentifier>S-128</S100:productIdentifier>
          </S100:DatasetIdentificationInformation>
          <S128:members>
            <S128:ElectronicProduct gml:id="APPROACH">
              <S128:geometry>
                <S100:surfaceProperty><S100:Surface gml:id="s.approach">
                  <gml:patches><gml:PolygonPatch>
                    <gml:exterior><gml:LinearRing>
                      <gml:posList>-32.35 62.78 -32.08 62.78 -32.08 63.05 -32.35 63.05 -32.35 62.78</gml:posList>
                    </gml:LinearRing></gml:exterior>
                  </gml:PolygonPatch></gml:patches>
                </S100:Surface></S100:surfaceProperty>
              </S128:geometry>
            </S128:ElectronicProduct>
            <S128:ElectronicProduct gml:id="HARBOUR">
              <S128:geometry>
                <S100:surfaceProperty><S100:Surface gml:id="s.harbour">
                  <gml:patches><gml:PolygonPatch>
                    <gml:exterior><gml:LinearRing>
                      <gml:posList>-32.25 62.88 -32.18 62.88 -32.18 62.95 -32.25 62.95 -32.25 62.88</gml:posList>
                    </gml:LinearRing></gml:exterior>
                  </gml:PolygonPatch></gml:patches>
                </S100:Surface></S100:surfaceProperty>
              </S128:geometry>
            </S128:ElectronicProduct>
          </S128:members>
        </S128:Dataset>
        """;
}
