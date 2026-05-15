<?xml version="1.0" encoding="UTF-8"?>
<S125:Dataset xmlns:S125="http://www.iho.int/S125/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S125_Relationships">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-125</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Status information with discrepancy (non-operational) -->
  <S125:imember>
    <S125:AtonStatusInformation gml:id="info_disc">
      <S125:changeTypes>2</S125:changeTypes>
      <S125:changeDetails>Light reported unreliable</S125:changeDetails>
      <S125:fixedDateRange>
        <S125:dateStart>2026-02-01T00:00:00Z</S125:dateStart>
        <S125:dateEnd>2026-02-28T23:59:59Z</S125:dateEnd>
      </S125:fixedDateRange>
    </S125:AtonStatusInformation>
  </S125:imember>

  <!-- Host beacon -->
  <S125:member>
    <S125:CardinalBeacon gml:id="beacon1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt_b">
            <gml:pos>37.0000 -76.0000</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfCardinalMark>1</S125:categoryOfCardinalMark>
      <S125:customAttribute>extension-value</S125:customAttribute>
    </S125:CardinalBeacon>
  </S125:member>

  <!-- Light mounted on the beacon, with discrepancy status -->
  <S125:member>
    <S125:LightAllAround gml:id="light1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt_l">
            <gml:pos>37.0000 -76.0000</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:colour>3</S125:colour>
      <S125:AtoNStatus xlink:href="#info_disc"/>
      <S125:parent xlink:href="#beacon1"/>
    </S125:LightAllAround>
  </S125:member>

  <!-- Virtual AIS AtoN -->
  <S125:member>
    <S125:VirtualAISAidToNavigation gml:id="vais1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt_v">
            <gml:pos>37.0500 -76.1000</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:virtualAISAidToNavigationType>1</S125:virtualAISAidToNavigationType>
    </S125:VirtualAISAidToNavigation>
  </S125:member>

  <!-- Physical AIS AtoN with bogus integer to exercise parse failure path -->
  <S125:member>
    <S125:PhysicalAISAidToNavigation gml:id="pais1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt_p">
            <gml:pos>37.0600 -76.1100</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S125:geometry>
    </S125:PhysicalAISAidToNavigation>
  </S125:member>

  <!-- Aggregation tying the beacon, light, and virtual AIS together (bad CategoryCode) -->
  <S125:member>
    <S125:AtonAggregation gml:id="agg1">
      <S125:categoryOfAggregation>not-a-number</S125:categoryOfAggregation>
      <S125:peerAtonAggregation xlink:href="#beacon1"/>
      <S125:peerAtonAggregation xlink:href="#light1"/>
      <S125:peerAtonAggregation xlink:href="#vais1"/>
    </S125:AtonAggregation>
  </S125:member>

</S125:Dataset>
