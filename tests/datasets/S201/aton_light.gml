<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-201 Edition 2.0.0 GML fixture: light attribute typing.

  Demonstrates typed light attributes (height, status, verticalDatum,
  effectiveIntensity, peakIntensity), lifecycle dates (installationDate,
  fixedDateRange), and an extra/unknown attribute that should
  round-trip through ExtraAttributes.
-->
<S201:Dataset xmlns:S201="http://www.iho.int/S-201/gml/cs0/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S201_Light">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-201</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S201:member>
    <S201:Lighthouse gml:id="house1">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-house"><gml:pos>36.93 -76.01</gml:pos></gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:AtoNNumber>USCG-99001</S201:AtoNNumber>
      <S201:aidAvailabilityCategory>1</S201:aidAvailabilityCategory>
      <S201:installationDate>1995-04-12T00:00:00Z</S201:installationDate>
      <S201:fixedDateRange>
        <S201:dateStart>1995-04-12T00:00:00Z</S201:dateStart>
        <S201:dateEnd>2099-12-31T00:00:00Z</S201:dateEnd>
      </S201:fixedDateRange>
      <S201:customExperimentalAttr>experiment-value</S201:customExperimentalAttr>
    </S201:Lighthouse>
  </S201:member>

  <S201:member>
    <S201:LightAllAround gml:id="lamp1">
      <S201:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-lamp"><gml:pos>36.93 -76.01</gml:pos></gml:Point>
        </S100:pointProperty>
      </S201:geometry>
      <S201:height>26.5</S201:height>
      <S201:verticalDatum>30</S201:verticalDatum>
      <S201:verticalLength>3.0</S201:verticalLength>
      <S201:effectiveIntensity>1200.0</S201:effectiveIntensity>
      <S201:peakIntensity>1500.0</S201:peakIntensity>
      <S201:status>1 5</S201:status>
      <S201:theParentFeature xlink:href="#house1"/>
    </S201:LightAllAround>
  </S201:member>

</S201:Dataset>
