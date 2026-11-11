<?xml version="1.0" encoding="UTF-8"?>
<S131:Dataset xmlns:S131="http://www.iho.int/S131/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S131_PointTest">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-131</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S131:members>
    <S131:Bollard gml:id="f1">
      <S131:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1" srsName="EPSG:4326">
            <gml:pos>44.6475 -63.5713</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S131:geometry>
      <S131:bollardNumber>B-001</S131:bollardNumber>
      <S131:bollardPull>50</S131:bollardPull>
    </S131:Bollard>

    <S131:MooringBuoy gml:id="f2">
      <S131:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt2" srsName="EPSG:4326">
            <gml:pos>44.6480 -63.5720</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S131:geometry>
      <S131:featureName>
        <S131:name>Mooring Buoy Alpha</S131:name>
        <S131:language>eng</S131:language>
      </S131:featureName>
    </S131:MooringBuoy>

    <S131:ContactDetails gml:id="info1">
      <S131:contactInstructions>Call VHF Ch 12</S131:contactInstructions>
    </S131:ContactDetails>
  </S131:members>
</S131:Dataset>
