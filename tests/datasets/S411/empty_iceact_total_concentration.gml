<?xml version="1.0" encoding="utf-8"?>
<!--
    Synthetic S-411 fixture for pick-report egg-code tests. The JCOMM short
    total-concentration element is present but whitespace-only, while the
    canonical totalConcentration element carries the usable Ct value.
-->
<ice:IceDataSet xmlns:gml="http://www.opengis.net/gml/3.2"
                xmlns:ice="http://www.jcomm.info/ice">
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.EmptyIceact.1">
            <ice:iceact>   </ice:iceact>
            <ice:totalConcentration>70</ice:totalConcentration>
            <ice:iceapc>1</ice:iceapc>
            <ice:icesod>87</ice:icesod>
            <ice:iceflz>7</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.EmptyIceact.1.g">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.0 -85.0 66.0 -84.0 66.5 -84.0 66.5 -85.0 66.0 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
</ice:IceDataSet>
