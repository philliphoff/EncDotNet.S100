<?xml version="1.0" encoding="UTF-8"?>
<S127:Dataset xmlns:S127="http://www.iho.int/S127/2.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_S127_Curve_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-127</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Feature: RouteingMeasure as a directional traffic lane (curve, 4 vertices) -->
  <S127:member>
    <S127:RouteingMeasure gml:id="f1">
      <S127:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="cv1">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>29.31 -94.78 29.40 -94.70 29.50 -94.60 29.60 -94.50</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S127:geometry>
      <S127:categoryOfRouteingMeasure>2</S127:categoryOfRouteingMeasure>
      <S127:trafficFlow>1</S127:trafficFlow>
    </S127:RouteingMeasure>
  </S127:member>

  <!-- Feature: RouteingMeasure as a 3-vertex outbound lane -->
  <S127:member>
    <S127:RouteingMeasure gml:id="f2">
      <S127:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="cv2">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>29.30 -94.50 29.40 -94.40 29.50 -94.30</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S127:geometry>
      <S127:categoryOfRouteingMeasure>3</S127:categoryOfRouteingMeasure>
    </S127:RouteingMeasure>
  </S127:member>

</S127:Dataset>
