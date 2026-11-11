<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-201 Edition 2.0.0 GML fixture.

  Equipment references a non-existent structure via theParentFeature;
  the typed projection must emit an xlink.unresolved diagnostic
  rather than throwing.
-->
<S201:Dataset xmlns:S201="http://www.iho.int/S-201/gml/cs0/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S201_Unresolved">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-201</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S201:member>
    <S201:LightAllAround gml:id="orphanLight">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-orphan">
            <gml:pos>36.95 -76.01</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:theParentFeature xlink:href="#missingStructure"/>
    </S201:LightAllAround>
  </S201:member>

</S201:Dataset>
