<?xml version="1.0" encoding="UTF-8"?>
<S127:Dataset xmlns:S127="http://www.iho.int/S127/2.0"
              xmlns:S100="http://www.iho.int/s100gml/5.0"
              xmlns:gml="http://www.opengis.net/gml/3.2"
              gml:id="DS_S127_Point_Test">

  <S100:DatasetIdentificationInformation>
    <S100:productIdentifier>S-127</S100:productIdentifier>
  </S100:DatasetIdentificationInformation>

  <!-- Feature: PilotBoardingPlace at the harbour approach -->
  <S127:member>
    <S127:PilotBoardingPlace gml:id="f1">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt1">
            <gml:pos>36.9500 -76.0133</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S127:geometry>
      <S127:categoryOfPilotBoardingPlace>1</S127:categoryOfPilotBoardingPlace>
      <S127:contactDetails>
        <S127:callName>HAMPTON ROADS PILOT</S127:callName>
        <S127:contactInstructions>VHF Channel 13</S127:contactInstructions>
      </S127:contactDetails>
    </S127:PilotBoardingPlace>
  </S127:member>

  <!-- Feature: SignalStationTraffic at port entrance -->
  <S127:member>
    <S127:SignalStationTraffic gml:id="f2">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt2">
            <gml:pos>37.0167 -76.3300</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S127:geometry>
      <S127:categoryOfSignalStationTraffic>4</S127:categoryOfSignalStationTraffic>
    </S127:SignalStationTraffic>
  </S127:member>

  <!-- Feature: PlaceOfRefuge -->
  <S127:member>
    <S127:PlaceOfRefuge gml:id="f3">
      <S127:geometry>
        <S100:pointProperty>
          <gml:Point gml:id="pt3">
            <gml:pos>36.9520 -76.0100</gml:pos>
          </gml:Point>
        </S100:pointProperty>
      </S127:geometry>
    </S127:PlaceOfRefuge>
  </S127:member>

</S127:Dataset>
