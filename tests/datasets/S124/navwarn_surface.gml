<?xml version="1.0" encoding="UTF-8"?>
<S124:DataSet xmlns:S124="http://www.iho.int/S124/1.0"
              xmlns:S100="http://www.iho.int/S100/profile/s100gml/1.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_NavWarn_Surface_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-124</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Information type: NavwarnPreamble -->
  <S124:imember>
    <S124:NavwarnPreamble gml:id="info1">
      <S124:messageSeriesIdentifier>
        <S124:warningNumber>217</S124:warningNumber>
        <S124:year>2026</S124:year>
        <S124:productionAgency>UKHO</S124:productionAgency>
      </S124:messageSeriesIdentifier>
      <S124:generalArea>English Channel</S124:generalArea>
      <S124:locality>Dover Strait</S124:locality>
    </S124:NavwarnPreamble>
  </S124:imember>

  <!-- Information type: References -->
  <S124:imember>
    <S124:References gml:id="info2">
      <S124:referenceCategory>1</S124:referenceCategory>
      <S124:messageReference>NAVAREA I 0183/2026</S124:messageReference>
    </S124:References>
  </S124:imember>

  <!-- Feature: NavwarnAreaAffected with surface geometry — restricted area off Dover -->
  <S124:member>
    <S124:NavwarnAreaAffected gml:id="f1">
      <S124:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="s1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>51.0500 1.2000 51.0500 1.4000 51.1200 1.4000 51.1200 1.2000 51.0500 1.2000</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S124:geometry>
      <S124:restriction>2</S124:restriction>
    </S124:NavwarnAreaAffected>
  </S124:member>

  <!-- Feature: NavwarnPart (point) inside the affected area — wreck -->
  <S124:member>
    <S124:NavwarnPart gml:id="f2">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1">
            <gml:pos>51.0850 1.3000</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:restriction>7</S124:restriction>
      <S124:warningInformation>
        <S124:information>Dangerous wreck. Depth 8.2m.</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

  <!-- Feature: NavwarnAreaAffected with surface + interior hole — exclusion zone with safe corridor -->
  <S124:member>
    <S124:NavwarnAreaAffected gml:id="f3">
      <S124:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="s2">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>51.0000 1.0000 51.0000 1.6000 51.1500 1.6000 51.1500 1.0000 51.0000 1.0000</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
                <gml:interior>
                  <gml:LinearRing>
                    <gml:posList>51.0600 1.2500 51.0600 1.3500 51.1100 1.3500 51.1100 1.2500 51.0600 1.2500</gml:posList>
                  </gml:LinearRing>
                </gml:interior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S124:geometry>
      <S124:restriction>5</S124:restriction>
    </S124:NavwarnAreaAffected>
  </S124:member>

</S124:DataSet>
