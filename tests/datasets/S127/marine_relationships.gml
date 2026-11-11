<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-127 dataset exercising feature-to-feature xlink references
  (theAuthority) and a geometry-less Authority container, plus an
  unresolved xlink target to drive the xlink.unresolved diagnostic.
  Used by the strongly-typed data-model tests.
-->
<S127:Dataset xmlns:S127="http://www.iho.int/S127/2.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S127_Relationships_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-127</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Geometry-less Authority container. -->
  <S127:member>
    <S127:Authority gml:id="auth1">
      <S127:authorityName>Port Authority of New York</S127:authorityName>
    </S127:Authority>
  </S127:member>

  <!-- Pilot boarding place pointing at auth1. -->
  <S127:member>
    <S127:PilotBoardingPlace gml:id="pbp1">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-pbp1"><gml:pos>40.7000 -74.0500</gml:pos></gml:Point>
        </S100:pointProperty>
      </S127:geometry>
      <S127:categoryOfPilotBoardingPlace>1</S127:categoryOfPilotBoardingPlace>
      <S127:theAuthority xlink:href="#auth1"/>
    </S127:PilotBoardingPlace>
  </S127:member>

  <!-- VTS area pointing at auth1. -->
  <S127:member>
    <S127:VesselTrafficServiceArea gml:id="vts1">
      <S127:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="sf-vts1"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:LinearRing>
              <gml:posList>40.50 -74.20 40.50 -74.10 40.55 -74.10 40.55 -74.20 40.50 -74.20</gml:posList>
            </gml:LinearRing></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
        </S100:surfaceProperty>
      </S127:geometry>
      <S127:theAuthority xlink:href="#auth1"/>
    </S127:VesselTrafficServiceArea>
  </S127:member>

  <!-- Restricted area carrying a category code and an unresolved xlink. -->
  <S127:member>
    <S127:RestrictedArea gml:id="ra1">
      <S127:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="sf-ra1"><gml:patches><gml:PolygonPatch>
            <gml:exterior><gml:LinearRing>
              <gml:posList>40.60 -74.30 40.60 -74.20 40.65 -74.20 40.65 -74.30 40.60 -74.30</gml:posList>
            </gml:LinearRing></gml:exterior>
          </gml:PolygonPatch></gml:patches></gml:Surface>
        </S100:surfaceProperty>
      </S127:geometry>
      <S127:categoryOfRestrictedArea>14</S127:categoryOfRestrictedArea>
      <S127:theAuthority xlink:href="#missing"/>
    </S127:RestrictedArea>
  </S127:member>

  <!-- Signal-station (traffic). No theAuthority binding. -->
  <S127:member>
    <S127:SignalStationTraffic gml:id="ss1">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-ss1"><gml:pos>40.71 -74.06</gml:pos></gml:Point>
        </S100:pointProperty>
      </S127:geometry>
    </S127:SignalStationTraffic>
  </S127:member>

  <!-- Catch-all feature: PilotService is not broken out as a typed shape. -->
  <S127:member>
    <S127:PilotService gml:id="ps1">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-ps1"><gml:pos>40.72 -74.07</gml:pos></gml:Point>
        </S100:pointProperty>
      </S127:geometry>
    </S127:PilotService>
  </S127:member>

</S127:Dataset>
