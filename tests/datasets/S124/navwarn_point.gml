<?xml version="1.0" encoding="UTF-8"?>
<S124:DataSet xmlns:S124="http://www.iho.int/S124/1.0"
              xmlns:S100="http://www.iho.int/S100/profile/s100gml/1.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_NavWarn_Point_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-124</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Information type: NavwarnPreamble -->
  <S124:imember>
    <S124:NavwarnPreamble gml:id="info1">
      <S124:messageSeriesIdentifier>
        <S124:warningNumber>42</S124:warningNumber>
        <S124:year>2026</S124:year>
        <S124:productionAgency>US NGA</S124:productionAgency>
      </S124:messageSeriesIdentifier>
      <S124:generalArea>North Atlantic</S124:generalArea>
      <S124:locality>Chesapeake Bay approach</S124:locality>
    </S124:NavwarnPreamble>
  </S124:imember>

  <!-- Feature: NavwarnPart with point geometry — buoy off station -->
  <S124:member>
    <S124:NavwarnPart gml:id="f1">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1">
            <gml:pos>36.9500 -76.0133</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:restriction>7</S124:restriction>
      <S124:warningInformation>
        <S124:information>Buoy reported off station</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

  <!-- Feature: NavwarnPart with point geometry — unlit beacon -->
  <S124:member>
    <S124:NavwarnPart gml:id="f2">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt2">
            <gml:pos>37.0167 -76.3300</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:restriction>3</S124:restriction>
      <S124:warningInformation>
        <S124:information>Light extinguished</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

  <!-- Feature: TextPlacement associated with f1 -->
  <S124:member>
    <S124:TextPlacement gml:id="f3">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt3">
            <gml:pos>36.9520 -76.0100</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:text>BUOY OFF STATION</S124:text>
    </S124:TextPlacement>
  </S124:member>

</S124:DataSet>
