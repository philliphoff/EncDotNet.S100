<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-201 Edition 2.0.0 GML fixture: AIS AtoN classification.

  Demonstrates Virtual vs Physical AIS aids to navigation, each with
  an MMSI code; the Physical variant binds to a host structure via
  theParentFeature.
-->
<S201:Dataset xmlns:S201="http://www.iho.int/S-201/gml/cs0/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S201_Ais">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-201</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S201:member>
    <S201:LateralBuoy gml:id="hostBuoy">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-host"><gml:pos>40.0 -73.0</gml:pos></gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:AtoNNumber>USCG-12345</S201:AtoNNumber>
    </S201:LateralBuoy>
  </S201:member>

  <S201:member>
    <S201:VirtualAISAidToNavigation gml:id="virtualAis">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-virtual"><gml:pos>40.1 -73.1</gml:pos></gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:mMSICode>993672111</S201:mMSICode>
      <S201:status>1</S201:status>
    </S201:VirtualAISAidToNavigation>
  </S201:member>

  <S201:member>
    <S201:PhysicalAISAidToNavigation gml:id="physicalAis">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-physical"><gml:pos>40.0 -73.0</gml:pos></gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:mMSICode>993672222</S201:mMSICode>
      <S201:status>1</S201:status>
      <S201:theParentFeature xlink:href="#hostBuoy"/>
    </S201:PhysicalAISAidToNavigation>
  </S201:member>

</S201:Dataset>
