<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-131 GML fixture covering every typed-projection family:
    f-bollard          HarbourInfrastructure / Bollard          (Point)
    f-anchorage        Layout / AnchorageArea                   (Surface, with hole)
    f-fender           Layout / FenderLine                      (Curve)
    f-coverage         Metadata / DataCoverage                  (Surface)
    f-unknown          OtherFeature (unknown FC code)           (Point)
    f-dup              Layout / Berth, duplicates f-bollard id  (Point, dup id)
    f-dangling         Layout / Berth with unresolved xlink     (Point)
    info-applic        S131Applicability
    info-contact       S131ContactDetails
    info-authority     S131Authority -> info-contact, info-applic (xlinks)
    info-rxn-nautical  S131RxNInformation / NauticalInformation
    info-rxn-regs      S131RxNInformation / Regulations
    info-spatial-q     S131SpatialQuality
    info-mystery       S131OtherInformationType (unknown FC code)
-->
<S131:Dataset xmlns:S131="http://www.iho.int/S131/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S131_TypedTest">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-131</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <S131:members>

    <S131:Applicability gml:id="info-applic">
      <S131:categoryOfCargo>1</S131:categoryOfCargo>
    </S131:Applicability>

    <S131:ContactDetails gml:id="info-contact">
      <S131:contactInstructions>VHF Ch 12</S131:contactInstructions>
    </S131:ContactDetails>

    <S131:Authority gml:id="info-authority">
      <S131:contactDetails xlink:href="#info-contact"/>
      <S131:applicability xlink:href="#info-applic"/>
    </S131:Authority>

    <S131:NauticalInformation gml:id="info-rxn-nautical">
      <S131:information>Tide rip in approaches</S131:information>
    </S131:NauticalInformation>

    <S131:Regulations gml:id="info-rxn-regs">
      <S131:information>Speed limit 5 knots</S131:information>
    </S131:Regulations>

    <S131:SpatialQuality gml:id="info-spatial-q">
      <S131:qualityOfPosition>1</S131:qualityOfPosition>
    </S131:SpatialQuality>

    <S131:MysteryInformation gml:id="info-mystery">
      <S131:somethingNew>future-info</S131:somethingNew>
    </S131:MysteryInformation>

    <S131:Bollard gml:id="f-bollard">
      <S131:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-b" srsName="EPSG:4326">
            <gml:pos>44.6475 -63.5713</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S131:geometry>
      <S131:bollardNumber>B-001</S131:bollardNumber>
      <S131:applicability xlink:href="#info-applic"/>
    </S131:Bollard>

    <S131:AnchorageArea gml:id="f-anchorage">
      <S131:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="srf-a" srsName="EPSG:4326">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>44.64 -63.58 44.64 -63.56 44.66 -63.56 44.66 -63.58 44.64 -63.58</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
                <gml:interior>
                  <gml:LinearRing>
                    <gml:posList>44.645 -63.575 44.645 -63.565 44.655 -63.565 44.655 -63.575 44.645 -63.575</gml:posList>
                  </gml:LinearRing>
                </gml:interior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S131:geometry>
    </S131:AnchorageArea>

    <S131:FenderLine gml:id="f-fender">
      <S131:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="crv-f" srsName="EPSG:4326">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>44.6475 -63.5713 44.6480 -63.5720 44.6485 -63.5725</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S131:geometry>
    </S131:FenderLine>

    <S131:DataCoverage gml:id="f-coverage">
      <S131:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="srf-c" srsName="EPSG:4326">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>44.0 -64.0 44.0 -63.0 45.0 -63.0 45.0 -64.0 44.0 -64.0</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S131:geometry>
    </S131:DataCoverage>

    <S131:MysteryFeature gml:id="f-unknown">
      <S131:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-u" srsName="EPSG:4326">
            <gml:pos>44.0 -63.0</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S131:geometry>
    </S131:MysteryFeature>

    <S131:Berth gml:id="f-bollard">
      <S131:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-d" srsName="EPSG:4326">
            <gml:pos>44.6 -63.6</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S131:geometry>
    </S131:Berth>

    <S131:Berth gml:id="f-dangling">
      <S131:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt-x" srsName="EPSG:4326">
            <gml:pos>44.1 -63.1</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S131:geometry>
      <S131:applicability xlink:href="#missing-target"/>
    </S131:Berth>

  </S131:members>
</S131:Dataset>
