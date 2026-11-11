<?xml version="1.0" encoding="UTF-8"?>
<S124:DataSet xmlns:S124="http://www.iho.int/S124/1.0"
              xmlns:S100="http://www.iho.int/S100/profile/s100gml/1.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_NavWarn_Xlinks_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-124</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Preamble -->
  <S124:imember>
    <S124:NavwarnPreamble gml:id="preamble1">
      <S124:messageSeriesIdentifier>
        <S124:warningNumber>42</S124:warningNumber>
        <S124:year>2026</S124:year>
        <S124:productionAgency>NGA</S124:productionAgency>
      </S124:messageSeriesIdentifier>
      <S124:generalArea>Test Area</S124:generalArea>
    </S124:NavwarnPreamble>
  </S124:imember>

  <!-- References info type -->
  <S124:imember>
    <S124:References gml:id="ref1">
      <S124:referenceCategory>2</S124:referenceCategory>
      <S124:messageReference>HYDROLANT 0100/2026</S124:messageReference>
    </S124:References>
  </S124:imember>

  <!-- Affected area feature -->
  <S124:member>
    <S124:NavwarnAreaAffected gml:id="area1">
      <S124:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="surf1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>50.0 -1.0 50.0 1.0 51.0 1.0 51.0 -1.0 50.0 -1.0</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S124:geometry>
      <S124:restriction>2</S124:restriction>
    </S124:NavwarnAreaAffected>
  </S124:member>

  <!-- Text placement feature -->
  <S124:member>
    <S124:TextPlacement gml:id="text1">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="ptx">
            <gml:pos>50.5 0.0</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:text>Test warning label</S124:text>
    </S124:TextPlacement>
  </S124:member>

  <!-- NavwarnPart with xlinks to area and text -->
  <S124:member>
    <S124:NavwarnPart gml:id="part1">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p1">
            <gml:pos>50.5 0.0</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:restriction>3</S124:restriction>
      <S124:warningInformation>
        <S124:information>Test warning text.</S124:information>
      </S124:warningInformation>
      <S124:theCartographicText xlink:href="#text1"/>
      <S124:areaAffected xlink:href="#area1"/>
      <S124:unresolved xlink:href="#nope"/>
    </S124:NavwarnPart>
  </S124:member>

</S124:DataSet>
