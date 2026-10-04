<?xml version="1.0" encoding="UTF-8"?>
<!--
  S-128 adapter: draws product coverages as outlines only.

  The bundled portrayal catalogue (iho-ohi/S-128-Product-Specification-Development
  @ c266c43) fills each ElectronicProduct, PhysicalProduct and S100Service
  coverage with a colour at transparency 0.30 (70 % opaque) on OVERRADAR.
  Over an ENC, nested products compound to near-opaque and hide the chart,
  against S-98 Main 9.2.1. Upstream issue #51 recommends dropping the fills,
  and the upstream Lua port (PR #56) comments them out. This adapter follows
  that without editing the bundled files: it imports the upstream main.xsl and,
  for the three product templates, keeps everything the upstream rule emits
  except its areaInstruction.

  Remove this adapter when the bundled catalogue is refreshed past upstream #56
  (EncDotNet.S100 issue #763).
-->
<xsl:transform version="1.0"
               xmlns:xsl="http://www.w3.org/1999/XSL/Transform"
               xmlns:msxsl="urn:schemas-microsoft-com:xslt"
               exclude-result-prefixes="msxsl">
  <xsl:import href="main.xsl"/>
  <xsl:output method="xml" encoding="UTF-8" indent="yes"/>

  <xsl:template match="ElectronicProduct[@primitive='Surface'] | PhysicalProduct[@primitive='Surface'] | S100Service[@primitive='Surface']">
    <xsl:variable name="upstream">
      <xsl:apply-imports/>
    </xsl:variable>
    <xsl:copy-of select="msxsl:node-set($upstream)/*[not(self::areaInstruction)]"/>
  </xsl:template>
</xsl:transform>
