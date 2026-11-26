---
name: s411-sea-ice
description: |
  S-411 Ice Information (IHO/JCOMM, Edition 1.2.1, GML) specifics for
  this repo: the two GML shapes (JCOMM ice:IceDataSet and IHO
  <Dataset>), pass-through of the original GML to XSLT, the
  Adapter/main.xsl substitute for the upstream mainRule, WMO egg-code
  colour tables and the three display modes. USE FOR: changes under
  src/EncDotNet.S100.Datasets.S411/ (incl. Adapter/main.xsl),
  tests/EncDotNet.S100.Datasets.S411.Tests/, tests/**/S411*.cs,
  src/EncDotNet.S100.Specifications/content/S411/,
  S411DatasetProcessor / S411DisplayModes; sea ice, lake ice, icebergs,
  ice edges, egg codes. Load s100-gml-products too. DO NOT USE FOR:
  S-412 weather (marine-weather), S-124 (s124-nav-warnings), S-421 route
  plans (s421-route-plans).
---

# S-411 Ice Information

Load `s100-gml-products` too; this skill records only S-411 specifics.

## Code map
- Reader/model/catalogue: `src/EncDotNet.S100.Datasets.S411/`
  (`S411DatasetReader`, `S411Dataset`, typed `S411SeaIceInventory`,
  `S411FeatureXmlSource`, `S411PortrayalCatalogue`, `S411IceColours`).
- Adapter: `src/EncDotNet.S100.Datasets.S411/Adapter/main.xsl`
  (embedded resource — not under the `content/` tree).
- Processor and modes: `src/EncDotNet.S100.Datasets.Pipelines/`
  `S411DatasetProcessor.cs`, `S411DisplayModes.cs`.
- Egg-code pick section: `IceEggCodeSection` / `IceEggCodeContributor`
  in `src/EncDotNet.S100.Core/DatasetPipelines/`.
- Tests: `tests/EncDotNet.S100.Datasets.S411.Tests/` plus
  `tests/EncDotNet.S100.Pipelines.Tests/S411*.cs` (display-mode and
  WMO colour parity tests). Fixtures: `tests/datasets/S411/`.

## Spec anchors
- Canonical: **S-411 Edition 1.2.1** (bundled FC and PC 1.2.1).
  Upstream: iho-ohi/S-411-Product-Specification.
- Bundled assets are © JCOMM/IHO; follow the upstream publication terms.

## Two GML shapes (both supported, never normalised)
1. **JCOMM / Canadian Ice Service operational** — root
   `<ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice">`, one feature
   per `<ice:IceFeatureMember>`, lowercase short codes (`ice:seaice`,
   `ice:icebrg`, `ice:lacice`, `ice:icelne`, …), direct
   `gml:Polygon` / `gml:LineString` / `gml:Point`, simple attribute
   values like `<ice:iceact>91</ice:iceact>`.
2. **IHO 1.2.1 sample** — bare `<Dataset>` (no namespace) with
   `xmlns:S100="http://www.iho.int/s100gml/5.0"`, `<members>` wrapper,
   PascalCase FC names (`SeaIce`, `Iceberg`, …), geometry under
   `geometry/<…Property>`, product identifier `S-411`.

`S100Products.S411` detects either (root `IceDataSet`, the JCOMM
namespace, or `productIdentifier`). The reader keeps the parsed
`XDocument` as `S411Dataset.SourceDocument`, and `S411FeatureXmlSource`
hands it unchanged to XSLT — S-411 does not use the neutral FeatureXML
form. Other reader quirks: leading whitespace before the XML
declaration is tolerated; `datasetReferenceDate` gives `IssueDate`.
S-411 has no information types — don't add info-type plumbing.

## Portrayal
- `content/S411/pc/` is upstream, byte-identical. Its `mainRule` and
  per-class entry points are empty, its WMO sub-templates declare
  `xmlns:ice="http://www.iho.int/ice"` (real data uses
  `http://www.jcomm.info/ice`, so they match nothing), and it emits
  `<symbol><symbolReference>X</symbolReference></symbol>` instead of the
  `Part9DisplayListReader` dialect. Do not "fix" upstream.
- `S411PortrayalCatalogue.GetCompiledRuleAsync("mainRule")` substitutes
  the embedded `Adapter/main.xsl`, which has entry templates for both
  shapes (`/ice:IceDataSet` and `/Dataset`). Only `mainRule` is an active
  rule; class-specific rules stay loadable by name.
- The WMO concentration (`iceact`) and stage-of-development (`icesod`)
  colour tables are inline in the adapter, mirrored from upstream
  `seaice_wmo_*.xsl`; `S411WmoColourParityTests` keeps them equal and
  `S411IceColours` reads them for legends.
- Display modes (`S411DisplayModes`): total concentration (default),
  stage of development, and a provisional navigational preview — shared
  by CLI `render --display-mode` and the viewer.
- No colour profiles ship; `S411PortrayalCatalogue` loads only
  explicitly named palettes.
