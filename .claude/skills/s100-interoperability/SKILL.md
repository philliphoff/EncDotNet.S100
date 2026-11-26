---
name: s100-interoperability
description: |
  Cross-product interoperability and conformance: IHO S-98 (display
  planes, cross-dataset ordering, inter-product suppression, predefined
  display combinations) and IHO S-164 test datasets as implemented here.
  USE FOR: adding or changing an S-98 rule or a product's default
  S98DisplayPlane; cross-dataset stacking bugs (wrong layer on top, S-101
  depth holes under S-102, surfaces crossing land); viewer display
  combinations and their MCP tools; running or extending the S-164 / S-64
  conformance tests and the matrix in docs/design/s164-conformance.md.
  DO NOT USE FOR: generic draw order, declutter or palettes
  (chart-cartography); one product's portrayal (the sXXX skill);
  catalogue/exchange-set framework (s100-framework); driving the viewer
  (viewer-evaluation).
---

# S-98 interoperability & S-164 conformance

## When engaged
- A change to which dataset draws above another, or to what one product hides or clips in another.
- A new product registration needing a default S-98 plane (`ProductRegistration.DefaultPlanes`).
- Display combinations (View pill, `list_display_combinations` / `set_display_combination`).
- Any `tests/EncDotNet.S100.S164.Tests` work, or a fix found by an S-164/S-64 test.

## Spec anchors
- S-98 Ed 2.0.0 (Oct 2025): Main §9.2.1 nine priority layers (informative, from MSC.530(106)/Rev.1), Annex A §4.4 interop levels 0–4, §8.4.1 skin-of-the-earth replacement, §A-6.9.1 S-102 over S-101 fills / under ENC line work, Part B §B-3.1.2. PDFs are **not** in `docs/specs/`; design notes quote them.
- S-98 v2.0.0 covers only S-101/102/104/111/129; every other product is a Level 0 overlay on a derived plane.
- S-100 Part 9 §11.6 intra-product UnderRadar/OverRadar planes; Part 16 IC XSD (not validated — see `TODO PR-L2-RESYNC` in the rules).
- S-164 manual draft 2.0.1 (Parts 1–9) + test data v2.0.0 from `iho-ohi/S-164-Sub-Group`; Part 8 points at S-64:100 Ed 4.0.0.
- Design notes (source of truth for status): `docs/design/s98-interoperability.md`, `docs/design/predefined-display-combinations.md`, `docs/design/s164-conformance.md`.

## Architecture in this repo
- `Core/Interoperability/S98DisplayPlane.cs` — the outer, cross-dataset plane enum (BaseChartUnder 0 … Bathymetry 10, OnDemandSurface 20, BaseChartOver 30, OtherChartOverlays 40, AML 45, CautionsAndWarnings 50, DynamicArrows 60, MarinerOverlay 70, Vessels 75, EcdisAlerts 80; gaps of 10 for IC planes). Distinct from the intra-product `Pipelines.Vector.DisplayPlane`.
- `Core/DatasetPipelines/Interoperability/` (namespace `EncDotNet.S100.Datasets.Pipelines.Interoperability`, despite living in Core):
  - `SubLayerStackItem` + `StackPayload` — renderer-neutral stack slot (plane, within-plane priority, dataset id, payload).
  - `LayerStackBuilder.Build` → `IInteroperabilityAuthority.Sort` / `ApplyRules`. `InteroperabilityAuthority` sorts by plane, then priority, then coarser scale first; `LoadOrderInteroperabilityAuthority` ignores planes (load order = paint order).
  - `S98DefaultRules.Default` — ordered rule list; each rule's output feeds the next. Rules: R-101-102-A/B, R-102-102-A, R-101-124-A, R-104-A, R-101-104-B, R-101-WX-B, R-111-A, R-111-WX-B. Ids with `WX` or `102-102` are SoundCharts extensions "by analogy"; say so in `SpecCitation`.
  - `S98SuppressionPolicy` — single source for which S-101 depth features S-102 suppresses, with the MSC.232(82) safety-contour exception. Both renderers must call it.
  - `DefaultDisplayPlaneAuthority` over `IProductPlaneTable`; the table is the product registry (`Datasets.Pipelines/Registry/ProductRegistration.DefaultPlanes`, `DisplayPlaneAuthorityProvider`) — no per-product `switch`.
