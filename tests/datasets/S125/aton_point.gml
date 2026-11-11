<?xml version="1.0" encoding="UTF-8"?>
<S125:Dataset xmlns:S125="http://www.iho.int/S125/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S125_PointTest">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-125</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S125:imember>
    <S125:AtonStatusInformation gml:id="info1">
      <S125:changeTypes>1</S125:changeTypes>
      <S125:fixedDateRange>
        <S125:dateStart>2026-01-01</S125:dateStart>
        <S125:dateEnd>2026-03-31</S125:dateEnd>
      </S125:fixedDateRange>
    </S125:AtonStatusInformation>
  </S125:imember>

  <S125:member>
    <S125:LateralBuoy gml:id="f1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1">
            <gml:pos>36.9500 -76.0133</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLateralMark>1</S125:categoryOfLateralMark>
      <S125:AtoNStatus xlink:href="#info1"/>
    </S125:LateralBuoy>
  </S125:member>

  <S125:member>
    <S125:Landmark gml:id="f2">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt2">
            <gml:pos>37.0167 -76.3300</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:objectName>
        <S125:name>Cape Henry Light</S125:name>
      </S125:objectName>
    </S125:Landmark>
  </S125:member>

</S125:Dataset>
