<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-412 dataset (issue #841): one feature of each warning type in
  Feature Catalogue 1.1.0 draft v009, encoded like the upstream sample
  (4122C00_TESTDS1.GML). Hand-written for the reader, data model and
  portrayal tests; the areas are invented and lie in the North Atlantic.
-->
<S412:Dataset xmlns:S412="http://www.iho.int/S412/1.1"
    xmlns:gml="http://www.opengis.net/gml/3.2"
    xmlns:S100="http://www.iho.int/s100gml/5.0"
    xmlns:xlink="http://www.w3.org/1999/xlink" gml:id="SYN412">
  <gml:boundedBy>
    <gml:Envelope srsName="EPSG:4326">
      <gml:lowerCorner>50.0 -40.0</gml:lowerCorner>
      <gml:upperCorner>62.0 -20.0</gml:upperCorner>
    </gml:Envelope>
  </gml:boundedBy>
  <S100:DatasetIdentificationInformation>
    <S100:encodingSpecification>S-100 Part 10b</S100:encodingSpecification>
    <S100:encodingSpecificationEdition>1.0</S100:encodingSpecificationEdition>
    <S100:productIdentifier>S-412</S100:productIdentifier>
    <S100:productEdition>1.1.0</S100:productEdition>
    <S100:applicationProfile>1</S100:applicationProfile>
    <S100:datasetFileIdentifier>4122C00_SYNTHETIC.GML</S100:datasetFileIdentifier>
    <S100:datasetTitle>Synthetic S-412 warnings</S100:datasetTitle>
    <S100:datasetReferenceDate>2026-11-07</S100:datasetReferenceDate>
    <S100:datasetLanguage>eng</S100:datasetLanguage>
    <S100:datasetTopicCategory>oceans</S100:datasetTopicCategory>
    <S100:datasetPurpose>base</S100:datasetPurpose>
    <S100:updateNumber>0</S100:updateNumber>
  </S100:DatasetIdentificationInformation>
  <S412:members>
    <S412:DataProvider gml:id="DP1">
      <S412:issuingService>
        <S412:metareaName code="1">METAREA I</S412:metareaName>
        <S412:countryName>Example</S412:countryName>
        <S412:issuingServiceName>
          <S412:information>
            <S412:language>eng</S412:language>
            <S412:text>Example Met Service</S412:text>
          </S412:information>
          <S412:information>
            <S412:language>fra</S412:language>
            <S412:text>Service météo exemple</S412:text>
          </S412:information>
        </S412:issuingServiceName>
      </S412:issuingService>
      <S412:contactInformation>
        <S412:agencyName>Example Marine Forecast Desk</S412:agencyName>
        <S412:emailAddress>forecast@example.org</S412:emailAddress>
      </S412:contactInformation>
      <S412:preparationService>
        <S412:issuingServiceName>
          <S412:information>
            <S412:language>eng</S412:language>
            <S412:text>Example Marine Forecast Desk</S412:text>
          </S412:information>
        </S412:issuingServiceName>
        <S412:countryName>Example</S412:countryName>
      </S412:preparationService>
    </S412:DataProvider>

    <S412:WindWarning gml:id="WIND1">
      <S412:fixedDateRange>
        <S412:dateStart><S100:date>2026-11-07</S100:date></S412:dateStart>
        <S412:dateEnd><S100:date>2026-11-08</S100:date></S412:dateEnd>
        <S412:timeOfDayStart>06:00:00Z</S412:timeOfDayStart>
        <S412:timeOfDayEnd>18:00:00Z</S412:timeOfDayEnd>
      </S412:fixedDateRange>
      <S412:weatherWarningMessage>
        <S412:information>
          <S412:language>eng</S412:language>
          <S412:text>Gale warning: southwest 8, increasing 9 later.</S412:text>
        </S412:information>
        <S412:information>
          <S412:language>fra</S412:language>
          <S412:text>Avis de coup de vent : sud-ouest 8, fraîchissant 9 plus tard.</S412:text>
        </S412:information>
      </S412:weatherWarningMessage>
      <S412:issueDate>2026-11-07</S412:issueDate>
      <S412:issueTime>05:30:00Z</S412:issueTime>
      <S412:weatherWarningNumber>W-001</S412:weatherWarningNumber>
      <S412:cancellationDate>2026-11-08T18:00:00Z</S412:cancellationDate>
      <S412:nextUpdateDateTime>2026-11-07T12:00:00Z</S412:nextUpdateDateTime>
      <S412:dataProviderInformation xlink:href="#DP1"/>
      <S412:windWarningThreshold code="2">gale force</S412:windWarningThreshold>
      <S412:beaufortForce code="8">gale</S412:beaufortForce>
      <S412:maximumWindSpeed>
        <S412:information>
          <S412:language>eng</S412:language>
          <S412:text>45 knots</S412:text>
        </S412:information>
      </S412:maximumWindSpeed>
      <S412:geometry>
        <S100:surfaceProperty>
          <S100:Surface gml:id="S_WIND1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>60.0 -38.0 61.0 -34.0 59.0 -32.0 58.0 -36.0 60.0 -38.0</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </S100:Surface>
        </S100:surfaceProperty>
      </S412:geometry>
    </S412:WindWarning>

    <S412:SignificantWaveHeightWarning gml:id="WAVE1">
      <S412:fixedDateRange>
        <S412:dateStart><S100:date>2026-11-07</S100:date></S412:dateStart>
        <S412:dateEnd><S100:date>2026-11-09</S100:date></S412:dateEnd>
        <S412:timeOfDayStart>00:00:00Z</S412:timeOfDayStart>
        <S412:timeOfDayEnd>00:00:00Z</S412:timeOfDayEnd>
      </S412:fixedDateRange>
      <S412:issueDate>2026-11-07</S412:issueDate>
      <S412:weatherWarningNumber>W-002</S412:weatherWarningNumber>
      <S412:cancellationDate>2026-11-09T00:00:00Z</S412:cancellationDate>
      <S412:dataProviderInformation xlink:href="#DP1"/>
      <S412:significantWaveHeightWarningThreshold code="3">six to nine metres</S412:significantWaveHeightWarningThreshold>
      <S412:geometry>
        <S100:surfaceProperty>
          <S100:Surface gml:id="S_WAVE1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>56.0 -30.0 57.0 -26.0 55.0 -24.0 54.0 -28.0 56.0 -30.0</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </S100:Surface>
        </S100:surfaceProperty>
      </S412:geometry>
    </S412:SignificantWaveHeightWarning>

    <S412:RestrictedVisibilityWarning gml:id="VIS1">
      <S412:fixedDateRange>
        <S412:dateStart><S100:date>2026-11-07</S100:date></S412:dateStart>
        <S412:dateEnd><S100:date>2026-11-07</S100:date></S412:dateEnd>
        <S412:timeOfDayStart>00:00:00Z</S412:timeOfDayStart>
        <S412:timeOfDayEnd>12:00:00Z</S412:timeOfDayEnd>
      </S412:fixedDateRange>
      <S412:issueDate>2026-11-07</S412:issueDate>
      <S412:weatherWarningNumber>W-003</S412:weatherWarningNumber>
      <S412:cancellationDate>2026-11-07T12:00:00Z</S412:cancellationDate>
      <S412:geometry>
        <S100:surfaceProperty>
          <S100:Surface gml:id="S_VIS1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>52.0 -38.0 53.0 -35.0 51.0 -34.0 50.0 -37.0 52.0 -38.0</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </S100:Surface>
        </S100:surfaceProperty>
      </S412:geometry>
    </S412:RestrictedVisibilityWarning>

    <S412:ThunderstormWarning gml:id="TS1">
      <S412:fixedDateRange>
        <S412:dateStart><S100:date>2026-11-07</S100:date></S412:dateStart>
        <S412:dateEnd><S100:date>2026-11-08</S100:date></S412:dateEnd>
        <S412:timeOfDayStart>00:00:00Z</S412:timeOfDayStart>
        <S412:timeOfDayEnd>00:00:00Z</S412:timeOfDayEnd>
      </S412:fixedDateRange>
      <S412:issueDate>2026-11-07</S412:issueDate>
      <S412:weatherWarningNumber>W-004</S412:weatherWarningNumber>
      <S412:cancellationDate>2026-11-08T00:00:00Z</S412:cancellationDate>
      <S412:geometry>
        <S100:surfaceProperty>
          <S100:Surface gml:id="S_TS1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>52.0 -28.0 53.0 -24.0 51.0 -22.0 50.0 -26.0 52.0 -28.0</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </S100:Surface>
        </S100:surfaceProperty>
      </S412:geometry>
    </S412:ThunderstormWarning>

    <S412:IceAccretionWarning gml:id="ICE1">
      <S412:fixedDateRange>
        <S412:dateStart><S100:date>2026-11-07</S100:date></S412:dateStart>
        <S412:dateEnd><S100:date>2026-11-10</S100:date></S412:dateEnd>
        <S412:timeOfDayStart>00:00:00Z</S412:timeOfDayStart>
        <S412:timeOfDayEnd>00:00:00Z</S412:timeOfDayEnd>
      </S412:fixedDateRange>
      <S412:issueDate>2026-11-07</S412:issueDate>
      <S412:weatherWarningNumber>W-005</S412:weatherWarningNumber>
      <S412:cancellationDate>2026-11-10T00:00:00Z</S412:cancellationDate>
      <S412:iceAccretionRate code="2">severeIcing</S412:iceAccretionRate>
      <S412:sourceOfIcing code="1">freezingSpray</S412:sourceOfIcing>
      <S412:geometry>
        <S100:surfaceProperty>
          <S100:Surface gml:id="S_ICE1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>62.0 -26.0 62.0 -20.0 60.0 -20.0 60.0 -26.0 62.0 -26.0</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </S100:Surface>
        </S100:surfaceProperty>
      </S412:geometry>
    </S412:IceAccretionWarning>
  </S412:members>
</S412:Dataset>
