<?xml version="1.0" encoding="UTF-8" standalone="no"?>
<Dataset xmlns:S100="http://www.iho.int/s100gml/5.0" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:s100_profile="http://www.iho.int/S-100/profile/s100_gmlProfile" xmlns:xlink="http://www.w3.org/1999/xlink" gml:id="DS1">
    <S100:DatasetIdentificationInformation>
        <S100:encodingSpecification>S-100 Part 10b</S100:encodingSpecification>
        <S100:encodingSpecificationEdition>1.0</S100:encodingSpecificationEdition>
        <S100:productIdentifier>S-411</S100:productIdentifier>
        <S100:productEdition>1.0.0</S100:productEdition>
        <S100:applicationProfile>1</S100:applicationProfile>
        <S100:datasetFileIdentifier>4112C000TDS02.GML</S100:datasetFileIdentifier>
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
        <IceLead gml:id="ID4">
            <iceLeadStatus code="2">02</iceLeadStatus>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID4">
                        <gml:pos>-39.98043689305149 69.75873117725548</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceLead>
        <IceThickness gml:id="ID5">
            <iceAverageThickness>10</iceAverageThickness>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID5">
                        <gml:pos>-39.84661453570196 69.7617050074188</gml:pos>
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
                        <gml:pos>-39.73063515933237 69.76021809233714</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </SnowCover>
        <StageOfMelt gml:id="ID10">
            <meltStage code="99">99</meltStage>
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID10">
                        <gml:pos>-39.60573429247282 69.7661657526638</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </StageOfMelt>
        <IceKeelBummock gml:id="ID7">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID7">
                        <gml:pos>-39.48975491610323 69.7661657526638</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceKeelBummock>
        <IceCompacting gml:id="ID11">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID11">
                        <gml:pos>-39.364854049243675 69.76021809233714</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceCompacting>
        <IceShear gml:id="ID12">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID12">
                        <gml:pos>-40.1261545710543 69.69776765890737</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceShear>
        <IceDivergence gml:id="ID13">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID13">
                        <gml:pos>-39.987871468459794 69.6962807438257</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceDivergence>
        <IceRidgeHummock gml:id="ID14">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID14">
                        <gml:pos>-39.851075280946944 69.70222840415235</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceRidgeHummock>
        <IceFracture gml:id="ID15">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID15">
                        <gml:pos>-39.735095904577356 69.7007414890707</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceFracture>
        <IceRafting gml:id="ID16">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID16">
                        <gml:pos>-39.6101950377178 69.70817606447899</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </IceRafting>
        <JammedBrashBarrier gml:id="ID17">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID17">
                        <gml:pos>-39.49272874626655 69.71263680972397</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </JammedBrashBarrier>
        <StripsAndPatches gml:id="ID18">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID18">
                        <gml:pos>-39.367827879406995 69.71263680972397</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </StripsAndPatches>
        <GroundedHummock gml:id="ID19">
            <geometry>
                <S100:pointProperty>
                    <S100:Point gml:id="PID19">
                        <gml:pos>-40.12164735971339 69.75241842833334</gml:pos>
                    </S100:Point>
                </S100:pointProperty>
            </geometry>
        </GroundedHummock>
        <LineOfIceFracture gml:id="ID20">
            <geometry>
                <S100:curveProperty>
                    <S100:Curve gml:id="CID20">
                        <gml:segments>
                            <gml:LineStringSegment>
                                <gml:posList>-40.12020691072766 69.45688741567822 -39.19237189977095 69.45837433075988</gml:posList>
                            </gml:LineStringSegment>
                        </gml:segments>
                    </S100:Curve>
                </S100:curveProperty>
            </geometry>
        </LineOfIceFracture>
        <LineOfIceLead gml:id="ID21">
            <geometry>
                <S100:curveProperty>
                    <S100:Curve gml:id="CID21">
                        <gml:segments>
                            <gml:LineStringSegment>
                                <gml:posList>-40.12318074089098 69.54312849041457 -39.20426722042424 69.54312849041457</gml:posList>
                            </gml:LineStringSegment>
                        </gml:segments>
                    </S100:Curve>
                </S100:curveProperty>
            </geometry>
        </LineOfIceLead>
    </members>
</Dataset>

