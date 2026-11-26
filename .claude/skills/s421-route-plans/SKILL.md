---
name: s421-route-plans
description: |
  IEC 63173-1 / IHO S-421 Route Plan (Edition 1.0.0, GML, application
  namespace `http://www.iho.int/S421/gml/cs0/1.0`): the Route /
  RouteWaypoints / RouteWaypoint / RouteWaypointLeg / RouteSchedules /
  RouteActionPoint graph linked by xlinks, RouteInfo, the typed route,
  writer and passage-plan conversion, XSLT portrayal
  (`main_Simplified.xsl`), and how S-421 relates to RTZ. Load
  `s100-gml-products` with it.
  USE FOR: `src/EncDotNet.S100.Datasets.S421/**`,
  `tests/EncDotNet.S100.Datasets.S421.Tests/**`, `S421DatasetProcessor`,
  `content/S421/**`, `tests/datasets/S421/**`, the viewer's
  `Routing/Exchange` S-421 format, S-421 output of
  `MapsuiDisplayListRenderer`; route plans, waypoints, legs, schedules,
  action points.
  DO NOT USE FOR: RTZ internals beyond the boundary noted here, route
  checks in `EncDotNet.S100.Navigation`, S-129 UKC (s129-ukc), S-124
  (s124-nav-warnings), rules shared by all GML products
  (s100-gml-products).
---

# S-421 Route Plans

Load `s100-gml-products` too; this skill records only S-421 specifics.

## Code map
- `src/EncDotNet.S100.Datasets.S421/` — `S421DatasetReader`,
  `S421Dataset`, `S421DatasetWriter`, `S421FeatureXmlSource`,
  `S421PortrayalCatalogue`, typed `DataModel/S421RoutePlan` (root
  `S421Route` with `S421Waypoint`, `S421Leg`, `S421ActionPoint`,
  `S421Schedule`), `Validation/S421RoutePlanRules` (IDs
  `S421-R-{clause}`), `DataModel/S421PassagePlan` (`ToPassagePlan` → `EncDotNet.S100.Navigation.PassagePlan`).
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S421DatasetProcessor.cs`
  (`GmlDatasetProcessorBase<S421Feature>`).
- Catalogue: `content/S421/{fc,pc}/`; rules `main_Simplified.xsl`,
  `RouteWaypoint.xsl`, `RouteWaypointLeg.xsl`, `RouteActionPoint.xsl`,
  `attributeRules.xsl`, `simpleLineStyle.xsl`, `Default.xsl`. Day-only
  palette.
- Fixtures: IEC samples `tests/datasets/S421/RTE-TEST-{GMIN,GBASIC,GFULL}.s421.gml`.

## S-421 and RTZ
RTZ (IEC 61174 Annex S, versions 1.0/1.1/1.2) is not an S-100 product
and lives separately in `src/EncDotNet.S100.Routes.Rtz` (`RtzReader`,
`RtzWriter`, its own `ToPassagePlan`). Neither package references the
other; they meet only in the viewer's
`src/EncDotNet.S100.Viewer/Routing/Exchange/` (`IRouteFormat`:
`S421RouteFormat`, `RtzRouteFormat`) and in the shared `PassagePlan`.
Don't add RTZ concerns to the S-421 package.

## Object model
- One `Route` per dataset; it links to `RouteInfo` (information type),
  `RouteWaypoints`, `RouteSchedules` and optionally `RouteActionPoints`.
- `RouteWaypoints` holds an **ordered** list of `routeWaypoint`
  references — preserve document order.
- Each `RouteWaypointLeg` is referenced from the waypoint it **ends**
  at (first waypoint has none); the typed model also reads datasets
  that reference it from the start waypoint (detected by a leg on the
  first waypoint).
- Geometry: `Route`, `RouteWaypoints`, `RouteSchedules`,
  `RouteActionPoints` → none; `RouteWaypoint`, `RouteActionPoint` →
  point; `RouteWaypointLeg` → curve (tolerate multi-position
  `LineStringSegment`).
- `<member>` → `S421Feature`, `<imember>` → `S421InformationType`;
  never promote one to the other. The xlink graph *is* the route
  topology; references must round-trip, including `arcrole`.
- The writer emits Edition 1.0.0 GML that the reader reads back,
  deterministic bytes, UTC times; needs ≥ 2 waypoints.

## Portrayal and rendering
- XSLT `main_Simplified.xsl` includes the per-feature templates and
  `Default.xsl` (keep it in the include list). Unregistered
  sub-templates resolve through
  `GmlPortrayalCatalogueBase.FetchRuleFallbackXmlResolver`.
- Containers flow through XSLT but emit nothing; never synthesise
  pseudo-geometry from their xlinks.
- No S-421-specific renderer: `MapsuiDisplayListRenderer` renders the
  `Part9DisplayListReader` output. Rendered features must carry
  `MapsuiDisplayListRenderer.FeatureRefKey` (= `gml:id`) so identify
  works.
- Fallback colours: prefer the magenta family (`CHMGD`) as upstream
  does.

## Pitfalls
- IEC samples use the lowercase `s100gml/1.0` base namespace.
- Some samples have leading whitespace before the XML declaration; the
  reader `TrimStart`s — keep it.
- Match on-the-wire misspellings, e.g.
  `routeActionPointRequredActionDescription` (missing "i"), used by the
  upstream XSLT and `S421ActionPoint`.
