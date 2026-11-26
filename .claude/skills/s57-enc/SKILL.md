---
name: s57-enc
description: |
  Expert knowledge of IHO S-57 Edition 3.1 ENC support in this repo:
  reading cells and update files, detecting S-57 vs S-101/S-401/AML `.000`
  files, the S-57 → S-101 (and inland → S-401) translation that is the only
  way S-57 is drawn, and the reverse S-101 → S-57 conversion under S-65
  Annex C with its round-trip harness, and the Esri S-57 NIS → S-57 cell
  conversion. USE FOR: `src/EncDotNet.S100.Datasets.S57/**`,
  `S57DatasetProcessor`, `S57ToS101Translator`, `S101ToS57Translator`,
  `S57S101Mapping`/`DefaultRules`/`InlandRules`, `S57UpdateApplicator`,
  S-57 exchange sets (`CATALOG.031`), `s100 s57 convert`,
  `s100 s101 convert --to s57`, `src/EncDotNet.S100.Datasets.Nis/**`,
  `NisToS57Translator`, `FileGdbNisSource`, `s100 nis convert`,
  translation diagnostics, SCAMIN/CSCL cell
  display window, S-57 tests and the NOAA round trip.
  DO NOT USE FOR: S-101 model, ISO 8211 reading or Lua portrayal rules
  (use s101-enc), generic S-100 framework/catalogue questions (use
  s100-framework), scale-band/declutter/draw-order principles (use
  chart-cartography), CLI command plumbing in general (use s100-cli).
---

# S-57 ENC expert

## When engaged
- `src/EncDotNet.S100.Datasets.S57/**`, `tests/EncDotNet.S100.Datasets.S57.Tests/**`
- `src/EncDotNet.S100.Datasets.Pipelines/S57DatasetProcessor.cs`, the S-57/AML
  registrations in `Registry/S100Products.cs`, `Registry/DatasetIso8211Matcher.cs`
- CLI `tools/EncDotNet.S100.Cli/Commands/S57Convert*.cs`, `S101Convert*.cs`,
  `NisConvert*.cs`
- `src/EncDotNet.S100.Datasets.Nis/**`, `tests/EncDotNet.S100.Datasets.Nis.Tests/**`
- Docs: `docs/s57-to-s101.md`, `docs/s101-to-s57.md`, `docs/nis-to-s57.md`,
  the package READMEs

## Spec anchors
- IHO S-57 Ed 3.1 (Part 3 encoding, Appendix A object/attribute catalogue).
- Forward: IHO *S-57 to S-101 Conversion Guidance* (S-101PT6, draft 2021) and
  S-65 Annex B; inland: IEHG *S-57 ENC to S-401 Conversion Guidance*.
- Reverse: S-65 Annex C Ed 1.0.0, written against **S-101 Ed 2.0.0**.
- S-52 §3.1.7 for what CSCL means. There is no S-52 implementation here.

