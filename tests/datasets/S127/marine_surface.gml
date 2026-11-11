<?xml version="1.0" encoding="UTF-8"?>
<S127:Dataset xmlns:S127="http://www.iho.int/S127/2.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_S127_Surface_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-127</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Feature: RestrictedArea as a rectangular surface -->
  <S127:member>
    <S127:RestrictedArea gml:id="f1">
      <S127:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="sf1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>51.05 1.20 51.05 1.30 51.10 1.30 51.10 1.20 51.05 1.20</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S127:geometry>
      <S127:categoryOfRestrictedArea>14</S127:categoryOfRestrictedArea>
      <S127:restriction>7</S127:restriction>
    </S127:RestrictedArea>
  </S127:member>

  <!-- Feature: PilotBoardingPlace point inside the restricted area -->
  <S127:member>
    <S127:PilotBoardingPlace gml:id="f2">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt2">
            <gml:pos>51.085 1.250</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S127:geometry>
      <S127:categoryOfPilotBoardingPlace>2</S127:categoryOfPilotBoardingPlace>
    </S127:PilotBoardingPlace>
  </S127:member>

  <!-- Feature: VesselTrafficServiceArea — surface with hole -->
  <S127:member>
    <S127:VesselTrafficServiceArea gml:id="f3">
      <S127:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="sf3">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>51.00 1.00 51.00 1.50 51.20 1.50 51.20 1.00 51.00 1.00</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
                <gml:interior>
                  <gml:LinearRing>
                    <gml:posList>51.06 1.21 51.06 1.29 51.09 1.29 51.09 1.21 51.06 1.21</gml:posList>
                  </gml:LinearRing>
                </gml:interior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S127:geometry>
    </S127:VesselTrafficServiceArea>
  </S127:member>

</S127:Dataset>
