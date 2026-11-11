<?xml version="1.0" encoding="UTF-8"?>
<!--
  Synthetic S-125 sample dataset around the Chesapeake Bay approaches.
  Exercises a representative cross-section of S-125 1.0.0 feature
  classes (lateral/cardinal/safe-water/special-purpose buoys, beacons,
  landmarks, sectored lights, AIS aids, navigation lines, data
  coverage) plus an AtoN status indication referencing an
  AtonStatusInformation instance via xlink:href.
-->
<S125:Dataset xmlns:S125="http://www.iho.int/S125/1.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              xmlns:xlink="http://www.w3.org/1999/xlink"
              gml:id="DS_S125_ChesapeakeApproach">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-125</S100:productIdentifier>
    <S100:datasetTitle>Chesapeake Bay Approach AtoNs (synthetic)</S100:datasetTitle>
  </S100:DatasetIdentificationInformation>

  <!-- ── Information types ─────────────────────────────────── -->

  <S125:imember>
    <S125:AtonStatusInformation gml:id="status_temporary">
      <S125:changeTypes>1</S125:changeTypes>
      <S125:fixedDateRange>
        <S125:dateStart>2026-04-01</S125:dateStart>
        <S125:dateEnd>2026-06-30</S125:dateEnd>
      </S125:fixedDateRange>
    </S125:AtonStatusInformation>
  </S125:imember>

  <S125:imember>
    <S125:AtonStatusInformation gml:id="status_permanent">
      <S125:changeTypes>2</S125:changeTypes>
    </S125:AtonStatusInformation>
  </S125:imember>

  <!-- ── Data coverage ─────────────────────────────────────── -->

  <S125:member>
    <S125:DataCoverage gml:id="coverage1">
      <S125:geometry>
        <S100:surfaceProperty>
          <gml:Surface gml:id="cov_surf">
            <gml:patches>
              <gml:PolygonPatch>
                <gml:exterior>
                  <gml:LinearRing>
                    <gml:posList>36.85 -76.20 36.85 -75.85 37.10 -75.85 37.10 -76.20 36.85 -76.20</gml:posList>
                  </gml:LinearRing>
                </gml:exterior>
              </gml:PolygonPatch>
            </gml:patches>
          </gml:Surface>
        </S100:surfaceProperty>
      </S125:geometry>
    </S125:DataCoverage>
  </S125:member>

  <!-- ── Lateral buoys (port and starboard) ────────────────── -->

  <S125:member>
    <S125:LateralBuoy gml:id="lb_port_1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_lb1"><gml:pos>36.9500 -76.0133</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLateralMark>1</S125:categoryOfLateralMark>
      <S125:colour>4</S125:colour>
      <S125:objectName>
        <S125:name>Buoy 1A</S125:name>
      </S125:objectName>
      <S125:AtoNStatus xlink:href="#status_temporary"/>
    </S125:LateralBuoy>
  </S125:member>

  <S125:member>
    <S125:LateralBuoy gml:id="lb_stbd_2">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_lb2"><gml:pos>36.9520 -76.0090</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLateralMark>2</S125:categoryOfLateralMark>
      <S125:colour>3</S125:colour>
      <S125:objectName>
        <S125:name>Buoy 2A</S125:name>
      </S125:objectName>
    </S125:LateralBuoy>
  </S125:member>

  <S125:member>
    <S125:LateralBuoy gml:id="lb_port_3">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_lb3"><gml:pos>36.9650 -76.0050</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLateralMark>1</S125:categoryOfLateralMark>
      <S125:colour>4</S125:colour>
    </S125:LateralBuoy>
  </S125:member>

  <!-- ── Cardinal buoys ────────────────────────────────────── -->

  <S125:member>
    <S125:CardinalBuoy gml:id="cb_n">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_cb1"><gml:pos>37.0500 -76.1000</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfCardinalMark>1</S125:categoryOfCardinalMark>
    </S125:CardinalBuoy>
  </S125:member>

  <S125:member>
    <S125:CardinalBuoy gml:id="cb_s">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_cb2"><gml:pos>36.8800 -76.0200</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfCardinalMark>3</S125:categoryOfCardinalMark>
    </S125:CardinalBuoy>
  </S125:member>

  <!-- ── Safe water + special purpose ──────────────────────── -->

  <S125:member>
    <S125:SafeWaterBuoy gml:id="sw_1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_sw1"><gml:pos>36.9200 -75.9500</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
    </S125:SafeWaterBuoy>
  </S125:member>

  <S125:member>
    <S125:SpecialPurposeGeneralBuoy gml:id="sp_1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_sp1"><gml:pos>36.9700 -75.9300</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfSpecialPurposeMark>2</S125:categoryOfSpecialPurposeMark>
    </S125:SpecialPurposeGeneralBuoy>
  </S125:member>

  <!-- ── Beacons ───────────────────────────────────────────── -->

  <S125:member>
    <S125:LateralBeacon gml:id="bn_lat">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_bn1"><gml:pos>36.9300 -76.0500</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLateralMark>1</S125:categoryOfLateralMark>
      <S125:beaconShape>4</S125:beaconShape>
    </S125:LateralBeacon>
  </S125:member>

  <S125:member>
    <S125:IsolatedDangerBeacon gml:id="bn_idg">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_bn2"><gml:pos>36.9000 -76.0400</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
    </S125:IsolatedDangerBeacon>
  </S125:member>

  <!-- ── Landmarks (lighthouse-like) ───────────────────────── -->

  <S125:member>
    <S125:Landmark gml:id="lm_capehenry">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_lm1"><gml:pos>36.9259 -76.0089</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLandmark>17</S125:categoryOfLandmark>
      <S125:objectName>
        <S125:name>Cape Henry Light</S125:name>
      </S125:objectName>
      <S125:height>49</S125:height>
    </S125:Landmark>
  </S125:member>

  <S125:member>
    <S125:Landmark gml:id="lm_thimbleshoals">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_lm2"><gml:pos>37.0050 -76.0830</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLandmark>17</S125:categoryOfLandmark>
      <S125:objectName>
        <S125:name>Thimble Shoal Light</S125:name>
      </S125:objectName>
      <S125:AtoNStatus xlink:href="#status_permanent"/>
    </S125:Landmark>
  </S125:member>

  <!-- ── Sectored light (co-located with Cape Henry) ───────── -->

  <S125:member>
    <S125:LightSectored gml:id="ls_capehenry">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_ls1"><gml:pos>36.9259 -76.0089</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfLight>4</S125:categoryOfLight>
      <S125:colour>1</S125:colour>
    </S125:LightSectored>
  </S125:member>

  <!-- ── AIS aids ──────────────────────────────────────────── -->

  <S125:member>
    <S125:VirtualAISAidToNavigation gml:id="vais_1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_vais1"><gml:pos>37.0200 -75.9000</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:categoryOfSyntheticAISAidToNavigation>1</S125:categoryOfSyntheticAISAidToNavigation>
      <S125:MMSICode>993661001</S125:MMSICode>
    </S125:VirtualAISAidToNavigation>
  </S125:member>

  <S125:member>
    <S125:PhysicalAISAidToNavigation gml:id="pais_1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="p_pais1"><gml:pos>36.9259 -76.0089</gml:pos></gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:MMSICode>993661002</S125:MMSICode>
    </S125:PhysicalAISAidToNavigation>
  </S125:member>

  <!-- ── Navigation line / recommended track ───────────────── -->

  <S125:member>
    <S125:NavigationLine gml:id="navline_1">
      <S125:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="c_nl1">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>36.9500 -76.0133 36.9650 -76.0050 36.9800 -75.9700 37.0000 -75.9400</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S125:geometry>
      <S125:orientation>045</S125:orientation>
    </S125:NavigationLine>
  </S125:member>

  <S125:member>
    <S125:RecommendedTrack gml:id="rt_1">
      <S125:geometry>
        <S100:curveProperty>
          <gml:Curve gml:id="c_rt1">
            <gml:segments>
              <gml:LineStringSegment>
                <gml:posList>36.9259 -76.0089 36.9500 -76.0133 36.9700 -76.0150 37.0050 -76.0830</gml:posList>
              </gml:LineStringSegment>
            </gml:segments>
          </gml:Curve>
        </S100:curveProperty>
      </S125:geometry>
      <S125:orientation>315</S125:orientation>
    </S125:RecommendedTrack>
  </S125:member>

  <!-- ── AtoN status indication area ───────────────────────── -->

  <S125:member>
    <S125:AtonStatusIndication gml:id="asi_1">
      <S125:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="asi_pt" srsName="EPSG:4326">
            <gml:pos>36.950 -76.013</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S125:geometry>
      <S125:changeTypes>1</S125:changeTypes>
      <S125:AtoNStatus xlink:href="#status_temporary"/>
    </S125:AtonStatusIndication>
  </S125:member>

</S125:Dataset>
