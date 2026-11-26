---
name: s129-ukc
description: |
  IHO S-129 Under Keel Clearance Management (Edition 2.0.0, GML,
  application namespace `http://www.iho.int/S129/2.0`): the UKC plan,
  plan area, (almost) non-navigable areas and time-ordered control
  points, the typed plan and validation rules, XSLT portrayal, and the
  S129.Fusion package that combines a plan with S-102, S-104 and S-421.
  Load `s100-gml-products` with it.
  USE FOR: `src/EncDotNet.S100.Datasets.S129/**`,
  `src/EncDotNet.S100.Datasets.S129.Fusion/**`, their test projects,
  `S129DatasetProcessor`, `content/S129/**`, `tests/datasets/S129/**`;
  under keel clearance plans, control points, UKC timelines.
  DO NOT USE FOR: S-104 water levels themselves (s104-water-level),
  S-102 bathymetry (s102-bathymetry), S-421 routes (s421-route-plans),
  UKC/clearance checks in `EncDotNet.S100.Navigation`, rules shared by
  all GML products (s100-gml-products).
---

# S-129 Under Keel Clearance Management

Load `s100-gml-products` too; this skill records only S-129 specifics.

## Code map
- `src/EncDotNet.S100.Datasets.S129/` — `S129DatasetReader`,
  `S129Dataset`, typed `DataModel/S129UnderKeelClearancePlan`,
  `Validation/S129UkcRules` (IDs `S129-R-{clause}`),
  `S129PortrayalCatalogue` (XSLT, Day/Dusk/Night).
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S129DatasetProcessor.cs`
  (uses the shared `GmlFeatureXmlSource<S129Feature>`).
- Catalogue: `content/S129/{fc,pc}/` (FC 2.0.0; rules `main.xsl` plus
  one per feature class and `InformationBox.xsl`, `PlannedRoute.xsl`).
- Fixtures: `tests/datasets/S129/12900MCTDS200TS.gml`,
  `12900MCTDS130TS.gml`.

## Feature model (FC 2.0.0, no information types)
`UnderKeelClearancePlan` (no geometry; vessel ID, `fixedTimeRange`,
`maximumDraught`, source-route reference), `UnderKeelClearancePlanArea`,
`UnderKeelClearanceNonNavigableArea`,
`UnderKeelClearanceAlmostNonNavigableArea` (surfaces) and
`UnderKeelClearanceControlPoint` (point; `distanceAboveUKCLimit`,
`expectedPassingTime`, `expectedPassingSpeed`).

## Product rules
- **Deviation from the shared rules:** the reader matches features
  against a closed `FeatureTypeCodes` set (dataset namespace or empty
  namespace), and it recognises only the `s100gml/5.0` and
  `S100/profile/s100gml/1.0` base namespaces. Samples also declare an
  older `S129/gml/cs0/1.0` prefix alongside the default `S129/2.0`.
- Control points are ordered by `expectedPassingTime` (stable sort).
  Never interpolate UKC values across gaps the producer left.
- Vessel parameters (draught, squat, safety margin) are producer
  inputs; surface them, never recompute UKC.
- References to the source S-421 route, S-102 and S-104 are **text
  identifiers** in Edition 2.0.0 (`S129ExternalReference`), not
  xlinks. The core package never needs those datasets.

## S129.Fusion (`src/EncDotNet.S100.Datasets.S129.Fusion`)
Read-only combination of a typed plan with the datasets it names; it
depends on the S-129, S-102, S-104 and S-421 dataset packages, is not
published to NuGet, and adds no portrayal, renderer or viewer code.
- `Fusion/S129CrossProductResolver.Resolve(plan, bathymetry, waterLevel, route)`
  matches the textual references → `S129ResolvedReferences`
  (`S129ResolvedReference<T>`, or `S129UnresolvedReference` with a
  `S129ReferenceResolutionReason`); never throws on a mismatch.
- `Fusion/S129BathymetryFusion`, `S129WaterLevelFusion`, `S129PlanFusion`
  sample S-102/S-104 coverage sources at a control point (nearest
  cell, nearest time step, no interpolation).
- `Routing/S129RouteBinder.Bind(plan, route, S129RouteBindingOptions)`
  maps each control point to a waypoint (≤200 m), else a leg (≤100 m),
  else `Unmapped` (haversine).
- `Timeline/S129TimelineView` steps through distinct passing times;
  `S129TimelineSamplingMode` = `NearestEarlier` (default),
  `NearestLater`, `Nearest`, `Exact`. No interpolation.
- Tests: `tests/EncDotNet.S100.Datasets.S129.Fusion.Tests/` build
  S-102/S-104/S-421 inputs in `Helpers/SyntheticDatasets.cs` rather than loading real
  datasets — keep it that way.
