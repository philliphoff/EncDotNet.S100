<?xml version="1.0" encoding="UTF-8"?>
<!--
    S-411 portrayal adapter — produced by EncDotNet.S100.Datasets.S411.

    The official S-411 1.2.1 portrayal catalogue cannot be driven end-to-end
    by this codebase's Part 9 display-list reader because:

    * Its `mainRule` (`pc/Rules/main.xsl`) calls per-feature templates
      (`seaice.xsl`, `lacice.xsl`, ...) that in 1.2.1 are empty stubs.
    * Its per-class entry-points (`seaice_class_*.xsl`) are empty too.
    * The actual symbology logic lives in WMO sub-templates
      (`seaice_wmo_iceact.xsl`, `lacice_wmo_iceact.xsl`, ...) but they
      declare `xmlns:ice="http://www.iho.int/ice"` while real producers
      use `xmlns:ice="http://www.jcomm.info/ice"`, so XPath silently
      returns nothing.
    * The fragments emit a different display-list dialect
      (e.g. `<symbol><symbolReference>X</symbolReference></symbol>`)
      than this codebase's `Part9DisplayListReader` consumes.

    Rather than mutating the upstream catalogue (it is preserved
    byte-identical under `pc/`), this adapter ships inside the
    EncDotNet.S100.Datasets.S411 library as an embedded resource and is
    used by `S411PortrayalCatalogue` in place of the catalogue's
    `mainRule`. It produces the framework display-list shape directly,
    covering both real-world S-411 GML shapes:

    * The JCOMM/Canadian-Ice-Service operational shape (root
      `<ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice">` containing
      `<ice:IceFeatureMember>` siblings with lowercase short-code feature
      names: `ice:seaice`, `ice:icebrg`, `ice:lacice`, `ice:icelne`, ...).
    * The IHO 1.2.1 sample shape (bare `<Dataset>` root with PascalCase
      Feature Catalogue class names like `SeaIce`, `Iceberg`).

    Symbol identifiers and line-style identifiers used below match those
    declared in the upstream `portrayal_catalogue.xml`. A future revision
    can replace this with a full port of the upstream WMO egg logic.
