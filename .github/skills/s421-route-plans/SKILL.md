---
name: s421-route-plans
description: |
  Expert knowledge of IEC/IHO S-421 Route Plan Product Specification.
  Covers GML encoding (S-100 Part 10b), the S-421 application schema,
  Route / RouteWaypoints / RouteWaypoint / RouteWaypointLeg /
  RouteSchedules / RouteActionPoint object model, xlink:href
  cross-references between objects, RouteInfo information type, and
  XSLT-based portrayal (per-feature templates plus a Default fallback).
  USE FOR: S-421 datasets, route plans, voyage plans, waypoints, route
  legs, action points, GML parsing for S-421, XSLT portrayal of routes,
  vector pipeline changes affecting S-421, S-421 reader/source code,
  S-421 tests, vector pipeline changes affecting S-421. DO NOT USE FOR: S-124
  nav warnings (use s124-nav-warnings), S-129 UKC (use s129-ukc),
  S-101 ENC (use s101-enc), generic GML / framework concerns (use
  s100-framework).
---

# S-421 Route Plans expert

## When engaged
- Tasks touching `src/EncDotNet.S100.Datasets.S421/**` or
  `tests/EncDotNet.S100.Datasets.S421.Tests/**`
- Changes to `MapsuiDisplayListRenderer` that affect S-421 output, or to the S-421 viewer processor (`S421DatasetProcessor`)
- GML / XSLT portrayal changes for route plans
- Bundled S-421 portrayal catalogue assets under
  `src/EncDotNet.S100.Specifications/content/S421/**`

## Spec anchors
- Canonical: **IEC 63173-2** / **S-421** Route Plan based on S-100
- S-100 Part 10b: GML encoding
- S-100 Part 9: Portrayal (XSLT path)
- S-421 Annex A: Feature Catalogue (Route, RouteInfo, RouteWaypoints,
  RouteWaypoint, RouteWaypointLeg, RouteSchedule, RouteSchedules,
  RouteActionPoint, RouteActionPoints, …)
- S-421 Annex B: Portrayal Catalogue (XSLT)

## Object model essentials
- A dataset has exactly one `Route` feature plus a graph of related
  objects connected by `xlink:href` references.
- `Route` is the root: it `xlink`s to `RouteInfo` (information type),
  `RouteWaypoints` (collection), `RouteSchedules` (collection), and
  optionally `RouteActionPoints`.
- `RouteWaypoints` `xlink`s to one or more `RouteWaypoint` features
  (point geometry) in document order along the route.
- `RouteWaypointLeg` features carry curve geometry connecting two
  consecutive waypoints; legs are referenced from waypoints, not from
  the `RouteWaypoints` collection.
- `RouteInfo`, `RouteSchedule`, etc. are non-spatial information types
  and live under `<imember>`, not `<member>`.
- All cross-references use `xlink:href` (and often `xlink:arcrole`);
  preserve them — they ARE the route topology.

## Review checklist
1. GML parsing tolerates both the lowercase `s100gml/1.0` namespace
   used by IEC's published samples and the newer `s100gml/5.0`
   namespace; do not hard-code one.
2. Coordinate ordering in `<gml:pos>` / `<gml:posList>` is **lat lon**
   for `EPSG:4326` (the convention used throughout this repo).
3. `<member>` children become `S421Feature`; `<imember>` children
   become `S421InformationType`. Do not promote one to the other.
4. `S421Feature.References` must capture every `xlink:href` child
   element along with its local name (the role) and `arcrole`.
   `RouteWaypoints` carries a *list* of `routeWaypoint` references
   in document order — preserve that order.
5. Geometry rules:
   - `Route`, `RouteWaypoints`, `RouteSchedules`, `RouteActionPoints`
     are container objects with **no** geometry (`GeometryType.None`).
   - `RouteWaypoint`, `RouteActionPoint` → `Point`.
   - `RouteWaypointLeg` → `Curve` (typically two-point line strings,
     but tolerate `LineStringSegment` with multiple `pos`/`posList`).
6. Portrayal goes through XSLT (no Lua). The `main_Simplified.xsl`
   top-level template includes per-feature sub-templates (Waypoint,
   WaypointLeg, ActionPoint) plus `Default.xsl` for unhandled
   primitives. Do not bypass `Default.xsl`.
7. The product-agnostic renderer (`MapsuiDisplayListRenderer` in
   `EncDotNet.S100.Renderers.Mapsui`) consumes the unified
   `DrawingInstruction` list emitted by `Part9DisplayListReader` and
   keyed by `featureReference` matching `gml:id`. Always tag rendered
   Mapsui features with the renderer's feature-reference key so picking
   works. There is no S-421-specific renderer subclass.
8. Public API changes have xunit tests under
   `tests/EncDotNet.S100.Datasets.S421.Tests/**`. Use the IEC sample
   fixtures under `tests/datasets/S421/` (GMIN, GBASIC, GFULL).

## Known pitfalls in this repo
- Some IEC sample datasets contain a leading whitespace byte before the
  XML declaration. The reader trims; preserve that behaviour.
- The bundled S-421 PC's XSLT files include each other via
  `xsl:include href="..."`. The `S421PortrayalCatalogue.AssetSourceXmlResolver`
  must resolve those by filename within `Rules/`.
- Route portrayal uses palette tokens such as `PLRTE` (route line),
  `CHMGD` (magenta), `CHBLK` (black). When adding fallbacks, prefer
  the magenta family over arbitrary colours.
- Several attribute codes are misspelled in the sample data
  (e.g. `routeActionPointRequredActionDescription` — note the missing
  "i"). Match the on-the-wire spelling, not the intended one.
- Container objects (`Route`, `RouteWaypoints`, …) flow through the
  XSLT but emit no instructions; the renderer correctly skips them.
  Don't synthesise pseudo-geometry from their `xlink` children — that
  belongs to the leg/waypoint features themselves.
