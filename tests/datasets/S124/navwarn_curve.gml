<?xml version="1.0" encoding="UTF-8"?>
<S124:DataSet xmlns:S124="http://www.iho.int/S124/1.0"
              xmlns:S100="http://www.iho.int/S100/profile/s100gml/1.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_NavWarn_Curve_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-124</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Information type: NavwarnPreamble -->
  <S124:imember>
    <S124:NavwarnPreamble gml:id="info1">
      <S124:messageSeriesIdentifier>
        <S124:warningNumber>99</S124:warningNumber>
        <S124:year>2026</S124:year>
        <S124:productionAgency>USCG</S124:productionAgency>
      </S124:messageSeriesIdentifier>
      <S124:generalArea>Gulf of Mexico</S124:generalArea>
      <S124:locality>Galveston Ship Channel</S124:locality>
    </S124:NavwarnPreamble>
  </S124:imember>

  <!-- Feature: NavwarnPart with curve geometry — submerged cable -->
  <S124:member>
    <S124:NavwarnPart gml:id="f1">
      <S124:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="c1">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>29.3100 -94.7800 29.3200 -94.7700 29.3350 -94.7550 29.3500 -94.7400</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S124:geometry>
      <S124:restriction>1</S124:restriction>
      <S124:warningInformation>
        <S124:information>Submarine cable. Do not anchor.</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

  <!-- Feature: NavwarnPart with curve geometry — dredging operations -->
  <S124:member>
    <S124:NavwarnPart gml:id="f2">
      <S124:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="c2">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>29.3600 -94.8000 29.3550 -94.7900 29.3500 -94.7850</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S124:geometry>
      <S124:restriction>4</S124:restriction>
      <S124:warningInformation>
        <S124:information>Dredging operations in progress. Vessels keep clear.</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

</S124:DataSet>
