<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-201 GML fixture: equipment-on-structure aggregation.
  The container feature (AtonAggregation) has no geometry — renderers
  must tolerate geometry-less features.
-->
<S201:Dataset xmlns:S201="http://www.iho.int/S-201/gml/cs0/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S201_Xlink">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-201</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S201:member>
    <S201:LightFloat gml:id="structure1">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-s1">
            <gml:pos>36.9500 -76.0133</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S201:geometry>
    </S201:LightFloat>
  </S201:member>

  <S201:member>
    <S201:Light gml:id="equipment1">
      <S201:colour>3</S201:colour>
      <S201:theParentFeature xlink:href="#structure1"/>
    </S201:Light>
  </S201:member>

  <S201:member>
    <S201:AtonAggregation gml:id="agg1">
      <S201:peer xlink:href="#structure1"/>
      <S201:peer xlink:href="#equipment1"/>
    </S201:AtonAggregation>
  </S201:member>

</S201:Dataset>
