<?xml version="1.0" encoding="UTF-8" standalone="no"?>
<Dataset xmlns:S100="http://www.iho.int/s100gml/5.0" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:s100_profile="http://www.iho.int/S-100/profile/s100_gmlProfile" xmlns:xlink="http://www.w3.org/1999/xlink" gml:id="DS1">
    <S100:DatasetIdentificationInformation>
        <S100:encodingSpecification>S-100 Part 10b</S100:encodingSpecification>
        <S100:encodingSpecificationEdition>1.0</S100:encodingSpecificationEdition>
        <S100:productIdentifier>S-411</S100:productIdentifier>
        <S100:productEdition>1.0.0</S100:productEdition>
        <S100:applicationProfile>1</S100:applicationProfile>
        <S100:datasetFileIdentifier>4112D00TDS001.GML</S100:datasetFileIdentifier>
        <S100:datasetTitle>Sample GML Encoding</S100:datasetTitle>
        <S100:datasetReferenceDate>2001-04-22</S100:datasetReferenceDate>
        <S100:datasetLanguage>eng</S100:datasetLanguage>
        <S100:datasetTopicCategory>oceans</S100:datasetTopicCategory>
        <S100:datasetPurpose>base</S100:datasetPurpose>
        <S100:updateNumber>0</S100:updateNumber>
    </S100:DatasetIdentificationInformation>
    <members>
        <DataCoverage gml:id="ID0">
            <maximumDisplayScale>22000</maximumDisplayScale>
            <minimumDisplayScale>180000</minimumDisplayScale>
            <geometry>
                <S100:surfaceProperty>
                    <S100:Surface gml:id="SID0">
                        <gml:patches>
                            <gml:PolygonPatch>
                                <gml:exterior>
                                    <gml:LinearRing>
                                        <gml:posList>-40.279260358369456 69.98068235213836 -38.94995827536446 69.98068235213836 -38.9559059356911 69.42886568791249 -40.28520801869612 69.42886568791249 -40.279260358369456 69.98068235213836</gml:posList>
                                    </gml:LinearRing>
                                </gml:exterior>
                            </gml:PolygonPatch>
                        </gml:patches>
                    </S100:Surface>
                </S100:surfaceProperty>
            </geometry>
        </DataCoverage>
        <SeaIce gml:id="ID1">
            <snowDepth>10</snowDepth>
            <geometry>
                <S100:surfaceProperty>
                    <S100:Surface gml:id="SID1">
                        <gml:patches>
                            <gml:PolygonPatch>
                                <gml:exterior>
                                    <gml:LinearRing>
                                        <gml:posList>-40.13354268036668 69.92359353498672 -39.69638964635833 69.92155176448463 -39.723154117828216 69.82433805372922 -40.148411831183296 69.82638929895934 -40.13354268036668 69.92359353498672</gml:posList>
                                    </gml:LinearRing>
                                </gml:exterior>
                            </gml:PolygonPatch>
                        </gml:patches>
                    </S100:Surface>
                </S100:surfaceProperty>
            </geometry>
        </SeaIce>
        <LakeIce gml:id="ID2">
            <totalConcentration code="20">20</totalConcentration>
            <geometry>
                <S100:surfaceProperty>
                    <S100:Surface gml:id="SID2">
                        <gml:patches>
                            <gml:PolygonPatch>
                                <gml:exterior>
                                    <gml:LinearRing>
                                        <gml:posList>-39.55661962868218 69.91848873545736 -39.07188531206066 69.9174676262334 -39.09864978353057 69.8151049764821 -39.55364579851885 69.81613107387797 -39.55661962868218 69.91848873545736</gml:posList>
                                    </gml:LinearRing>
                                </gml:exterior>
                            </gml:PolygonPatch>
                        </gml:patches>
                    </S100:Surface>
                </S100:surfaceProperty>
            </geometry>
        </LakeIce>
        <Iceberg gml:id="ID3">
            <icebergSize code="7">07</icebergSize>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID3">
                        <gml:pos>-40.12164735971339 69.75241842833334</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </Iceberg>
        <IceLead gml:id="ID4">
            <iceLeadStatus code="2">02</iceLeadStatus>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID4">
                        <gml:pos>-39.764787740114734 69.75241842833334</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceLead>
        <IceThickness gml:id="ID5">
            <iceAverageThickness>10</iceAverageThickness>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID5">
                        <gml:pos>-39.42874493165933 69.75447667925893</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceThickness>
        <IceEdge gml:id="ID6">
            <geometry>
                <S100:curveProperty>
                    <S100:Curve gml:id="CID6">
                        <gml:segments>
                            <gml:LineStringSegment>
                                <gml:posList>-40.12462118987671 69.57880801899854 -39.20273383924683 69.57880801899854</gml:posList>
                            </gml:LineStringSegment>
                        </gml:segments>
                    </S100:Curve>
                </S100:curveProperty>
            </geometry>
        </IceEdge>
        <IcebergLimit gml:id="ID8">
            <geometry>
                <S100:curveProperty>
                    <S100:Curve gml:id="CID8">
                        <gml:segments>
                            <gml:LineStringSegment>
                                <gml:posList>-40.118673529550065 69.50813272244953 -39.19381234875688 69.50813272244953</gml:posList>
                            </gml:LineStringSegment>
                        </gml:segments>
                    </S100:Curve>
                </S100:curveProperty>
            </geometry>
        </IcebergLimit>
        <SnowCover gml:id="ID9">
            <snowCoverConcentration code="8">08</snowCoverConcentration>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID9">
                        <gml:pos>-40.13354268036668 69.65545421709106</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </SnowCover>
        <StageOfMelt gml:id="ID10">
            <meltStage code="99">99</meltStage>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID10">
                        <gml:pos>-39.78857838142131 69.65958939801016</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </StageOfMelt>
        <IceKeelBummock gml:id="ID7">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID7">
                        <gml:pos>-39.43171876182265 69.66062306746849</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceKeelBummock>
    </members>
</Dataset>
