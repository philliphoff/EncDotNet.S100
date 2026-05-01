<?xml version="1.0" encoding="UTF-8"?>
<S127:Dataset xmlns:S127="http://www.iho.int/S127/2.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_S127_Mixed_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-127</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Point feature -->
  <S127:member>
    <S127:PilotBoardingPlace gml:id="f1">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1"><gml:pos>40.7000 -74.0500</gml:pos></gml:Point>
        </S100:pointProperty>
      </S127:geometry>
      <S127:categoryOfPilotBoardingPlace>1</S127:categoryOfPilotBoardingPlace>
    </S127:PilotBoardingPlace>
  </S127:member>

  <!-- Curve feature -->
  <S127:member>
    <S127:RouteingMeasure gml:id="f2">
      <S127:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="cv2"><gml:segments><gml:LineStringSegment>
            <gml:posList>40.60 -74.10 40.65 -74.05 40.70 -74.00</gml:posList>
          </gml:LineStringSegment></gml:segments></gml:Curve>
        </S100:curveProperty>
      </S127:geometry>
    </S127:RouteingMeasure>
  </S127:member>

  <!-- Surface feature -->
  <S127:member>
    <S127:RestrictedArea gml:id="f3">
      <S127:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="sf3"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:LinearRing>
              <gml:posList>40.50 -74.20 40.50 -74.10 40.55 -74.10 40.55 -74.20 40.50 -74.20</gml:posList>
            </gml:LinearRing></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
        </S100:surfaceProperty>
      </S127:geometry>
      <S127:categoryOfRestrictedArea>14</S127:categoryOfRestrictedArea>
    </S127:RestrictedArea>
  </S127:member>

  <!-- Second point feature -->
  <S127:member>
    <S127:SignalStationTraffic gml:id="f4">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt4"><gml:pos>40.71 -74.06</gml:pos></gml:Point>
        </S100:pointProperty>
      </S127:geometry>
    </S127:SignalStationTraffic>
  </S127:member>

  <!-- Feature without geometry (e.g. container) -->
  <S127:member>
    <S127:Authority gml:id="f5">
      <S127:authorityName>Coast Guard</S127:authorityName>
    </S127:Authority>
  </S127:member>

</S127:Dataset>
