<?xml version="1.0" encoding="UTF-8"?>
<S131:Dataset xmlns:S131="http://www.iho.int/S131/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S131_XlinkTest">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-131</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S131:members>
    <S131:Applicability gml:id="info1">
      <S131:categoryOfCargo>1</S131:categoryOfCargo>
    </S131:Applicability>

    <S131:ContactDetails gml:id="info2">
      <S131:contactInstructions>VHF Ch 12</S131:contactInstructions>
    </S131:ContactDetails>

    <S131:Authority gml:id="f1">
      <S131:applicability xlink:href="#info1"/>
      <S131:contactDetails xlink:href="#info2"/>
    </S131:Authority>

    <S131:Berth gml:id="f2">
      <S131:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1" srsName="EPSG:4326">
            <gml:pos>44.6475 -63.5713</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S131:geometry>
      <S131:applicability xlink:href="#info1"/>
    </S131:Berth>
  </S131:members>
</S131:Dataset>
