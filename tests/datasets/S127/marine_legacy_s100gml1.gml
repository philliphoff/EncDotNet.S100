<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-127 fixture mirroring the shape of the published 2019
  Edition 1.0.1 sample (`127JS00EX_A0001.GML`):
    * Application schema namespace `http://www.iho.int/S127/gml/cs0/1.0`
    * S-100 GML 1.0 namespace `http://www.iho.int/s100gml/1.0` (no /profile/)
    * Unprefixed <geometry> wrapper (no S127 prefix on the container)
    * Inline <S100:Point> / <S100:surfaceProperty> primitives
  Used to lock in tolerance for the legacy 1.0.1 namespace shape.
-->
<S127:Dataset xmlns:S127="http://www.iho.int/S127/gml/cs0/1.0"
              xmlns:S100="http://www.iho.int/s100gml/1.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S127_Legacy_Test">
  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-127</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <member>
    <S127:PilotBoardingPlace gml:id="f1">
      <geometry>
        <S100:pointProperty>
          <S100:Point gml:id="P1" srsName="urn:ogc:def:crs:EPSG::4326">
            <gml:pos>40.500 -74.000</gml:pos>
          </S100:Point>
        </S100:pointProperty>
      </geometry>
    </S127:PilotBoardingPlace>
  </member>

  <member>
    <S127:RestrictedAreaNavigational gml:id="f2">
      <geometry>
        <S100:surfaceProperty>
          <S100:Polygon gml:id="S1" srsName="urn:ogc:def:crs:EPSG::4326">
            <gml:exterior>
              <gml:LinearRing>
                <gml:posList>40.50 -74.10 40.55 -74.10 40.55 -74.05 40.50 -74.05 40.50 -74.10</gml:posList>
              </gml:LinearRing>
            </gml:exterior>
          </S100:Polygon>
        </S100:surfaceProperty>
      </geometry>
    </S127:RestrictedAreaNavigational>
  </member>

  <member>
    <S127:RouteingMeasure gml:id="f3">
      <geometry>
        <S100:curveProperty>
          <S100:Curve gml:id="C1" srsName="urn:ogc:def:crs:EPSG::4326">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>40.60 -74.20 40.62 -74.18 40.65 -74.15</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </S100:Curve>
        </S100:curveProperty>
      </geometry>
    </S127:RouteingMeasure>
  </member>
</S127:Dataset>
