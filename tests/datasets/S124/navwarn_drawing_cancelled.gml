<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<!-- NAVAREA X 121/26: its part runs 1 to 30 Oct, but the warning cancels itself on 2026-10-12 (#993). Fictitious. -->
<S124:Dataset xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:S124="http://www.iho.int/S124/gml/2.0" xmlns:S100="http://www.iho.int/s100gml/5.0" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xlink="http://www.w3.org/1999/xlink" gml:id="ZZ.S124.NAVAREAX.121.26" xsi:schemaLocation="http://www.iho.int/S124/gml/2.0 https://schemas.s100dev.net/schemas/S124/2.0.0/20250728/124_2.0.0.xsd">
    <S100:DatasetIdentificationInformation>
        <S100:encodingSpecification>S-100 Part 10b</S100:encodingSpecification>
        <S100:encodingSpecificationEdition>1.0</S100:encodingSpecificationEdition>
        <S100:productIdentifier>S-124</S100:productIdentifier>
        <S100:productEdition>2.0.0</S100:productEdition>
        <S100:applicationProfile>1</S100:applicationProfile>
        <S100:datasetFileIdentifier>navwarn_drawing_cancelled.gml</S100:datasetFileIdentifier>
        <S100:datasetTitle>Drawing at the view time</S100:datasetTitle>
        <S100:datasetReferenceDate>2026-10-01</S100:datasetReferenceDate>
        <S100:datasetLanguage>eng</S100:datasetLanguage>
        <S100:datasetAbstract>Synthetic fixture (fictitious) for #993.</S100:datasetAbstract>
        <S100:datasetTopicCategory>oceans</S100:datasetTopicCategory>
        <S100:datasetPurpose>base</S100:datasetPurpose>
        <S100:updateNumber>0</S100:updateNumber>
    </S100:DatasetIdentificationInformation>
    <S124:members>
        <S124:NavwarnPreamble gml:id="ZZ.S124.NAVAREAX.121.26.0">
            <S124:generalArea>
                <S124:locationName>
                    <S124:language>eng</S124:language>
                    <S124:text>Coral Sea</S124:text>
                </S124:locationName>
            </S124:generalArea>
            <S124:messageSeriesIdentifier>
                <S124:agencyResponsibleForProduction>Synthetic Hydrographic Office (fictitious)</S124:agencyResponsibleForProduction>
                <S124:nameOfSeries>NAVAREA X</S124:nameOfSeries>
                <S124:nationality>Zz</S124:nationality>
                <S124:warningNumber>121</S124:warningNumber>
                <S124:warningType code="4">NAVAREA Navigational Warning</S124:warningType>
                <S124:year>2026</S124:year>
            </S124:messageSeriesIdentifier>
            <S124:navwarnTitle>
                <S124:language>eng</S124:language>
                <S124:text>Buoy adrift</S124:text>
            </S124:navwarnTitle>
            <S124:intService>true</S124:intService>
            <S124:navwarnTypeGeneral code="15">Scientific Instruments Change</S124:navwarnTypeGeneral>
            <S124:publicationTime>2026-10-01T00:00:00Z</S124:publicationTime>
            <S124:cancellationDate>2026-10-12T00:00:00Z</S124:cancellationDate>
        </S124:NavwarnPreamble>
        <S124:NavwarnPart gml:id="ZZ.S124.NAVAREAX.121.26.1">
            <S124:fixedDateRange>
                <S124:dateStart>
                    <S100:date>2026-10-01</S100:date>
                </S124:dateStart>
                <S124:dateEnd>
                    <S100:date>2026-10-30</S100:date>
                </S124:dateEnd>
            </S124:fixedDateRange>
            <S124:warningInformation>
                <S124:information>
                    <S124:language>eng</S124:language>
                    <S124:text>NAVAREA X 121/26. BUOY ADRIFT. CANCEL 12 OCT. FICTITIOUS.</S124:text>
                </S124:information>
                <S124:navwarnTypeDetails>Survey operations</S124:navwarnTypeDetails>
            </S124:warningInformation>
            <S124:header xlink:href="#ZZ.S124.NAVAREAX.121.26.0"/>
            <S124:geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="ZZ.S124.NAVAREAX.121.26.1.location" srsName="https://www.opengis.net/def/crs/EPSG/0/4326" srsDimension="2">
                        <gml:pos>-16.45 146.05</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </S124:geometry>
        </S124:NavwarnPart>
    </S124:members>
</S124:Dataset>