-->
<xsl:stylesheet
    version="1.0"
    xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
    xmlns:ice="http://www.jcomm.info/ice"
    xmlns:gml="http://www.opengis.net/gml/3.2">

    <xsl:output method="xml" indent="yes" omit-xml-declaration="yes"/>

    <!--
        Active S-100 Part 9 §11.7 display mode, threaded in by the vector
        engine (XsltRuleExecutor) from the catalogue's DisplayModeController.
        Empty when no mode is active. The three S-411 modes declared in
        pc/portrayal_catalogue.xml select which colour ramp `emit-ice-area`
        applies:
          * IceScientificIceactDisplayMode  — total concentration (default)
          * IceScientificIcesodDisplayMode  — stage of development
          * IceNavigationalDisplayMode      — PROVISIONAL navigational preview
                                              (derived from total concentration;
                                              NOT a POLARIS/RIO risk computation)
    -->
    <xsl:param name="displayMode" select="''"/>

    <!--
        WMO colour tables (issue #416).

        The concentration (iceact) and stage-of-development (icesod) colours are
        held INLINE here as pre-converted #RRGGBB literals rather than read from
        the upstream stylesheets at run time. The upstream files
        (pc/Rules/seaice_wmo_iceact.xsl, seaice_wmo_icesod.xsl) remain the
        canonical source of the values and stay byte-identical to upstream; the
        S411 xunit parity test (S411WmoColourParityTests) parses those files,
        converts each `number($iceX)=N -> colorToken` "R G B" entry to #RRGGBB
        and asserts equality with the two templates below — so any upstream drift
        fails the build LOUDLY instead of silently degrading at render time.

        Values were converted with the WMO "R G B" decimal triples mapped to
        hex; the single malformed upstream entry '255-125-007' (dash-separated)
        is sanitised to 255 125 7 -> #FF7D07. The display-list reader accepts
        #RRGGBB hex but not the upstream space-separated "R G B" form.

        Each template returns an #RRGGBB literal for a tabulated integer egg
        code, or the empty string when the code is not tabulated (letting the
        caller fall back to an adapter-authored ramp).
    -->
    <xsl:template name="iceact-fill-hex">
        <xsl:param name="code"/>
        <xsl:choose>
            <xsl:when test="$code=1">#0064FF</xsl:when>
            <xsl:when test="$code=2">#96C8FF</xsl:when>
            <xsl:when test="$code=3">#96C8FF</xsl:when>
            <xsl:when test="$code=10">#8CFFA0</xsl:when>
            <xsl:when test="$code=12">#8CFFA0</xsl:when>
            <xsl:when test="$code=13">#8CFFA0</xsl:when>
            <xsl:when test="$code=20">#8CFFA0</xsl:when>
            <xsl:when test="$code=23">#8CFFA0</xsl:when>
            <xsl:when test="$code=24">#FFFF00</xsl:when>
            <xsl:when test="$code=30">#8CFFA0</xsl:when>
            <xsl:when test="$code=34">#FFFF00</xsl:when>
            <xsl:when test="$code=35">#FFFF00</xsl:when>
            <xsl:when test="$code=40">#FFFF00</xsl:when>
            <xsl:when test="$code=45">#FFFF00</xsl:when>
            <xsl:when test="$code=46">#FFFF00</xsl:when>
            <xsl:when test="$code=50">#FFFF00</xsl:when>
            <xsl:when test="$code=56">#FFFF00</xsl:when>
            <xsl:when test="$code=57">#FF7D07</xsl:when>
            <xsl:when test="$code=60">#FFFF00</xsl:when>
            <xsl:when test="$code=67">#FF7D07</xsl:when>
            <xsl:when test="$code=68">#FF7D07</xsl:when>
            <xsl:when test="$code=70">#FF7D07</xsl:when>
            <xsl:when test="$code=78">#FF7D07</xsl:when>
            <xsl:when test="$code=79">#FF0000</xsl:when>
            <xsl:when test="$code=80">#FF7D07</xsl:when>
            <xsl:when test="$code=81">#FF0000</xsl:when>
            <xsl:when test="$code=89">#FF0000</xsl:when>
            <xsl:when test="$code=90">#FF0000</xsl:when>
            <xsl:when test="$code=91">#FF0000</xsl:when>
            <xsl:when test="$code=92">#910000</xsl:when>
            <xsl:otherwise/>
        </xsl:choose>
    </xsl:template>

    <xsl:template name="icesod-fill-hex">
        <xsl:param name="code"/>
        <xsl:choose>
            <xsl:when test="$code=1">#96C8FF</xsl:when>
            <xsl:when test="$code=70">#96C8FF</xsl:when>
            <xsl:when test="$code=80">#96C8FF</xsl:when>
            <xsl:when test="$code=81">#F0D2FA</xsl:when>
            <xsl:when test="$code=82">#FF64FF</xsl:when>
            <xsl:when test="$code=83">#AA28F0</xsl:when>
            <xsl:when test="$code=84">#873CD7</xsl:when>
            <xsl:when test="$code=85">#DC50EB</xsl:when>
            <xsl:when test="$code=86">#FFFF00</xsl:when>
            <xsl:when test="$code=87">#9BD200</xsl:when>
            <xsl:when test="$code=88">#D7FA82</xsl:when>
            <xsl:when test="$code=89">#AFFA00</xsl:when>
            <xsl:when test="$code=91">#00C814</xsl:when>
            <xsl:when test="$code=93">#007800</xsl:when>
            <xsl:when test="$code=94">#007800</xsl:when>
            <xsl:when test="$code=95">#B46432</xsl:when>
            <xsl:when test="$code=96">#FF780A</xsl:when>
            <xsl:when test="$code=97">#C80000</xsl:when>
            <xsl:otherwise/>
        </xsl:choose>
    </xsl:template>

    <!--
        Reduces a WMO egg attribute to its first integer code.

        DMI / CIS operational S-411 encode the egg attributes not as a bare
        scalar but as a JSON-style list, thickest-first, e.g.
        <ice:icesod>[95, 93, 91, 98]</ice:icesod> or <ice:iceact>[80, 60]</ice:iceact>.
        The WMO egg keys its fill colour off the leading (thickest / dominant)
        element, so this template strips the brackets and whitespace and returns
        the first comma-separated token as a floored integer. Returns the empty
        string when the value is absent or its first token is non-numeric.
        A bare scalar ("80") passes through unchanged.
    -->
    <xsl:template name="first-egg-code">
        <xsl:param name="raw"/>
        <xsl:variable name="stripped" select="translate($raw, '[] ', '')"/>
        <xsl:variable name="first" select="substring-before(concat($stripped, ','), ',')"/>
        <xsl:if test="string($first) != '' and number($first) = number($first)">
            <xsl:value-of select="floor(number($first))"/>
        </xsl:if>
    </xsl:template>

    <!-- ───────── Entry points for the two GML shapes ───────── -->

    <xsl:template match="/ice:IceDataSet">
        <displayList>
            <xsl:for-each select="ice:IceFeatureMember/*">
                <xsl:call-template name="dispatch">
                    <xsl:with-param name="featureType" select="local-name()"/>
                    <xsl:with-param name="featureId" select="@gml:id"/>
                </xsl:call-template>
            </xsl:for-each>
        </displayList>
    </xsl:template>

    <xsl:template match="/Dataset">
        <displayList>
            <xsl:for-each select=".//members/* | .//imembers/*">
                <xsl:call-template name="dispatch">
                    <xsl:with-param name="featureType" select="local-name()"/>
                    <xsl:with-param name="featureId" select="@*[local-name()='id']"/>
                </xsl:call-template>
            </xsl:for-each>
        </displayList>
    </xsl:template>

    <!-- ───────── Per-feature dispatch ───────── -->

    <xsl:template name="dispatch">
        <xsl:param name="featureType"/>
        <xsl:param name="featureId"/>

        <xsl:variable name="ft" select="translate($featureType,
            'ABCDEFGHIJKLMNOPQRSTUVWXYZ',
            'abcdefghijklmnopqrstuvwxyz')"/>

        <xsl:choose>
            <!-- Sea / lake ice areas with WMO egg attributes. -->
            <xsl:when test="$ft = 'seaice' or $ft = 'lacice'">
                <xsl:call-template name="emit-ice-area">
                    <xsl:with-param name="featureType" select="$featureType"/>
                    <xsl:with-param name="featureId" select="$featureId"/>
                </xsl:call-template>
            </xsl:when>

            <!-- Iceberg area: a plain very-light fill with outline. -->
            <xsl:when test="$ft = 'brgare' or $ft = 'icebergarea'">
                <xsl:call-template name="emit-area-fill">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="fillColor" select="'#F0E6FF'"/>
                    <xsl:with-param name="lineColor" select="'#604080'"/>
                    <xsl:with-param name="lineWidth" select="'0.4'"/>
                </xsl:call-template>
            </xsl:when>

            <!-- Line features: distinct colour / dash per type. -->
            <xsl:when test="$ft = 'icelne' or $ft = 'iceedge'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#1F3F7F'"/>
                    <xsl:with-param name="lineWidth" select="'0.6'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'brglne' or $ft = 'icebergLimit' or $ft = 'iceberglimit'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#604080'"/>
                    <xsl:with-param name="lineWidth" select="'0.5'"/>
                    <xsl:with-param name="dashed" select="'1'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'opnlne' or $ft = 'limitofopenwater'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#0080FF'"/>
                    <xsl:with-param name="lineWidth" select="'0.5'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'lkilne' or $ft = 'limitofallknownice'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#1F3F7F'"/>
                    <xsl:with-param name="lineWidth" select="'0.4'"/>
                    <xsl:with-param name="dashed" select="'1'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'i_crac' or $ft = 'lineoficecrack'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#000000'"/>
                    <xsl:with-param name="lineWidth" select="'0.3'"/>
                    <xsl:with-param name="dashed" select="'1'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'i_fral' or $ft = 'lineoficefracture'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#400000'"/>
                    <xsl:with-param name="lineWidth" select="'0.4'"/>
                    <xsl:with-param name="dashed" select="'1'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'i_lead' or $ft = 'lineoficelead'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#0066CC'"/>
                    <xsl:with-param name="lineWidth" select="'0.4'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'i_ridg' or $ft = 'lineoficeridge'">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#806000'"/>
                    <xsl:with-param name="lineWidth" select="'0.5'"/>
                </xsl:call-template>
            </xsl:when>

            <!-- Point features that take a size-keyed iceberg symbol. -->
            <xsl:when test="$ft = 'icebrg' or $ft = 'iceberg'">
                <xsl:call-template name="emit-iceberg-point">
                    <xsl:with-param name="featureId" select="$featureId"/>
                </xsl:call-template>
            </xsl:when>

            <!-- Point features that take a direction-keyed drift symbol. -->
            <xsl:when test="$ft = 'icedft' or $ft = 'icedrift'">
                <xsl:call-template name="emit-icedft-point">
                    <xsl:with-param name="featureId" select="$featureId"/>
                </xsl:call-template>
            </xsl:when>

            <!-- Point feature: ice thickness, keyed on icetty. -->
            <xsl:when test="$ft = 'icethk' or $ft = 'icethickness'">
                <xsl:call-template name="emit-icethk-point">
                    <xsl:with-param name="featureId" select="$featureId"/>
                </xsl:call-template>
            </xsl:when>

            <!-- Other simple point features: registered symbols by short code. -->
            <xsl:when test="$ft = 'flobrg' or $ft = 'floeberg'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'flobrgSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'icecom' or $ft = 'icecompacting'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'icecomSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'icediv' or $ft = 'icedivergence'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'icedivSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'icefra' or $ft = 'icefracture'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'icefraSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'icekel' or $ft = 'icekeelbummock'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'icekelSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'icelea' or $ft = 'icelead'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'iceleaSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'icerdg' or $ft = 'iceridgehummock'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'icerdgSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'icerft' or $ft = 'icerafting'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'icerftSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'iceshr' or $ft = 'iceshear'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'iceshrSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'jmdbrr' or $ft = 'jammedbrashbarrier'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'jmdbrrSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'snwcvr' or $ft = 'snowcover'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'snwcvrSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'stgmlt' or $ft = 'stageofmelt'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'stgmltSymbol'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$ft = 'strptc' or $ft = 'stripsandpatches'">
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'strptcSymbol'"/>
                </xsl:call-template>
            </xsl:when>

            <!-- Fallback: emit a thin neutral outline so unknown features
                 are still visible in the viewer. -->
            <xsl:otherwise>
                <xsl:call-template name="emit-fallback">
                    <xsl:with-param name="featureId" select="$featureId"/>
                </xsl:call-template>
            </xsl:otherwise>
        </xsl:choose>
    </xsl:template>

    <!-- ───────── Sea ice / Lake ice (areas with WMO egg attributes) ───────── -->

    <xsl:template name="emit-ice-area">
        <xsl:param name="featureType"/>
        <xsl:param name="featureId"/>

        <!--
            Fill colour depends on the active display mode ($displayMode):
              * IceScientificIcesodDisplayMode — stage-of-development palette
                (inline `icesod-fill-hex`, mirrored from upstream
                `seaice_wmo_icesod.xsl` and guarded by the parity test).
              * IceNavigationalDisplayMode — PROVISIONAL traffic-light preview
                derived from total concentration (no upstream navigational
                colours ship in 1.2.1). This is a placeholder only; it is NOT
                a POLARIS/RIO navigational-risk computation.
              * default / IceScientificIceactDisplayMode / empty — total
                concentration palette (inline `iceact-fill-hex`, mirrored from
                upstream `seaice_wmo_iceact.xsl` and guarded by the parity test).
            When a code is not tabulated upstream (real CIS data uses tenths /
            list-style codes) the concentration branch falls back to an
            adapter-authored leading-digit ramp so a fill is always produced.
        -->
        <xsl:variable name="iceact" select="ice:iceact | iceact"/>
        <xsl:variable name="iceapc" select="ice:iceapc | iceapc"/>
        <xsl:variable name="icesod" select="ice:icesod | icesod"/>
        <xsl:variable name="icelso" select="ice:icelso | icelso"/>
        <xsl:variable name="iceflz" select="ice:iceflz | iceflz"/>

        <!-- Integer egg codes used to key the WMO colour tables. Egg attributes
             may be a bare scalar ("80") or a WMO list ("[95, 93, 91, 98]", as
             DMI/CIS encode partial concentrations / stages thickest-first); the
             egg keys its colour off the leading element, so reduce each to its
             first integer code (empty when absent or non-numeric). -->
        <xsl:variable name="iceactCode">
            <xsl:call-template name="first-egg-code">
                <xsl:with-param name="raw" select="string($iceact)"/>
            </xsl:call-template>
        </xsl:variable>
        <xsl:variable name="icesodCode">
            <xsl:call-template name="first-egg-code">
                <xsl:with-param name="raw" select="string($icesod)"/>
            </xsl:call-template>
        </xsl:variable>

        <!-- Leading concentration digit (tenths) for the ramp fallbacks; -1
             when no numeric concentration code is present. -->
        <xsl:variable name="lead">
            <xsl:choose>
                <xsl:when test="string($iceactCode) = ''">-1</xsl:when>
                <xsl:when test="number($iceactCode) &gt;= 10"><xsl:value-of select="floor(number($iceactCode) div 10)"/></xsl:when>
                <xsl:otherwise><xsl:value-of select="number($iceactCode)"/></xsl:otherwise>
            </xsl:choose>
        </xsl:variable>

        <xsl:variable name="fillColor">
            <xsl:choose>
                <!-- Stage of development: inline WMO icesod table. -->
                <xsl:when test="$displayMode = 'IceScientificIcesodDisplayMode'">
                    <xsl:variable name="hex">
                        <xsl:if test="string($icesodCode) != ''">
                            <xsl:call-template name="icesod-fill-hex">
                                <xsl:with-param name="code" select="$icesodCode"/>
                            </xsl:call-template>
                        </xsl:if>
                    </xsl:variable>
                    <xsl:choose>
                        <xsl:when test="string($hex) != ''"><xsl:value-of select="$hex"/></xsl:when>
                        <xsl:otherwise>#DDDDDD</xsl:otherwise>
                    </xsl:choose>
                </xsl:when>

                <!-- Navigational: PROVISIONAL adapter-authored traffic light
                     keyed off total concentration (no upstream navigational
                     colours ship in 1.2.1). Placeholder only — NOT a
                     POLARIS/RIO navigational-risk computation. -->
                <xsl:when test="$displayMode = 'IceNavigationalDisplayMode'">
                    <xsl:choose>
                        <xsl:when test="string($iceactCode) = ''">#DDDDDD</xsl:when>
                        <xsl:when test="$lead &lt;= 1">#00C800</xsl:when>
                        <xsl:when test="$lead &lt;= 3">#9ACD32</xsl:when>
                        <xsl:when test="$lead &lt;= 6">#FFD000</xsl:when>
                        <xsl:otherwise>#E00000</xsl:otherwise>
                    </xsl:choose>
                </xsl:when>

                <!-- Concentration (default): inline WMO iceact table with an
                     adapter-authored leading-digit fallback ramp. -->
                <xsl:otherwise>
                    <xsl:variable name="hex">
                        <xsl:if test="string($iceactCode) != ''">
                            <xsl:call-template name="iceact-fill-hex">
                                <xsl:with-param name="code" select="$iceactCode"/>
                            </xsl:call-template>
                        </xsl:if>
                    </xsl:variable>
                    <xsl:choose>
                        <xsl:when test="string($hex) != ''"><xsl:value-of select="$hex"/></xsl:when>
                        <xsl:when test="$lead = 0 or $lead = 1">#E6F2FF</xsl:when>
                        <xsl:when test="$lead = 2">#F5F5DC</xsl:when>
                        <xsl:when test="$lead = 3 or $lead = 4">#FFFFCC</xsl:when>
                        <xsl:when test="$lead = 5 or $lead = 6">#FFCC99</xsl:when>
                        <xsl:when test="$lead = 7 or $lead = 8">#FF9966</xsl:when>
                        <xsl:when test="$lead = 9">#FFFAF0</xsl:when>
                        <xsl:otherwise>#DDDDDD</xsl:otherwise>
                    </xsl:choose>
                </xsl:otherwise>
            </xsl:choose>
        </xsl:variable>

        <areaInstruction>
            <featureReference><xsl:value-of select="$featureId"/></featureReference>
            <viewingGroup>27000</viewingGroup>
            <displayPlane>OverRADAR</displayPlane>
            <drawingPriority>10</drawingPriority>
            <colorFill>
                <color><xsl:value-of select="$fillColor"/></color>
                <transparency>0.4</transparency>
            </colorFill>
        </areaInstruction>
        <lineInstruction>
            <featureReference><xsl:value-of select="$featureId"/></featureReference>
            <viewingGroup>27000</viewingGroup>
            <displayPlane>OverRADAR</displayPlane>
            <drawingPriority>20</drawingPriority>
            <lineStyle>
                <pen width="0.2">
                    <color>#404040</color>
                </pen>
            </lineStyle>
        </lineInstruction>

        <!--
            WMO "egg" content as two text instructions:

            1. The total concentration (`iceact`, the dominant single-digit
               or paired tenths code) is rendered at all scales — this is
               the most informative single number.
            2. A verbose label combining the partial concentrations
               (`iceapc`), stage of development (`icesod` / `icelso`) and
               floe size (`iceflz`) is gated to large-scale views via
               `<scaleMinimum>` (the S-100 Part 9 §11.1 most-zoomed-out
               denominator bound, mapped by MapsuiDisplayListRenderer to
               `MaxVisible`).  Real-world CIS GMLs encode each of these
               attributes as a single Python-list-style string
               (e.g. `[20, 30, 20, 4, '23']`), so the label can become very
               long; gating keeps the small-scale view legible.
        -->
        <xsl:if test="string($iceact) != ''">
            <textInstruction>
                <featureReference><xsl:value-of select="$featureId"/></featureReference>
                <viewingGroup>27000</viewingGroup>
                <displayPlane>OverRADAR</displayPlane>
                <drawingPriority>30</drawingPriority>
                <textPoint>
                    <element>
                        <text><xsl:value-of select="$iceact"/></text>
                    </element>
                </textPoint>
                <font>
                    <bodySize>10</bodySize>
                    <foreground>CHBLK</foreground>
                </font>
            </textInstruction>
        </xsl:if>

        <xsl:variable name="iceapcPart">
            <xsl:if test="string($iceapc) != ''"> Cp<xsl:value-of select="$iceapc"/></xsl:if>
        </xsl:variable>
        <xsl:variable name="sodPart">
            <xsl:choose>
                <xsl:when test="string($icesod) != ''"> S<xsl:value-of select="$icesod"/></xsl:when>
                <xsl:when test="string($icelso) != ''"> S<xsl:value-of select="$icelso"/></xsl:when>
            </xsl:choose>
        </xsl:variable>
        <xsl:variable name="flzPart">
            <xsl:if test="string($iceflz) != ''"> F<xsl:value-of select="$iceflz"/></xsl:if>
        </xsl:variable>
        <xsl:variable name="verbose" select="concat($iceapcPart, $sodPart, $flzPart)"/>
        <xsl:if test="string($verbose) != ''">
            <textInstruction>
                <featureReference><xsl:value-of select="$featureId"/></featureReference>
                <viewingGroup>27000</viewingGroup>
                <displayPlane>OverRADAR</displayPlane>
                <drawingPriority>31</drawingPriority>
                <scaleMinimum>3000000</scaleMinimum>
                <textPoint>
                    <element>
                        <text><xsl:value-of select="$verbose"/></text>
                    </element>
                    <offset>
                        <x>0</x>
                        <y>3</y>
                    </offset>
                </textPoint>
                <font>
                    <bodySize>8</bodySize>
                    <foreground>CHBLK</foreground>
                </font>
            </textInstruction>
        </xsl:if>
    </xsl:template>

    <!-- ───────── Generic primitives ───────── -->

    <xsl:template name="emit-area-fill">
        <xsl:param name="featureId"/>
        <xsl:param name="fillColor"/>
        <xsl:param name="lineColor"/>
        <xsl:param name="lineWidth"/>
        <areaInstruction>
            <featureReference><xsl:value-of select="$featureId"/></featureReference>
            <viewingGroup>27000</viewingGroup>
            <displayPlane>OverRADAR</displayPlane>
            <drawingPriority>10</drawingPriority>
            <colorFill>
                <color><xsl:value-of select="$fillColor"/></color>
                <transparency>0.4</transparency>
            </colorFill>
        </areaInstruction>
        <lineInstruction>
            <featureReference><xsl:value-of select="$featureId"/></featureReference>
            <viewingGroup>27000</viewingGroup>
            <displayPlane>OverRADAR</displayPlane>
            <drawingPriority>20</drawingPriority>
            <lineStyle>
                <pen width="{$lineWidth}">
                    <color><xsl:value-of select="$lineColor"/></color>
                </pen>
            </lineStyle>
        </lineInstruction>
    </xsl:template>

    <xsl:template name="emit-line">
        <xsl:param name="featureId"/>
        <xsl:param name="lineColor"/>
        <xsl:param name="lineWidth"/>
        <xsl:param name="dashed" select="'0'"/>
        <lineInstruction>
            <featureReference><xsl:value-of select="$featureId"/></featureReference>
            <viewingGroup>27000</viewingGroup>
            <displayPlane>OverRADAR</displayPlane>
            <drawingPriority>20</drawingPriority>
            <lineStyle>
                <pen width="{$lineWidth}">
                    <color><xsl:value-of select="$lineColor"/></color>
                </pen>
                <xsl:if test="$dashed = '1'">
                    <dash><start>0</start><length>3.0</length></dash>
                    <dash><start>3.0</start><length>0</length></dash>
                </xsl:if>
            </lineStyle>
        </lineInstruction>
    </xsl:template>

    <xsl:template name="emit-point-symbol">
        <xsl:param name="featureId"/>
        <xsl:param name="symbolId"/>
        <pointInstruction>
            <featureReference><xsl:value-of select="$featureId"/></featureReference>
            <viewingGroup>27000</viewingGroup>
            <displayPlane>OverRADAR</displayPlane>
            <drawingPriority>20</drawingPriority>
            <symbol reference="{$symbolId}">
                <scaleFactor>1</scaleFactor>
                <rotation>0</rotation>
            </symbol>
        </pointInstruction>
    </xsl:template>

    <xsl:template name="emit-fallback">
        <xsl:param name="featureId"/>
        <xsl:variable name="hasArea" select=".//gml:Polygon[1] | .//gml:Surface[1]"/>
        <xsl:variable name="hasLine" select=".//gml:LineString[1] | .//gml:Curve[1]"/>
        <xsl:variable name="hasPoint" select=".//gml:Point[1]"/>
        <xsl:choose>
            <xsl:when test="$hasArea">
                <xsl:call-template name="emit-area-fill">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="fillColor" select="'#EEEEEE'"/>
                    <xsl:with-param name="lineColor" select="'#666666'"/>
                    <xsl:with-param name="lineWidth" select="'0.3'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$hasLine">
                <xsl:call-template name="emit-line">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="lineColor" select="'#666666'"/>
                    <xsl:with-param name="lineWidth" select="'0.3'"/>
                </xsl:call-template>
            </xsl:when>
            <xsl:when test="$hasPoint">
                <!--
                    No registered symbol for unknown feature types; fall back
                    to the catalogue's "undefined" sea-ice symbol so the
                    feature is still picked up by the viewer.
                -->
                <xsl:call-template name="emit-point-symbol">
                    <xsl:with-param name="featureId" select="$featureId"/>
                    <xsl:with-param name="symbolId" select="'seaiceUndefinedSymbol'"/>
                </xsl:call-template>
            </xsl:when>
        </xsl:choose>
    </xsl:template>

    <!-- ───────── Iceberg point (icebsz-keyed symbol) ───────── -->

    <xsl:template name="emit-iceberg-point">
        <xsl:param name="featureId"/>
        <xsl:variable name="icebsz" select="ice:icebsz | icebsz | ice:icebrg/ice:icebsz"/>
        <xsl:variable name="sym">
            <xsl:choose>
                <xsl:when test="number($icebsz) = 1">icebrgIcebsz01Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 2">icebrgIcebsz02Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 3">icebrgIcebsz03Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 4">icebrgIcebsz04Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 5">icebrgIcebsz05Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 6">icebrgIcebsz06Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 7">icebrgIcebsz07Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 8">icebrgIcebsz08Symbol</xsl:when>
                <xsl:when test="number($icebsz) = 9">icebrgIcebsz09Symbol</xsl:when>
                <xsl:otherwise>icebrgIcebsz99Symbol</xsl:otherwise>
            </xsl:choose>
        </xsl:variable>
        <xsl:call-template name="emit-point-symbol">
            <xsl:with-param name="featureId" select="$featureId"/>
            <xsl:with-param name="symbolId" select="$sym"/>
        </xsl:call-template>
    </xsl:template>

    <!-- ───────── Ice drift point (iceddr-keyed symbol) ───────── -->

    <xsl:template name="emit-icedft-point">
        <xsl:param name="featureId"/>
        <xsl:variable name="iceddr" select="ice:iceddr | iceddr | ice:icedft/ice:iceddr"/>
        <xsl:variable name="sym">
            <xsl:choose>
                <xsl:when test="number($iceddr) = 1">icedftIceddr01Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 2">icedftIceddr02Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 3">icedftIceddr03Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 4">icedftIceddr04Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 5">icedftIceddr05Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 6">icedftIceddr06Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 7">icedftIceddr07Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 8">icedftIceddr08Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 9">icedftIceddr09Symbol</xsl:when>
                <xsl:when test="number($iceddr) = 10">icedftIceddr10Symbol</xsl:when>
                <xsl:otherwise>icedftIceddr99Symbol</xsl:otherwise>
            </xsl:choose>
        </xsl:variable>
        <xsl:call-template name="emit-point-symbol">
            <xsl:with-param name="featureId" select="$featureId"/>
            <xsl:with-param name="symbolId" select="$sym"/>
        </xsl:call-template>
    </xsl:template>

    <!-- ───────── Ice thickness point (icetty-keyed symbol) ───────── -->

    <xsl:template name="emit-icethk-point">
        <xsl:param name="featureId"/>
        <xsl:variable name="icetty" select="ice:icetty | icetty | ice:icethk/ice:icetty"/>
        <xsl:variable name="sym">
            <xsl:choose>
                <xsl:when test="number($icetty) = 1">icethkIcetty01Symbol</xsl:when>
                <xsl:when test="number($icetty) = 2">icethkIcetty02Symbol</xsl:when>
                <xsl:otherwise>icethkIcetty99Symbol</xsl:otherwise>
            </xsl:choose>
        </xsl:variable>
        <xsl:call-template name="emit-point-symbol">
            <xsl:with-param name="featureId" select="$featureId"/>
            <xsl:with-param name="symbolId" select="$sym"/>
        </xsl:call-template>
    </xsl:template>

</xsl:stylesheet>
