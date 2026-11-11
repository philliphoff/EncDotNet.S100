<?xml version="1.0" encoding="utf-8"?>
<!--
    Synthetic S-411 fixture for the list-style egg-code portrayal test (issue
    #416). DMI / CIS operational S-411 encode the WMO egg attributes not as a
    bare scalar but as a JSON-style list, thickest-first, e.g.
    <ice:icesod>[95, 93, 91, 98]</ice:icesod>. The WMO egg keys its fill colour
    off the leading (thickest / dominant) element, so the adapter reduces each
    attribute to its first integer code before the colour lookup. This fixture
    exercises that reduction with the exact list shapes seen in real DMI data:

      * iceact = [80, 60]        -> first 80 -> upstream colorToken '255-125-007'
                                    => concentration fill #FF7D07;
                                    navigational lead 8 (red #E00000).
      * icesod = [95, 93, 91, 98] -> first 95 -> upstream colorToken '180 100 050'
                                    => stage-of-development fill #B46432.
-->
<ice:IceDataSet xmlns:gml="http://www.opengis.net/gml/3.2"
                xmlns:ice="http://www.jcomm.info/ice">
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.ListModes.1">
            <ice:iceact>[80, 60]</ice:iceact>
            <ice:iceapc>[8, 2]</ice:iceapc>
            <ice:icesod>[95, 93, 91, 98]</ice:icesod>
            <ice:iceflz>[7, 4]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.ListModes.1.g">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.0 -85.0 66.0 -84.0 66.5 -84.0 66.5 -85.0 66.0 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
</ice:IceDataSet>
