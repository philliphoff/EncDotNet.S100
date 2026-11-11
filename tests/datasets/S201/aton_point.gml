<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-201 Edition 2.0.0 GML fixture.

  Hand-authored from the Feature Catalogue (Annex C2) and the GML
  application schema (Annex B). NOT derived from any third-party sample
  data.

  Demonstrates: point feature (LateralBuoy), an information type
  (AtonStatusInformation), and an information reference binding via
  xlink:href.
-->
<S201:Dataset xmlns:S201="http://www.iho.int/S-201/gml/cs0/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S201_PointTest">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-201</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S201:imember>
    <S201:AtonStatusInformation gml:id="info1">
      <S201:changeTypes>1</S201:changeTypes>
    </S201:AtonStatusInformation>
  </S201:imember>

  <S201:member>
    <S201:LateralBuoy gml:id="f1">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1">
            <gml:pos>36.9500 -76.0133</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:categoryOfLateralMark>1</S201:categoryOfLateralMark>
      <S201:AtoNStatus xlink:href="#info1"/>
    </S201:LateralBuoy>
  </S201:member>

  <S201:member>
    <S201:Landmark gml:id="f2">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt2">
            <gml:pos>37.0167 -76.3300</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:featureName>
        <S201:name>Cape Henry Light</S201:name>
      </S201:featureName>
    </S201:Landmark>
  </S201:member>

</S201:Dataset>
