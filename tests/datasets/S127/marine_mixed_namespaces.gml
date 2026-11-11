<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<!--
  Synthetic S-127 fixture mirroring the shape of the IIC test dataset
  (`127IIC0GB4XTEST8.GML`) where the dataset root is in one S-127
  application namespace (`http://www.iho.int/S127/gml/1.0`) but the
  feature children are declared in a *different* S-127 application
  namespace (`http://www.iho.int/S127/gml/cs0/1.0`) using a separate
  prefix.

  The reader must accept any non-GML, non-s100gml namespace as the
  application schema rather than insisting feature children share the
  root's namespace.
-->
<S127:Dataset xmlns:S127="http://www.iho.int/S127/gml/1.0"
              xmlns:S-127="http://www.iho.int/S127/gml/cs0/1.0"
              xmlns:S100="http://www.iho.int/s100gml/1.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S127_MixedNs_Test">
  <member>
    <S-127:PilotBoardingPlace gml:id="f1">
      <geometry>
        <S100:pointProperty>
          <S100:Point gml:id="P1" srsName="urn:ogc:def:crs:EPSG::4326">
            <gml:pos>40.500 -74.000</gml:pos>
          </S100:Point>
        </S100:pointProperty>
      </geometry>
    </S-127:PilotBoardingPlace>
  </member>

  <member>
    <S-127:RestrictedAreaNavigational gml:id="f2">
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
    </S-127:RestrictedAreaNavigational>
  </member>
</S127:Dataset>
