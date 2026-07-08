<?xml version="1.0" encoding="utf-8"?>
<!--
    Synthetic CIS-shape S-411 fixture exercising the full range of WMO /
    SIGRID-3 egg-code permutations for visual inspection of the Pick Report
    egg-code control (S-411 Edition 1.2.1 Annex A). Each <ice:seaice> polygon
    is a self-contained egg-code case laid out on a 3x3 grid so every variant
    can be picked and compared side by side:

      1. Three ice types (full egg)          2. Two ice types
      3. Single ice type (folded row)        4. Four types (thinner class out)
      5. Five types (3 in oval, 5th dropped) 6. Snow depth annotation
      7. Missing form-of-ice row             8. Open water (no oval)
      9. Undetermined tokens ('9+', 'X')

    Uses the JCOMM/CIS shape (bare ice:IceDataSet root, ice:IceFeatureMember
    wrappers, shared gml:id="seaice.None" exercising the reader's synthetic-id
    path) with Python-list-style list attributes, mirroring real CIS feeds.
-->
<ice:IceDataSet xmlns:gml="http://www.opengis.net/gml/3.2"
                xmlns:ice="http://www.jcomm.info/ice">
    <!-- Feature 1: Three ice types — full egg, all rows populated, no outside values -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>80</ice:iceact>
            <ice:iceapc>[30, 30, 20]</ice:iceapc>
            <ice:icesod>[85, 84, 81]</ice:icesod>
            <ice:iceflz>[7, 6, 5]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.0 -85.0 66.0 -84.6 66.4 -84.6 66.4 -85.0 66.0 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 2: Two ice types — two columns per row -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>60</ice:iceact>
            <ice:iceapc>[40, 20]</ice:iceapc>
            <ice:icesod>[93, 91]</ice:icesod>
            <ice:iceflz>[6, 4]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.0 -84.6 66.0 -84.2 66.4 -84.2 66.4 -84.6 66.0 -84.6</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 3: Single ice type — partial-concentration row folds away (would repeat Ct) -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>50</ice:iceact>
            <ice:iceapc>[50]</ice:iceapc>
            <ice:icesod>[95]</ice:icesod>
            <ice:iceflz>[7]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.0 -84.2 66.0 -83.8 66.4 -83.8 66.4 -84.2 66.0 -84.2</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 4: Four ice types — thinner 4th class surfaces below the egg (Sd + partial) -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>90</ice:iceact>
            <ice:iceapc>[30, 30, 20, 10]</ice:iceapc>
            <ice:icesod>[87, 85, 84, 99]</ice:icesod>
            <ice:iceflz>[7, 6, 5, 4]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.4 -85.0 66.4 -84.6 66.8 -84.6 66.8 -85.0 66.4 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 5: Five ice types — 3 in oval, 4th shown outside, 5th dropped; undetermined 'X' form preserved -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>95</ice:iceact>
            <ice:iceapc>[40, 30, 20, 7, '3']</ice:iceapc>
            <ice:icesod>[95, 93, 91, 98, 81]</ice:icesod>
            <ice:iceflz>[7, 6, 5, 4, 'X']</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.4 -84.6 66.4 -84.2 66.8 -84.2 66.8 -84.6 66.4 -84.6</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 6: Snow depth — outside annotation 'Snow 12.5 cm' -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>70</ice:iceact>
            <ice:iceapc>[40, 30]</ice:iceapc>
            <ice:icesod>[95, 93]</ice:icesod>
            <ice:iceflz>[7, 6]</ice:iceflz>
            <ice:snowDepth>12.5</ice:snowDepth>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.4 -84.2 66.4 -83.8 66.8 -83.8 66.8 -84.2 66.4 -84.2</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 7: Missing form-of-ice row — only Ct, partials and stages present -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>40</ice:iceact>
            <ice:iceapc>[20, 20]</ice:iceapc>
            <ice:icesod>[91, 85]</ice:icesod>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.8 -85.0 66.8 -84.6 67.2 -84.6 67.2 -85.0 66.8 -85.0</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 8: Open water — Ct 0, no oval, 'Open water' caption -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>0</ice:iceact>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.8 -84.6 66.8 -84.2 67.2 -84.2 67.2 -84.6 66.8 -84.6</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
    <!-- Feature 9: Undetermined tokens — '9+' and 'X'/range values preserved verbatim -->
    <ice:IceFeatureMember>
        <ice:seaice gml:id="seaice.None">
            <ice:iceact>9+</ice:iceact>
            <ice:iceapc>['9+', 'X']</ice:iceapc>
            <ice:icesod>[91, 95]</ice:icesod>
            <ice:iceflz>['4-6', 7]</ice:iceflz>
            <gml:Polygon srsName="http://www.opengis.net/def/crs/EPSG/0/4326" gml:id="seaice.Noneg">
                <gml:exterior>
                    <gml:LinearRing>
                        <gml:posList>66.8 -84.2 66.8 -83.8 67.2 -83.8 67.2 -84.2 66.8 -84.2</gml:posList>
                    </gml:LinearRing>
                </gml:exterior>
            </gml:Polygon>
        </ice:seaice>
    </ice:IceFeatureMember>
</ice:IceDataSet>