## Architecture in this repo
- Records are parsed by the external `EncDotNet.S57` package (on
  `EncDotNet.Iso8211`); its `S57DocumentWriter` encodes reverse output. S-57
  codes and List types come from `S57ObjectCatalogue.Iho` upstream — don't
  re-add local OBJL/ATTL tables (#1284).
- `EncDotNet.S100.Datasets.Nis` reads an Esri ArcGIS Maritime S-57 NIS and
  writes S-57 base cells (#1759). `INisSource` keeps the conversion
  independent of storage. `FileGdbNisSource` is a managed, read-only file
  geodatabase reader that needs no Esri software. `NisToS57Translator` turns
  one cell into an `S57Document` for `S57DocumentWriter`. See
  `docs/nis-to-s57.md` and the package README.
- **No S-57 rendering path.** `S57DatasetProcessor` translates in memory and
  hands the result to the S-101 (or S-401) Part 9A Lua pipeline. Its `Spec` is
  `S-57`; `PortrayalSpec` is the translation target. Falls back to S-101 if no
  S-401 catalogue is registered (`SelectTarget`).
- Detection: `.000` is shared by S-57, S-101, S-401. S-57 = DDR has a `DSPM`
  field (`S57Dataset.IsS57File`, `Iso8211RootInfo.HasDataSetParameterField`).
  `PRSP` 1 maritime, 10 inland (→ `S57TranslationTarget.S401`), 51–57 = NATO
  AML, claimed by the `Aml` registration and **never translated**
  (`AmlDatasetProcessor`, `Aml/`). Matchers must stay mutually exclusive.
- Forward mapping: `S57S101Mapping` built from compiled-in `DefaultRules.cs`
  (flat `F(OBJL, acronym, S101Code)`/`A(...)` rows plus redirects, per-feature
  overrides, enum remaps) and `InlandRules.cs`, `S401*Rules.cs`.
  `ForSpec("S-401")` restricts to what the S-401 FC defines. Structural logic
  (bridges, C_AGGR, sector lights, topmarks, hazard depths) lives in
  `S57ToS101Translator*.cs`, `S57HazardDepths.cs`.
- FC gating: `S101FeatureAttributeBindings` / `S101AllowedEnumValues` read the
  bundled target catalogue; attributes not bound by the resolved class and
  enum values not permitted are dropped and counted.
- Reverse: `S101ToS57Translator` (+ `.Structure`, `.Topology`),
  `S101ToS57Rules` (inverse of forward) + `S101ToS57AnnexCRules` override layer
  (`Redirects`, `Companions`, `InformText`), `S101ToS57Coverage.Clauses`
  (test keeps the README table in sync), `S57EdgeGraph` (NTS noding),
  `S57TopologyCheck`, `S101ToS57LexicalLevels`.
- `S101ToS57AnnexCClauses.cs` is **auto-generated** by `tools/annexc-clauses`
  from the Annex C PDF — regenerate, never hand-edit.
- Updates: `S57UpdateApplicator`; reports via `S57Dataset.UpdateReport` /
  `S57DatasetProcessor.UpdateReport`.
- Validation: `Validation/S57PreTranslationRules.cs` on the raw cell, then the
  S-101 pack on the translation (rule ids prefixed `S101-as-S57/`; skipped for
  S-401). Exchange sets: `S57ExchangeSetCatalog`, `S57ExchangeSetVerification`.

## Review checklist
1. New forward mappings go in the rule tables and must still pass the FC
   gate; a high count of FC-rejected values means a wrong value map, not a
   missing S-101 value. Check `S57TranslationDiagnostics` counters.
2. Any forward change must keep the reverse inverse consistent; run the
   round trip (`RoundTrip/S57RoundTripCorpusTests`, opt-in via
   `ENCDOTNET_S57_CORPUS`, `_LIMIT`, `ENCDOTNET_ROUNDTRIP_REPORT`). Target is
   0 reverse bugs and only triaged "unexplained" diffs. A symmetric bug
   (same wrong code both ways) is invisible to the round trip.
3. Derived features (bridge spans) keep agency+FIDN and take the next free
   FIDS (wrapping 65535→0); the primary keeps the S-57 FOID. Reverse: one
   object per feature keeps the FOID, extras take the next subdivision.
4. Unmapped object classes become `S57UnknownObject` ("?" QUESMRK1), not
   drops; a class dropped on purpose must be a rule drop. Adding a rule can
   change `S64SmokeTests`' expected "?" lists (S-64 data only on CI) and
   golden images in `tests/EncDotNet.S100.VisualRegression.Tests/S57RenderingTests.cs`.
5. Update sequencing: same edition (`EDTN`), `UPDN` follows on (already-held
   skipped, gap stops), record `RVER` = update−1 for modify/delete, edition 0
   cancels. First failure stops; earlier updates stay; the cell always opens.
   Translation target is chosen *after* updates apply.
6. Reverse output is a base cell only (every record Insert, version 1); no
   S-57 update cells are written; not S-58 checked and not for navigation.

## Known pitfalls
- **SCAMIN / display window**: S-57 has no minimum display scale. The cell's
  cut-off is `ResolveCellMinimumDisplayScale` = max(CSCL, largest feature
  SCAMIN); CSCL alone (`ResolveCompilationScale`) only ranks overlapping cells.
  Using CSCL as the cut-off hides USACE inland cells (CSCL 1:5,000, SCAMIN up
  to 1:300,000). Feature `SCAMIN` → `scaleMinimum` (also on soundings).
- **Text and charsets**: `NINFOM`/`NOBJNM`/`NPLDST`/`NTXTDS` live in `NATF`
  (`NationalAttributes`), not ATTF. National text gets `language = "und"`
  (empty would read as English and merge back into INFORM). `OBJNAM` →
  `featureName` nameUsage 1, `NOBJNM` → nameUsage 2. Reverse: ATTF is level 0/1
  (ASCII/ISO 8859-1), NATF up to 2 (UCS-2, BMP only); out-of-Latin-1 ATTF text
  moves to its national counterpart, else `UnencodableText`.
- S-101 textual info: INFORM/TXTDSC go to a `NauticalInformation` information
  type via `AdditionalInformation`; S-401 keeps them inline as `information`.
- Area rings are chained by shared nodes, not `FSPT` order or `USAG` — grouping
  interiors by USAG merges holes and draws spikes.
- List enums (`COLOUR = "3,1"`) are split into separate occurrences before
  validation. Reverse: List attribute → comma list; Enumerated → first value only.
- Lossy by design: `M_COVR CATCOV=2` dropped; `M_CSCL/M_HDAT/M_HOPA/M_PROD/M_UNIT`
  dropped; `C_ASSO` and non-range/non-bridge `C_AGGR` unmapped; bridges need
  `VERCLR`/`VERCCL` to get a span; `VALLMA` converts minutes → degrees (÷60,
  full precision so reverse ×60 is exact). Reverse drops time/URI/URN, Text
  Placement, spatial quality (`QUAPOS`, `POSACC`/`SOUACC`), and S-101 Ed 1.0.0
  class names (`BuoyLateral` etc.) as unmapped.
- Hazard depths (`surroundingDepth`, `defaultClearanceDepth`) are added at
  translation time because S-101 rules read attributes, not S-52 CSPs.
- Picks keep S-57 acronyms; the chart reference maps them via
  `Pipelines/Reference/S57Aliases.cs` (ambiguous splits like `LIGHTS` map to nothing).