- Consumers: `Rendering.Engine/Composition/ChartCompositor.cs` (headless / tiles / CLI) and `Renderers.Mapsui/MapsuiDatasetLayerSession.Stack.cs` + `Interoperability/LayerStackProjector.cs` (viewer).
- Display combinations: `Viewer/Services/DisplayCombinations/` (`DisplayCombination`, `BuiltInDisplayCombinations`, `DisplayCombinationService`), MCP in `Viewer/McpTools/DisplayCombinationTools.cs`, tests in `tests/EncDotNet.S100.Viewer.Tests/DisplayCombination*Tests.cs`. IC-origin combinations (`S100_IC_PredefinedCombination`) wait for an IC loader; none is bundled under `Specifications/content/`.
- Rule tests: `tests/EncDotNet.S100.Pipelines.Tests/S98*Tests.cs`, `LayerStackProjectorTests.cs`, `S101WithS102SessionTests.cs`.

## S-164 harness
- `tests/EncDotNet.S100.S164.Tests` — one class per manual part (`Part2UpdateTests`, `Part3DisplayTests`, `Part4FeatureInformationTests`, `Part5…7`), helpers `S164Portrayal`, `S164Pick`, `S164RouteCheck`; S-64 under `S64/`.
- Data is not committed (no licence). It is pinned by SHA-256 in `s164-v2.0.0.json` and `S64/s64-100-v4.0.0.json` and fetched by `PinnedDownload`.
- Run: `S100_S164_DOWNLOAD=1 dotnet test tests/EncDotNet.S100.S164.Tests`. Without the variable, uncached tests skip (`Assert.SkipWhen`). Cache: `S100_S164_DATA` / `S100_S64_DATA`, else `~/.cache/EncDotNet.S100/{s164,s64}/`. A hash mismatch fails unless downloads are on. CI sets the variable and caches keyed on the manifests (`.github/workflows/ci.yml`).
- Expectations live in the test code, taken from the manual and reference plots. The plots aren't committed; display checks compare headless renders by eye, locally. Accuracy1–15 live in `tests/EncDotNet.S100.Viewer.Tests/MeasureAccuracyTests.cs`.
- When a test starts running, move its matrix row (§7) to Passes/Fails and update the §3 summary counts.

## Review checklist
- Rule condition keys on `LoadedDatasetInfo.Active`, never visibility (`HasActiveProductSet`).
- New rule added to `S98DefaultRules.Default` in the right position, with `RuleId` + `SpecCitation`, and covered for both the engine and Mapsui paths.
- New product sets a default plane in its registration; non-S-98 products go on a derived plane, documented in `S98DisplayPlane` remarks.
- A display combination never touches mariner settings, display planes, palette, the warnings switch, dynamic sources or route state (design note §1.1).
- S-164 change: manifest unchanged unless moving releases; matrix and summary updated; deliberate differences (§5) not "fixed".

## Known pitfalls
- Hiding S-102 instead of deactivating it leaves R-101-102-B suppressing S-101 depth areas → holes in the chart. Combinations use `SetActive`.
- Two "display plane" enums: don't confuse the outer `S98DisplayPlane` with the per-instruction UnderRadar/OverRadar.
- The s98 design note's update paragraph mentions `HeadlessCompositor`; the current headless consumer is `ChartCompositor`.
- Analysis (route check, depth probe) uses every loaded dataset regardless of the active combination, on purpose.
- S-164 archives hold the exchange set under `S100_ROOT/` and use long-form product ids (`INT.IHO.S-101.2.0.0`); both are handled — don't regress `ExchangeSetRoot` / `MapProductIdentifierToSpec`.
