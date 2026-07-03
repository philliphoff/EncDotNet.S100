<?xml version="1.0" encoding="utf-8"?>
<!--
    Synthetic S-411 fixture for the display-mode portrayal tests (issue #416).
    A single JCOMM/CIS-shape sea-ice polygon carries scalar WMO egg-code
    components chosen to fall on distinct, known entries of the upstream WMO
    colour tables so each display mode yields a different, verifiable fill:

      * iceact = 1   -> upstream seaice_wmo_iceact.xsl colorToken '000 100 255'
                        => concentration fill #0064FF; navigational lead 1 (green).
      * icesod = 87  -> upstream seaice_wmo_icesod.xsl colorToken '155 210 000'
                        => stage-of-development fill #9BD200.

    Scalar (not list-style) codes are used deliberately so number() succeeds.
    The adapter holds the WMO iceact/icesod colour tables inline, and an xunit
    parity test guards those inline tables against the bundled upstream
    seaice_wmo_iceact.xsl and seaice_wmo_icesod.xsl tables.
-->
<ice:IceDataSet xmlns:gml="http://www.opengis.net/gml/3.2"
                xmlns:ice="http://www.jcomm.info/ice">
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.DisplayModes.1">
            <ice:iceact>1</ice:iceact>
            <ice:iceapc>1</ice:iceapc>
            <ice:icesod>87</ice:icesod>
            <ice:iceflz>7</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.DisplayModes.1.g">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.0 -85.0 66.0 -84.0 66.5 -84.0 66.5 -85.0 66.0 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
</ice:IceDataSet>
