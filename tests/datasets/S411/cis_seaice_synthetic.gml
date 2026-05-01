<?xml version="1.0" encoding="utf-8"?>
<!--
    Synthetic CIS-shape S-411 fixture used by the visual regression tests.
    Mirrors the JCOMM/CIS structure observed in real-world Canadian Ice Service
    feeds: bare ice:IceDataSet root, JCOMM namespace, ice:IceFeatureMember
    wrappers, and every feature sharing gml:id="seaice.None" so that the
    reader's synthetic-id path is exercised. WMO egg-code attributes are
    encoded as Python-list-style strings, mimicking the producer.
-->
<ice:IceDataSet xmlns:gml="http://www.opengis.net/gml/3.2"
                xmlns:ice="http://www.jcomm.info/ice">
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>91</ice:iceact>
            <ice:iceapc>[20, 30, 20, 4, '23']</ice:iceapc>
            <ice:icesod>[87, 85, 84, 99, 81]</ice:icesod>
            <ice:iceflz>[7, 6, 5, 6, 5]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.0 -85.0 66.0 -84.0 66.5 -84.0 66.5 -85.0 66.0 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>74</ice:iceact>
            <ice:iceapc>[40, 30, 4]</ice:iceapc>
            <ice:icesod>[85, 84, 81]</ice:icesod>
            <ice:iceflz>[6, 5, 4]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.5 -85.0 66.5 -84.0 67.0 -84.0 67.0 -85.0 66.5 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
</ice:IceDataSet>
