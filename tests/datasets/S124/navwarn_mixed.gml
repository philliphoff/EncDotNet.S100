<?xml version="1.0" encoding="UTF-8"?>
<S124:DataSet xmlns:S124="http://www.iho.int/S124/1.0"
              xmlns:S100="http://www.iho.int/S100/profile/s100gml/1.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_NavWarn_Mixed_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-124</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Information type: NavwarnPreamble -->
  <S124:imember>
    <S124:NavwarnPreamble gml:id="info1">
      <S124:messageSeriesIdentifier>
        <S124:warningNumber>305</S124:warningNumber>
        <S124:year>2026</S124:year>
        <S124:productionAgency>NGA</S124:productionAgency>
      </S124:messageSeriesIdentifier>
      <S124:generalArea>North Sea</S124:generalArea>
      <S124:locality>Approaches to Rotterdam</S124:locality>
    </S124:NavwarnPreamble>
  </S124:imember>

  <!-- Information type: References -->
  <S124:imember>
    <S124:References gml:id="info2">
      <S124:referenceCategory>2</S124:referenceCategory>
      <S124:messageReference>HYDROLANT 0412/2026</S124:messageReference>
    </S124:References>
  </S124:imember>

  <!-- Information type: SpatialQuality -->
  <S124:imember>
    <S124:SpatialQuality gml:id="info3">
      <S124:qualityOfPosition>2</S124:qualityOfPosition>
    </S124:SpatialQuality>
  </S124:imember>

  <!-- Feature 1: NavwarnPart point — uncharted obstruction -->
  <S124:member>
    <S124:NavwarnPart gml:id="f1">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1">
            <gml:pos>51.9200 3.9800</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:restriction>7</S124:restriction>
      <S124:warningInformation>
        <S124:information>Uncharted obstruction reported. Least depth unknown.</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

  <!-- Feature 2: NavwarnPart curve — pipeline route -->
  <S124:member>
    <S124:NavwarnPart gml:id="f2">
      <S124:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="c1">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>51.9000 3.9000 51.9100 3.9200 51.9250 3.9500 51.9400 3.9700 51.9500 4.0000</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S124:geometry>
      <S124:restriction>1</S124:restriction>
      <S124:warningInformation>
        <S124:information>Submarine pipeline. Do not anchor or trawl.</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

  <!-- Feature 3: NavwarnAreaAffected surface — military exercise area -->
  <S124:member>
    <S124:NavwarnAreaAffected gml:id="f3">
      <S124:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="s1">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>51.8500 3.8000 51.8500 4.1000 51.9800 4.1000 51.9800 3.8000 51.8500 3.8000</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S124:geometry>
      <S124:restriction>2</S124:restriction>
    </S124:NavwarnAreaAffected>
  </S124:member>

  <!-- Feature 4: TextPlacement for the obstruction -->
  <S124:member>
    <S124:TextPlacement gml:id="f4">
      <S124:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt2">
            <gml:pos>51.9220 3.9830</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S124:geometry>
      <S124:text>OBSTRUCTION</S124:text>
    </S124:TextPlacement>
  </S124:member>

  <!-- Feature 5: NavwarnPart point — second warning, no geometry -->
  <S124:member>
    <S124:NavwarnPart gml:id="f5">
      <S124:restriction>3</S124:restriction>
      <S124:warningInformation>
        <S124:information>Mariners are advised to exercise caution.</S124:information>
      </S124:warningInformation>
    </S124:NavwarnPart>
  </S124:member>

</S124:DataSet>
