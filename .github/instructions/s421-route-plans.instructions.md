---
applyTo: "src/EncDotNet.S100.Datasets.S421/**,tests/EncDotNet.S100.Datasets.S421.Tests/**,src/EncDotNet.S100.Renderers.Mapsui/MapsuiDisplayListRenderer.cs,src/EncDotNet.S100.Viewer/S421DatasetProcessor.cs,src/EncDotNet.S100.Specifications/content/S421/**"
---

# S-421 editing rules

When modifying S-421 code or assets:

- Load the `s421-route-plans` skill before proposing non-trivial
  changes.
- GML parsing must tolerate both the lowercase `s100gml/1.0` namespace
  used by IEC's S-421 samples and the newer `s100gml/5.0` namespace —
  do not hard-code one.
- Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
  for `EPSG:4326`. Do not assume lon,lat.
- `<member>` children become `S421Feature`; `<imember>` children
  become `S421InformationType`. Do not promote one to the other.
- Preserve every `xlink:href` reference (with its role / `arcrole`) on
  features and information types — these references encode the route
  topology and must round-trip.
- `RouteWaypoints` carries an ordered list of `routeWaypoint`
  references; preserve document order.
- Container objects (`Route`, `RouteWaypoints`, `RouteSchedules`,
  `RouteActionPoints`) have **no** geometry. Do not synthesise it.
- Portrayal goes through XSLT (no Lua). Top-level template is
  `main_Simplified.xsl`; per-feature sub-templates are included via
  `xsl:include`. The `Default.xsl` fallback handles unhandled
  primitives — keep it in the include list.
- Rendered Mapsui features must be tagged with
  `MapsuiDisplayListRenderer.FeatureRefKey` so the viewer's identify
  flow works.
- Cite the S-421 (or IEC 63173-2) section number in XML doc comments
  when adding spec-derived constants or element names.
- Any new public API requires a matching xunit test using the IEC
  sample fixtures under `tests/datasets/S421/`.
