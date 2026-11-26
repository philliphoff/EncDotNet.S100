---
name: s131-marine-harbour
description: |
  IHO S-131 Marine Harbour Infrastructure (FC and PC Edition 2.0.0,
  GML, application namespace `http://www.iho.int/S131/2.0`, 1.0 also
  read): berths, bollards, mooring buoys, dolphins, locks, dry docks,
  terminals, harbour basins, authorities, and **Lua portrayal**
  (S-100 Part 9A) through a GML-to-Lua host bridge — the only
  GML+Lua product here. Load `s100-gml-products` with it.
  USE FOR: `src/EncDotNet.S100.Datasets.S131/**`,
  `tests/EncDotNet.S100.Datasets.S131.Tests/**`, `S131DatasetProcessor`,
  `content/S131/**`, `tests/datasets/S131/**`; `S131LuaDataProvider`,
  `S131LuaRuleExecutor`, harbour infrastructure features.
  DO NOT USE FOR: S-101 ENC / ISO 8211 (s101-enc), the Lua engines and
  `LuaRuleExecutor` themselves (s100-framework), S-127
  (s127-marine-services), rules shared by all GML products
  (s100-gml-products).
---

# S-131 Marine Harbour Infrastructure

Load `s100-gml-products` too; this skill records only S-131 specifics.

## Code map
- `src/EncDotNet.S100.Datasets.S131/` — `S131DatasetReader`,
  `S131Dataset`, typed `DataModel/S131HarbourInfrastructureDataset`,
  `Validation/S131HarbourInfrastructureRules`, `S131LuaDataProvider`,
  `S131LuaRuleExecutor`, `S131PortrayalCatalogue` (implements
  `IVectorPortrayalCatalogue` directly; Lua, not
  `GmlPortrayalCatalogueBase`).
- Processor: `src/EncDotNet.S100.Datasets.Pipelines/S131DatasetProcessor.cs`
  — does **not** derive from `GmlDatasetProcessorBase` (XSLT-only); it
  follows the S-101 Lua processor design.
- Catalogue: `content/S131/{fc,pc}/` from
  `iho-ohi/S-131-Product-Specification-Development` @ `46eb6c7`
  (tag `ed2.0.0`); provenance in `content/S131/README.md`. Palettes
  Day/Dusk/Night.
- Fixtures: `tests/datasets/S131/harbour_*.gml`.

## GML + Lua bridge
1. `S131DatasetReader` parses GML into `S131Dataset`.
2. `S131LuaDataProvider` (`ILuaDataProvider`) exposes the GML features
   through the S-101 Lua host API (`HostGetFeatureIDs`,
   `HostFeatureGetCode`, `HostFeatureGetSimpleAttribute`, …) — the
   contract must stay identical to S-101's.
3. `S131LuaRuleExecutor` (`ILuaVectorRuleExecutor`) composes the shared
   `LuaRuleExecutor` (Core `Pipelines/Vector/Lua`) with the provider
   factory and S-131 context bindings. No anchor provider, no
   post-parse transform.
4. Output goes through the shared `DrawingInstructionParser`
   (`src/EncDotNet.S100.Core/Pipelines/Vector/`); extend it rather than
   forking if S-131 emits new instruction forms.

## Product rules
- **One `<S131:members>` container** holds features *and* information
  types (no `<member>`/`<imember>` split; root `<imember>` is still
  read for forward compatibility). Info types are told apart by the
  reader's closed `InformationTypeCodes` set from the FC.
- Numeric IDs: `gml:id` strings map to sequential doubles for Lua
  (features from 1, info types from 100 001). Keep the mapping
  bidirectional and deterministic.
- Geometry is inline, so the provider synthesises spatial records in
  the shape S-101's `HostGetSpatialData` expects (plus a Lua
  `HostGetSpatial` shim).
- Lua rules navigate complex attributes with `"complexCode:index"`
  paths; the provider maps these to GML element traversal.
- The PC declares `TwoShades` where S-101 declares `FourShades`;
  `S131ContextParameterBindings` binds it as the inverse.
- Rules must run on both Lua engines (Lua-CSharp default, MoonSharp
  selectable; both Lua 5.2). The provider already patches a MoonSharp
  nil/void quirk in `contains`.
- Upstream Lua is byte-identical; adaptations go in the dataset
  library (as S-411 keeps its `Adapter/` in its project), not in
  `content/S131/`.
