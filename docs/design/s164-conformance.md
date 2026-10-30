# S-164 conformance — Design Note

> Status: **Assessment** (slices 1–4 of
> [#1120](https://github.com/philliphoff/EncDotNet.S100/issues/1120)). Most
> statuses come from reading the code. Slice 2 added the test harness and a
> smoke test over every v2.0.0 exchange set (§6.1); slices 3 and 4 run Part 2
> and Part 3 tests against the data, and those rows say **Passes** or
> **Fails**.

SoundCharts isn't an ECDIS and won't go through type approval. The IHO's
S-164 test datasets are still the most complete public description of what
an S-100 ECDIS has to do with S-100 data, and most of that applies to a
chart viewer too. This note lists every S-164 test, rates how far the
library, SoundCharts and `s100` support it today, and sets out the order in
which #1120 turns those ratings into tests that run.

## 1. Which S-164

| Item | Version | Notes |
|---|---|---|
| Instruction manual, published | Edition 1.0.0, March 2023 | On iho.int. Written against S-100 5.0.0 and S-98 1.0.0. |
| Instruction manual, current draft | Edition 2.0.1, July 2026 (draft) | Split into Parts 1–9 in the [S-164 sub-group repository](https://github.com/iho-ohi/S-164-Sub-Group) under `manual/docs/2.0.1`. Normative references: S-100 5.2.1, S-98 3.0, IEC 61174 and IEC 63173-2 (SECOM). |
| Test data | Exchange sets v2.0.0, 16 October 2026 | 39 signed exchange sets on the repository's releases page, built for S-101 FC 2.0.0. They cover Part 2 loading and updating, the non-ENC overlays, display categories, Chart 1 and the Part 5–7 route checks. There's no encrypted, dual-fuel or polar data yet. |

The matrix uses the 2.0.1 test names. Several 2.0.1 tests are still marked
TODO or TBD in the draft, and many name exchange sets that v2.0.0 doesn't
include yet; the **v2.0.0 data** column shows which do.

**Copyright and licensing.** The manual is IHO copyright. This note gives
only test names and references and describes each test in our own words.
The data repository has no licence file, so #1120 fetches the exchange sets
at test time rather than committing them. `tests/datasets/S101` already
holds 18 S-101 cells from an earlier S-164 release.

## 2. Status key

| Status | Meaning |
|---|---|
| Passes | A test in `tests/EncDotNet.S100.S164.Tests` runs it against the v2.0.0 data and it passes. The note says where the apps still fall short. |
| Fails | A test runs it against the v2.0.0 data and it fails. The row links the issue; the test is skipped until that's fixed. |
| Supported | The code does what the test checks. Not yet verified against S-164 data. |
| Partial | Some of it is done; the note says what's missing. |
| Gap | Not done, and worth doing in a viewer. |
| N/A | An ECDIS-only requirement that doesn't fit a viewer (radar video, bridge display calibration, mariner chart corrections). |

Evidence gives one representative file. Line numbers are left out because
they drift.

## 3. Summary

| Part | Area | Tests | Passes | Fails | Supported | Partial | Gap | N/A |
|---|---|---|---|---|---|---|---|---|
| 2 | Catalogue and dataset loading and updating | 70 | 5 | 0 | 10 | 34 | 21 | 0 |
| 3 | Chart display | 61 | 7 | 0 | 27 | 21 | 5 | 1 |
| 4 | Chart related functions | 47 | 0 | 0 | 2 | 32 | 11 | 2 |
| 5 | Navigational hazards | 6 | 0 | 0 | 1 | 1 | 4 | 0 |
| 6 | Areas with special conditions | 5 | 0 | 0 | 2 | 1 | 2 | 0 |
| 7 | Safety contour | 6 | 0 | 0 | 1 | 3 | 2 | 0 |
| 8 | S-57 testing | 0 | — | — | — | — | — | — |
| 9 | Dual fuel mode and SECOM | 17 | 0 | 0 | 2 | 9 | 6 | 0 |
| | **Total** | **212** | **12** | **0** | **45** | **101** | **51** | **3** |

Part 8 has no tests of its own in 2.0.1. It points S-57 testing at the
existing S-64 datasets, which a later slice covers.

## 4. Cross-cutting findings

These account for most of the Partial and Gap ratings. Each has its own
issue.

1. **Portrayal instructions that are skipped.** ([#1126](https://github.com/philliphoff/EncDotNet.S100/issues/1126)) `DrawingInstructionParser`
   recognised `SpatialReference`, `AreaPlacement`, `Date`, `TimeValid` and
   `AlertReference` but did nothing with them, and treats
   `LineInstructionUnsuppressed` as a plain line. `VectorSceneBuilder` sorts
   by geometry type before drawing priority, where S-100 Part 9 puts
   priority first. Edge-specific lines (the safety contour on depth areas,
   area borders), line suppression, date-dependent display and area-symbol
   placement all follow from this. In the S-164 DisplayBase cell the
   safety contour was drawn round each adjoining depth area's whole outline,
   coastline and cell edges included. `SpatialReference` is now honoured for
   lines, so the safety contour and other edge lines follow their edges. The
   time commands (`Date`, `Time`, `DateTime`, `TimeValid`, `ClearTime`) now
   set each instruction's time intervals, and S-101 and S-401 draw only what
   is valid at the view time. Line suppression, priority order and
   `AreaPlacement` remain. Part 3 tests affected: SafetyContourDisplay,
   AreaBorders, ComplexPortrayal, DifferentPriority, LineSuppression,
   FixedDateRange, PeriodicDateRange, CentredSymbols1.
2. **Route checks use their own rules, not the S-101 alert catalogue.** ([#1127](https://github.com/philliphoff/EncDotNet.S100/issues/1127)) The
   portrayal catalogue reader finds the alert catalogue but nothing loads
   it, and `AlertReference` is skipped (finding 1). The checks cover wrecks,
   obstructions and rocks, and eight area kinds plus S-122, so other
   NavHazard and ProhAre features aren't checked. Affects Parts 5–7 and 9.
3. **Protected data stops at the library.** ([#1128](https://github.com/philliphoff/EncDotNet.S100/issues/1128)) Part 15 decryption, permits and
   signature checks work in the library, but SoundCharts and `s100` don't
   open protected datasets. The viewer verifies exchange sets with
   `AllowUntrustedCertificates = true`, shows the result as a badge and
   never refuses data. Authorities added in Keys & certificates are used
   only for SECOM. Since slice 3 the library checks `CATALOG.SIGN`, and a
   catalogue that fails it shows as an invalid signature. Affects most of
   §2.6–2.7.
4. **No installed-data store.** ([#1129](https://github.com/philliphoff/EncDotNet.S100/issues/1129)) The Library indexes sources where they are.
   S-101 updates apply only when they sit beside their base cell. Since
   slice 3 the update applicator rejects an update for another edition as
   well as a break in the sequence. Re-issues,
   cancellations and later update sets don't change what was loaded before.
   Affects §2.2–2.5 and the S-128 status reports.
5. **Spherical geodesy.** ([#1130](https://github.com/philliphoff/EncDotNet.S100/issues/1130)) The measure tool, route legs and
   `GeodesicHelper` use a sphere. The S-164 accuracy tests ask for metre
   and 0.001° agreement on WGS-84, so all 15 need an ellipsoidal (Karney)
   implementation. The measure tool is rhumb line only, and there's no
   tool that turns a range and bearing into a position.
6. **No route monitoring.** ([#1131](https://github.com/philliphoff/EncDotNet.S100/issues/1131)) A steerable simulated own ship exists, but
   nothing looks ahead from it for hazards, areas or the safety contour.
   The 12 monitoring-mode tests are rated Gap rather than N/A because the
   own-ship pieces are already there.
7. **Water level adjustment is point-only.** ([#1132](https://github.com/philliphoff/EncDotNet.S100/issues/1132)) S-104 is applied at a picked
   point, over time at a point, and in route clearance. It isn't applied to
   the chart: no adjusted depths or safety contour, no adjustment boundary,
   and no permanent indication.
8. **Chart furniture.** ([#1133](https://github.com/philliphoff/EncDotNet.S100/issues/1133)) There's no S-52-style north arrow or vertical scale
   bar, no latitude bar, no "no data" indication, no chart legend for a
   position, no check symbol, no colour differentiation diagram and no
   scale-boundary line. Each is small. Affects Parts 3 and 4.

9. **Chart text hidden by the display category.** ([#1140](https://github.com/philliphoff/EncDotNet.S100/issues/1140), fixed) S-101 text has its own
   viewing groups in the *Chart text* layers, which no display mode lists,
   and the viewing-group filter hid everything outside the active mode, so
   every label was dropped whenever an ECDIS category was set. The *Chart
   text* groups are now independent of the display mode: they show unless
   the mariner's text switches hide them.
10. **Complex line styles.** ([#1141](https://github.com/philliphoff/EncDotNet.S100/issues/1141), fixed) Line styles were read as pen and dash only,
    and ignored when the instruction also named a colour, so symbolised
    boundaries drew as plain lines and some dashes came out solid. Named
    styles now win, with their interval, dashes, symbols along the line and
    composite parts, and simple dashes repeat over their interval.

## 5. Deliberate differences

These are places where the viewer departs from S-164 on purpose. #1120
doesn't try to close them.

- **Unauthenticated data.** S-164 expects an ECDIS to refuse data the
  scheme administrator hasn't signed. SoundCharts loads it and shows the
  signature status, because much of the data people open in it has no
  scheme administrator at all. Once finding 3 is fixed, refusing could be
  an opt-in setting.
- **Sources stay where they are.** S-164 tests a system database that
  data is imported into. The Library references folders, ZIPs and feeds in
  place. Tests about the system database are rated against the Library.
- **Manual chart corrections, radar overlay and display calibration** are
  ECDIS-only and rated N/A.
- **Error codes.** S-164 checks for specific S-98 error codes (SSE nn) in
  messages. The viewer's messages describe the problem instead. Whether to
  add the codes is open; it isn't a conformance goal.

## 6. Slices

| Slice | Scope | Depends on |
|---|---|---|
| 1 | This note: the matrix, findings and differences. | — |
| 2 | **Done.** `tests/EncDotNet.S100.S164.Tests`: the v2.0.0 release pinned by SHA-256 in `s164-v2.0.0.json`, downloaded on demand into a cache, cached in CI, and skipped cleanly without it. A smoke test opens every exchange set, opens every dataset in it, and verifies every signature. | 1 |
| 3 | **Done for updates and catalogue signatures.** S-164 tests apply the update sets to the `X01SW` cell across exchange sets (SequentialUpdates, InvalidSequence, NewerEditionUpdate, OlderEditionUpdate) and check every `CATALOG.SIGN` (MissingCatalogueSignature, InvalidCatalogueSignature). The applicator now rejects updates for another edition, and the library checks `CATALOG.SIGN`. The rest of finding 3 (authorities for exchange sets, a refuse-untrusted setting, protected data) stays with #1128; cross-set updates in the apps stay with #1129. | 2 |
| 4 | **Done.** S-164 tests portray every cell in the display sets and Chart 1 at the tests' mariner settings: no portrayal rule fails, and DBASE, STNDR and OTHER features fall in their display categories. The safety contour (#1126) and chart text (#1140) tests failed at first and pass since those fixes; symbolised boundaries (#1141) were found from headless renders compared with the reference plots by eye. | 2 |
| 5 | Part 4 tests: pick report checks, and Accuracy1–15 as unit tests once ellipsoidal geodesy (finding 5) lands. | 2 |
| 6 | Parts 5–7 tests: route checks over the NavigationalHazards, SpecialConditions and SafetyContour exchange sets, compared with the expected findings. Dual-fuel tests follow when that data is released. | 2 |
| 7 | S-64 for the S-57 path (Part 8). | 2 |

Slices 3–7 are independent of each other once slice 2 lands. Gaps found in
any slice become their own issues, and this matrix is updated as tests start
running: a test moves to **Passes** or **Fails** once it runs against S-164
data.

### 6.1 What the test data showed

Opening the 39 v2.0.0 exchange sets turned up four problems before any S-164
test ran. Slice 2 fixes the first three in the library:

1. **`S100_ROOT` packaging.** Every S-164 archive holds its exchange set
   under `S100_ROOT/`, as S-100 Part 17 lays it out, and `S100ExchangeSet`
   only looked for `CATALOG.XML` at the root. It now opens a folder or ZIP
   whose catalogue is at `S100_ROOT/CATALOG.XML`. SoundCharts and the Library
   use the same rule (`ExchangeSetRoot`) since
   [#1136](https://github.com/philliphoff/EncDotNet.S100/issues/1136), so the
   S-164 ZIPs open there too.
2. **Long-form product identifiers.** The catalogues declare
   `INT.IHO.S-101.2.0.0` with `number` 214 (the IHO register number), so
   nothing mapped to S-101. Base cells were still found by content
   sniffing, but update files weren't. `MapProductIdentifierToSpec` now
   reads the long form.
3. **Zipped catalogues.** The portrayal catalogues are delivered as ZIP
   packages with `compressionFlag` set and signed as `unencrypted` over the
   package bytes. Part 15 compression holds exactly one file, so the
   verifier now checks a multi-file package against its stored bytes.
4. **Unpublished scheme administrator.** The v2.0.0 data producer
   certificate is issued by `urn:mrn:iho:00AA:1810`, and the S-164
   repository publishes no certificate with that name. The smoke test
   checks each signature against the certificate the catalogue carries but
   can't anchor the chain; slice 3 needs that certificate for the SA tests.

Ten exchange sets (`SequentialUpdate1`–`5`, `InvalidSequence001`–`003`,
`NewUpdate`, `OldUpdate`) hold only S-101 updates for cells in other sets.
The apps can't apply them until #1129 adds cross-set updates; slice 3's
tests apply them in the library.

Slice 3 found one more problem, in the S-101 reader:

5. **S-101 2.0 field layouts.** The v2.0.0 cells describe DSID, FASC and
   INAS with a fixed part followed by a repeating group (`DSED\\*DSTC`,
   `FAUI\\*NATC…`): one association per FASC or INAS field, with its
   attributes repeated after it. The reader expected the association itself
   to repeat, so it dropped every feature and information association in
   these cells (77 and 20 in `X01SW`), and it didn't find the edition. It
   now reads both layouts.

### 6.2 What the display tests showed

The Part 3 reference plots can't be committed, so slice 4 checks the portrayal
output and compares headless renders with the plots by eye, locally:

- No S-101 rule fails in DisplayBase, DisplayStandard, DisplayOther (both
  feature catalogue editions), Chart 1 (14 cells) or Settings. The one
  question mark is Chart 1's own legend entry for it.
- At the tests' settings (10 m safety contour and safety depth), the DBASE
  dangers draw as isolated dangers in Display Base, STNDR draws only in
  Standard Display, and OTHER's features, apart from the background areas
  and depth-dependent dangers, are Other information only. At SoundCharts'
  default 30 m safety contour the same dangers are shallow-water dangers
  instead, as the portrayal catalogue intends.
- Three things differed from the plots, all since fixed: the safety contour
  outline (#1126), missing chart text (#1140) and plain boundaries (#1141).
- FixedDateRange and PeriodicDateRange use the Settings cell `X0001`: two
  buoys that replace each other on 16.08.2012, a traffic separation scheme
  that moves on 01.12.2013, and two buoys laid March–October and
  October–December. All were drawn at every date until #1126.
- The date-dependent highlight (`CHDATD01`) also carries viewing group
  90022, which is off at the tests' display settings, so the tests don't
  check it; the manual turns *Highlight date dependent* on.
- Dock and lock areas in DBASE belong to Standard Display, so with Display
  Base selected they leave an unfilled hole in the land area; the plot shows
  them filled. Not filed: the plot alone doesn't settle which is right.

## 7. Matrix

### Part 2 — Catalogue and dataset loading and updating

| Test | Reference | v2.0.0 data | Status | Evidence | Note |
|---|---|---|---|---|---|
| InitialCatalogues | S-98: 20.1 | InitialCatalogues | Partial | `src/EncDotNet.S100.Viewer/Services/FeatureCatalogueOverrides.cs` | Bundled catalogues plus one persisted override per product; nothing installs catalogues from an exchange set. |
| OutOfCatalogueSequence Dataset | S-98: 20.1 and B-1 | InitialCatalogues, OutOfCatalogueSequenceDataset | Partial | `src/EncDotNet.S100.Core/DatasetPipelines/IDatasetProcessor.cs` | Version mismatch is a warning badge; the dataset still loads. |
| UpdateCatalogues | S-98: 20.1 and B-1 | InitialCatalogues, PowerUpCatalogues, UpdatedCatalogueData | Partial | `src/EncDotNet.S100.Viewer/ViewModels/CatalogueViewModels.cs` | Overrides can be replaced by hand; no list of installed versions and no install from exchange sets. |
| MultipleRevisions | S-98: 20.1 and B-1 | MultipleRevisions | Partial | `src/EncDotNet.S100.Datasets.S101/S101LegacyFeatureNames.cs` | The library supports a catalogue per layer; the viewer uses one per product. 1.x names map onto the 2.0.0 portrayal. |
| InitialPowerUp | S-98: 6.1.1; IEC 61174: 4.4.1 | InitialPowerUp | Supported | `src/EncDotNet.S100.ExchangeSets/ExchangeSetRoot.cs` | The set opens in SoundCharts and the Library (`S100_ROOT` layout, §6.1), and own ship can be placed at a fixed position. |
| PowerUp | S-98: 6.1.1; IEC 61174: 4.4.1 | PowerUp | Partial | `src/EncDotNet.S100.Collections/Library/LibraryItemText.cs` | Edition, update and dates show per Library item; no table of everything installed. |
| AdditionalCell | IEC 61174: 4.4.1 | Settings | Supported | `src/EncDotNet.S100.Viewer/ViewModels/LibraryPanelViewModel.cs` | Adding a folder or ZIP indexes it and draws its coverage. |
| RemoveCell | S-98: 6.1.1; IEC 61174: 4.4.1 | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/LibraryPanelViewModel.cs` | Sources and loaded datasets can be removed, not single cells. |
| SequentialUpdates | S-98: 20.4.3; IEC 61174: 4.4.2 | PowerUp, SequentialUpdate1 | Passes | `tests/EncDotNet.S100.S164.Tests/Part2UpdateTests.cs` | The library applies SequentialUpdate1–5 to the PowerUp cell. The apps apply updates only within one exchange set (#1129), and there's no update review. |
| InvalidSequence | S-98: 20.4.3; IEC 61174: 4.4.2 | InvalidSequence001–003 | Passes | `tests/EncDotNet.S100.S164.Tests/Part2UpdateTests.cs` | The library stops after update 2 and reports the missing update 3. The apps show a toast, with no lasting indication. |
| NewerEditionUpdate | IEC 61174: 6.8.16.1 | GoodBaseCells, NewUpdate | Passes | `tests/EncDotNet.S100.S164.Tests/Part2UpdateTests.cs` | An update for a newer edition than the cell is rejected with a message; it applies to that edition. |
| UpdateInformation | S-98 20.4.3; IEC 61174 4.4.2 | — | Supported | `src/EncDotNet.S100.Specifications/content/S101/pc/Rules/UpdateInformation.lua` | Drawn by the S-101 PC and shown in the pick report. |
| OlderEditionUpdate | S-98: 20.4.3; IEC 61174: 4.4.2 | OldUpdate | Passes | `tests/EncDotNet.S100.S164.Tests/Part2UpdateTests.cs` | An update for an older edition is rejected with a message. |
| ReIssueAndUpdate | S-98: 20.5; IEC 61174: 4.4.2 | — | Partial | `src/EncDotNet.S100.Datasets.Pipelines/S101ExchangeSetUpdatePlan.cs` | A re-issue loads with in-set updates; later sets don't build on it. |
| UpdateRejection | S-98: 20.4.3; IEC 61174: 4.4.2 | — | Gap | — | Needs manual updates. |
| ManualUpdateCreation | S-98: 20.4.4; IEC 61174 6.8.17 | PowerUp | Gap | — | No manual-update editor. |
| ManualUpdatesDates | S-98 20.4.4; IEC 61174: 6.8.17 | — | Gap | — | Needs manual updates. |
| ManualUpdatesRetrieval | S-98: 20.4.4; IEC 61174: 6.8.17 | — | Gap | — | Needs manual updates. |
| ManualUpdatesAlerts | S-98: 20.4.4; IEC 61174: 6.8.17 | — | Gap | — | Needs manual updates. |
| InvalidPermit | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/PermitFile.cs` | Library rejects a malformed permit file; the apps don't open protected data. |
| IncorrectPermitFormat | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/PermitFile.cs` | One bad entry fails the whole file. Library only. |
| InvalidPermitChecksum | S-98: B-3 | — | Gap | `src/EncDotNet.S100.ExchangeSets/Protection/UserPermit.cs` | Only user permits have a CRC check. |
| MissingPermitSignature | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/PermitSignatureVerifier.cs` | Library reports NotSigned. Library only. |
| MultiplePermits | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/PermitFile.cs` | Several permits parse; the first match wins. |
| InvalidPermitSignature | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/PermitSignatureVerifier.cs` | Library rejects bad signatures. Library only. |
| ExpiringPermit | S-98: B-3 | — | Gap | `src/EncDotNet.S100.ExchangeSets/Protection/DataPermit.cs` | No 30-day permit expiry warning. |
| InvalidPermitSignature | S-98: B-3 | — | Partial | `docs/protected-exchange-sets.md` | A wrong hardware ID only fails when the dataset is decrypted. |
| ExpiredPermits | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/PermitKeyProvider.cs` | Datasets issued after expiry are refused; no expiry warning. |
| PermitInstallation | S-98: 15-7.4 | — | Gap | `src/EncDotNet.S100.Viewer/ViewModels/Keys/KeysAndCertificatesViewModel.cs` | No permit install UI; the System IDs tab is a placeholder. |
| MultipleDataServers | S-98: B-3 | — | Gap | `src/EncDotNet.S100.ExchangeSets/Protection/PermitHeader.cs` | Data server is parsed; no per-server permit store. |
| PermitManagement | S-98: 15-7.4 | — | Gap | — | No permit list or removal. |
| InstallSACertificate | S-98: B-3 | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/Keys/AddAuthorityDialogViewModel.cs` | Authorities can be added but only SECOM uses them. |
| MissingSACertificate | S-98: B-3 | — | Partial | `docs/protected-exchange-sets.md` | The library needs a trusted root for permits; the viewer doesn't for exchange sets. |
| CertificateExpiry | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/ExchangeSetVerifier.cs` | Library reports an expired certificate; the viewer shows a badge only. |
| MissingCatalogueSignature | S-98: B-3 | — | Partial | `tests/EncDotNet.S100.S164.Tests/Part2CatalogueSignatureTests.cs` | The library reports a missing `CATALOG.SIGN`; the apps don't show it, and the set loads (§5). |
| InvalidCatalogueSignature | S-98: B-3 | — | Passes | `tests/EncDotNet.S100.S164.Tests/Part2CatalogueSignatureTests.cs` | A catalogue that doesn't match `CATALOG.SIGN` shows as an invalid signature; the set still loads (§5). |
| NonSASignedData | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/ExchangeSetVerifier.cs` | Library rejects chains outside the roots; the viewer allows untrusted certificates. |
| AuthenticationDomainCoordinator | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/ExchangeSetVerifier.cs` | Intermediate chains build in the library; the viewer doesn't anchor them to the SA. |
| InvalidDatasetSignature | — | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/SignatureStatus.cs` | Shown as a badge; nothing is blocked. |
| ContinuousAuthentication | — | — | Supported | `src/EncDotNet.S100.ExchangeSets/ExchangeSetVerifier.cs` | Each file gets its own result. The test's cells are encrypted, which the apps can't open. |
| MultipleDataServers | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/ExchangeSetVerifier.cs` | Several signer certificates verify in the library; no decryption in the apps. |
| MissingCertificate | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/ExchangeSetVerifier.cs` | Library flags it per file; the viewer doesn't block. |
| ExpiredPermits | S-98: B-3 | — | Gap | — | No expiry warnings on import or display. |
| ExpiringPermits | S-98: B-3 | — | Gap | — | No 30-day warning. |
| IncorrectCellKeys | S-63: 10.7.3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/DatasetDecryptionException.cs` | Library fails per dataset and continues. Library only. |
| DataIntegrity | S-98: B-3 | — | Partial | `src/EncDotNet.S100.ExchangeSets/Protection/Part15SignatureContentResolver.cs` | Library verifies decrypted content. Library only. |
| DataManagement | S-98: B-3 | — | Gap | — | No per-server key store. |
| AdditionalPermits | S-98: B-3 | — | Gap | — | Permit files aren't merged. |
| ProviderChange | S-98: B-3 | — | Gap | — | No provider handling. |
| CancelledDatasets | — | — | Partial | `src/EncDotNet.S100.Collections/Model/CollectionItem.cs` | Cancelled status comes from feeds; edition-0 cancellations aren't handled at load. |
| Reissues | — | — | Partial | `src/EncDotNet.S100.Datasets.Pipelines/S101ExchangeSetUpdatePlan.cs` | A re-issue loads; it doesn't replace an earlier install. |
| ECDISManagement | — | — | Partial | `src/EncDotNet.S100.Datasets.Pipelines/S101ExchangeSetUpdatePlan.cs` | Orphan updates are skipped with a warning. |
| SupplementaryFiles | S-98: Appendix C | — | Partial | `src/EncDotNet.S100/S100ExchangeSet.cs` | Support files are read from the same set; replacements in later sets aren't kept. |
| Fileless Cancellation | S-98: 21.2 | — | Partial | `src/EncDotNet.S100.ExchangeSets/DatasetDiscoveryMetadata.cs` | Withdrawal records are excluded from loading; an already loaded dataset stays. |
| UpdateStatusReportENC | S-98: Appendix C | PowerUp | Partial | `src/EncDotNet.S100.Collections/Library/LibraryItemState.cs` | Downloads are flagged out of date against feeds; no S-98 Appendix C report. |
| UpdateStatusReportENP | S-98: Appendix C | — | Gap | — | No ENP status report. |
| MissingRevisionInformation | S-98, Appendix C | — | Gap | — | No "unknown" status without revision information. |
| MultipleRevisionInformation | S-98, Appendix C | — | Gap | — | S-128 datasets aren't merged. |
| RevisionInformation | S-98, Appendix C | — | Gap | — | No per-product up-to-date logic. |
| RevisionUpdate | S-98, Appendix C | — | Gap | `src/EncDotNet.S100.Datasets.S128` | S-128 updates aren't applied. |
| RevisionCompleteness | S-98, Appendix C | — | Gap | — | No S-124 bulletin vs S-128 cross-check. |
| S-102 | S-98 6.1; S-98 13 | InitialCatalogues, S102 | Partial | `src/EncDotNet.S100.Datasets.S102/S102PortrayalCatalogue.cs` | Shading follows the safety contour and S-101 depth areas are suppressed; no enhanced safety contour or coverage border. |
| Water Level Adjustment (S-104) | S-98 Appendix D-2 | EnhancedSafetyContour, InitialCatalogues, WLAOverlap | Partial | `src/EncDotNet.S100.Viewer/Services/Depth/DepthAssimilationService.cs` | S-104 overlay and tide-adjusted depth at a point; no chart-wide adjustment. |
| S-111 | S-98 6.1; S-98 13 | InitialCatalogues, S111 | Supported | `src/EncDotNet.S100.Datasets.Pipelines/S111DatasetProcessor.cs` | Reader and portrayal. |
| Marine Protected Areas | S-98 6.1.1 | — | Supported | `src/EncDotNet.S100.Datasets.Pipelines/S122DatasetProcessor.cs` | Reader and portrayal. |
| Navigational Warnings | S-98 11.12.1 S-98 11.2.2 | S124NAVARNSelection | Supported | `src/EncDotNet.S100.Datasets.Pipelines/S124DatasetProcessor.cs` | Reader, portrayal and warnings panel. |
| S-124 Detail 2 | S-98 11.12.1 | S124MessageManagement, S124NewNAVWARN | Partial | `src/EncDotNet.S100.Viewer/ViewModels/NavigationalWarningsViewModel.cs` | No sort by installed time or warning type. |
| Marine Traffic Management | S-98 6.1.1 | — | Supported | `src/EncDotNet.S100.Datasets.Pipelines/S127DatasetProcessor.cs` | Reader and portrayal. |
| UnderKeelClearance | S-98 6.1.1 | — | Supported | `src/EncDotNet.S100.Datasets.Pipelines/S129DatasetProcessor.cs` | Reader and portrayal. |
| Marine Harbour Infrastructure | S-98 6.1.1 | — | Supported | `src/EncDotNet.S100.Datasets.Pipelines/S131DatasetProcessor.cs` | Reader and portrayal; PC edition not confirmed. |

### Part 3 — Chart display

| Test | Reference | v2.0.0 data | Status | Evidence | Note |
|---|---|---|---|---|---|
| Dataset Selection | S-98 Appendix E | — | Partial | `src/EncDotNet.S100.Renderers.Mapsui/OverlapSuppression.cs` | Cells ranked by minimum display scale, finer clip coarser; not the S-98 Appendix E selection. The S-164 test is still TODO. |
| DatasetRendering | S-98 Appendix E | — | Partial | `src/EncDotNet.S100.Renderers.Mapsui/CoverageClip.cs` | Coarser cells clipped to finer coverage; not the Appendix E algorithm. The S-164 test is still TODO. |
| DisplayBase | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | DisplayBase | Passes | `tests/EncDotNet.S100.S164.Tests/Part3DisplayTests.cs` | No rule fails, the dangers are Display Base at the test settings, and the safety contour follows its edges ([#1126](https://github.com/philliphoff/EncDotNet.S100/issues/1126)). Dock and lock areas leave holes in Display Base (§6.2). |
| DisplayStandard | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | DisplayStandard | Passes | `tests/EncDotNet.S100.S164.Tests/Part3DisplayTests.cs` | Every feature draws in Standard Display, with symbolised boundaries ([#1141](https://github.com/philliphoff/EncDotNet.S100/issues/1141)). |
| DisplayOther | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | DisplayOther | Passes | `tests/EncDotNet.S100.S164.Tests/Part3DisplayTests.cs` | Features draw as Other information, with their text and line styles. |
| ViewingGroupsStandard | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | DisplayStandard | Partial | `src/EncDotNet.S100.Viewer/Services/EcdisDisplayState.cs` | Viewing groups toggle individually; the PC's named viewing-group layers aren't the UI. |
| ViewingGroupsOther | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | DisplayOther | Partial | `src/EncDotNet.S100.Viewer/Services/EcdisDisplayState.cs` | As above. |
| TextGrouping | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | DisplayBase | Passes | `tests/EncDotNet.S100.S164.Tests/Part3DisplayTests.cs` | Chart text shows in every display category; Important / other text toggles hide it ([#1140](https://github.com/philliphoff/EncDotNet.S100/issues/1140)). |
| InvalidFeaturesA | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Specifications/content/S101/pc/Rules/main.lua` | Unknown classes fall back to the PC default rule. |
| InvalidFeaturesB | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Specifications/content/S101/pc/Rules/main.lua` | PC rules; not compared with the reference plot. |
| InvalidFeaturesPickA | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Viewer/Services/PickService.cs` | Pick report from drawn hits; unknown names fall back to codes. |
| InvalidFeaturesPickB | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Viewer/Services/PickService.cs` | Feature without attributes still reports class and position. |
| InvalidFeaturesPickC | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Viewer/Services/PickService.cs` | Unknown attributes show by code. |
| InvalidFeaturesPickD | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Datasets.Pipelines/Spec/S101FeatureDescriber.cs` | Attributes decoded through the feature catalogue. |
| SimplifiedSymbolsFalse | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Core/Pipelines/MarinerSettings.cs` | SimplifiedSymbols context parameter. |
| SimplifiedSymbolsTrue | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Core/Pipelines/MarinerSettings.cs` | As above. |
| PlainBoundaries | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Core/Pipelines/MarinerSettings.cs` | PlainBoundaries context parameter. |
| Symbolisedboundaries | S-98 6.1.1; S-100 Part 9; S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Rendering.Scene/VectorSceneBuilder.cs` | Complex line styles draw their dashes and symbols along the boundary ([#1141](https://github.com/philliphoff/EncDotNet.S100/issues/1141)). |
| FixedDateRange | S-98 12.8.1 | Settings | Passes | `tests/EncDotNet.S100.S164.Tests/Part3DisplayTests.cs` | Features draw only within their dates at the view time, read in the Local/UTC setting, and a pinned time says "Display Not Real Time" ([#1126](https://github.com/philliphoff/EncDotNet.S100/issues/1126)). The library takes a viewing range; the apps show one view time, and the Timeline only moves while time-aware data is loaded. |
| PeriodicDateRange | S-98 12.8.1 | Settings | Passes | `tests/EncDotNet.S100.S164.Tests/Part3DisplayTests.cs` | Truncated dates (`----03--` to `----10--`) recur every year, wrapping over the year end. The manual names DisplayOther, but the dated buoys are in the Settings cell. As above for the view time. |
| SafetyContourDisplay | S-98 12.6.1 | DisplayBase | Passes | `tests/EncDotNet.S100.S164.Tests/Part3DisplayTests.cs` | The safety contour is drawn only on the depth-area edges at the safety depth ([#1126](https://github.com/philliphoff/EncDotNet.S100/issues/1126)). |
| UserSelectedSafetyContour | S-98 Appendix D-1 | PowerUp | Partial | `src/EncDotNet.S100.Datasets.S102/S102PortrayalCatalogue.cs` | S-102 coloured by the user's safety contour; no S-104 adjustment of the contour. |
| SafetyDepth | — | — | Supported | `src/EncDotNet.S100.Datasets.S101/S101LuaRuleExecutor.cs` | Passed to the PC. |
| ShallowPattern | S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Viewer/Services/EcdisDisplayState.cs` | PC pattern, switchable by viewing group. |
| ContourLabels | S-101 Portrayal | — | Supported | `src/EncDotNet.S100.Datasets.S101/S101LuaRuleExecutor.cs` | PC labels plus safety-contour label merging. |
| ColourPalettes1 | S-101 PC | — | Supported | `src/EncDotNet.S100.Core/Pipelines/ColorPalette.cs` | Day, dusk and night from the PC colour profiles. |
| AdditionalInformation1 | — | — | Supported | `src/EncDotNet.S100.Viewer/ViewModels/PickTextCardViewModel.cs` | Referenced text files show in the pick report. |
| ScaleMinimum | S-98 12.9 | — | Supported | `src/EncDotNet.S100.Renderers.Mapsui/TileScaleClass.cs` | SCAMIN per op and tile scale class, with an override. |
| FullLightLines | — | — | Supported | `src/EncDotNet.S100.Core/Pipelines/MarinerSettings.cs` | FullLightLines context parameter. |
| OtherLanguages | S-98 10.3 | — | Supported | `src/EncDotNet.S100.Datasets.S101/S101LuaDataProvider.cs` | NationalLanguage parameter selects chart text. |
| Language Selections | S-98 10.3 | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/SettingsViewModel.cs` | One language, not a ranked list; pick report not localised. |
| LanguagePacks | S-98 15.1 | InitialPowerUp | Gap | — | No language packs. |
| DifferentPriority | S-101PC | — | Partial | `src/EncDotNet.S100.Rendering.Scene/VectorSceneBuilder.cs` | Instructions sort by geometry type before drawing priority. |
| SamePriority | S-101PC | — | Supported | `src/EncDotNet.S100.Rendering.Scene/VectorSceneBuilder.cs` | Equal priority draws area, line, point, text. |
| LineSuppression | S-101PC | — | Gap | `src/EncDotNet.S100.Core/Pipelines/Vector/DrawingInstructionParser.cs` | LineInstructionUnsuppressed is treated as a plain line; no shared-edge suppression. |
| ManualUpdates | S-98 12.12.1 | — | N/A | — | Mariner chart corrections are ECDIS chart maintenance. |
| TextDisplay1 | — | — | Supported | `src/EncDotNet.S100.Renderers.Skia/Scene/LabelDeclutterer.cs` | Text placed and decluttered. |
| AreaBorders | S-101PC | — | Supported | `src/EncDotNet.S100.Core/Pipelines/Vector/DrawingInstructionParser.cs` | Plain, symbolised and edge-specific borders all draw. |
| UnknownSymbols | S-101PC | — | Partial | `src/EncDotNet.S100.Renderers.Mapsui/MapsuiDisplayListRenderer.cs` | A missing symbol draws as a dot, not the unknown-symbol glyph. |
| BoundaryDisplay1 | S-98 9.1.1 | — | Partial | `src/EncDotNet.S100.Renderers.Mapsui/S100DatasetExtentIndicatorLayer.cs` | Extents outlined only when out of scale. |
| BoundaryDisplay2 | S-98 12.3.1 | — | Gap | — | No scale-boundary line between areas of different scale. |
| OverscalePattern1 | S-98 12.3.2 | — | Supported | `src/EncDotNet.S100.Renderers.Mapsui/OverscaleCurtain.cs` | Overscale pattern over overscaled coverage. |
| ComplexPortrayal | S-101PC | — | Partial | `src/EncDotNet.S100.Rendering.Scene/VectorSceneBuilder.cs` | Affected by the SpatialReference and priority-order gaps. |
| CentredSymbols1 | S-101PC | Settings | Partial | `src/EncDotNet.S100.Rendering.Scene/VectorSceneBuilder.cs` | Fixed centroid; not re-centred in the visible part. AreaPlacement skipped. |
| OverscaleIndication1 | S-98 12.3 | PowerUp | Supported | `src/EncDotNet.S100.Renderers.Mapsui/OverscaleEvaluator.cs` | Status-bar overscale pill with factor. |
| LargerScaleData | S-98 12.3.3 | PowerUp | Partial | `src/EncDotNet.S100.Renderers.Mapsui/S100DatasetExtentIndicatorLayer.cs` | Outlines hint at larger-scale data; no explicit indication. |
| ScaleBoundary | S-98 12.3.1 | PowerUp | Partial | `src/EncDotNet.S100.Renderers.Mapsui/OverscaleCurtain.cs` | Overscale shown; scale-boundary line missing. |
| DifferentScale1 | S-98 Appendix E | PowerUp | Supported | `src/EncDotNet.S100.Renderers.Mapsui/CoverageClip.cs` | Coarser cell fills where no finer cell covers. |
| OverlappingData | S-98 20.3.1 | — | Gap | `src/EncDotNet.S100.Renderers.Mapsui/OverlapSuppression.cs` | Same-band overlaps both draw; no overlap indication. |
| GraphicalIndex | S-98 12.4.1 | PowerUp | Supported | `src/EncDotNet.S100.Viewer/Services/LibraryCoverageOverlayController.cs` | Library coverage overlay with edition and update. |
| DisplayScaleChange | S-98 12 | PowerUp | Partial | `src/EncDotNet.S100.Viewer/MapScaleFormatter.cs` | Zoom and scale readout; no chart-scale or range picker outside MCP. |
| WaterLevelAdjustment | S-98 Appendix D-2 | PowerUp | Partial | `src/EncDotNet.S100.Viewer/Services/Depth/DepthAssimilationService.cs` | S-102 replaces ENC depths; S-104 tide only at a picked point. |
| AdjustmentOfDepthValues | S-98 Appendix D-3.1 | — | Partial | `src/EncDotNet.S100.Viewer/Services/Depth/LocationDepthProbe.cs` | Point probe only. |
| WLAFeatureInformation | S-98 Appendix D-3 | — | Partial | `src/EncDotNet.S100.Viewer/Services/Depth/DepthAssimilationService.cs` | Depth card shows source and tide; not per feature. |
| UncertaintyWLA | S-98 Appendix D-2 | — | Partial | `src/EncDotNet.S100.Viewer/Services/Depth/DepthAssimilationService.cs` | S-102 uncertainty only; S-104 uncertainty not combined. |
| WLATimePeriod | S-98 Appendix D-2.5 | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/DepthOverTimeViewModel.cs` | Depth over time at a point; no period adjustment. |
| IncompatibleDatums | — | — | Partial | `src/EncDotNet.S100.Viewer/Services/Depth/DepthAssimilationService.cs` | Mismatched datums warn; data isn't rejected. |
| WLAPlanning1 | S-98 Appendix D-2.7 | — | Supported | `src/EncDotNet.S100.Navigation/ClearanceCheck.UkcModel.cs` | Route clearance applies S-104 at each ETA. |
| PolarData1 | — | — | Supported | `src/EncDotNet.S100.Rendering.Scene/WebMercator.cs` | Web Mercator to 85.05°. |
| HighLatitudeScaleBar | S-98 12.10.1 | — | Partial | `src/EncDotNet.S100.Viewer/Views/ScaleBarView.axaml.cs` | Scale bar corrected at view centre only. |
| PolarData2 | S-52 10.1.10.2 | — | Gap | `src/EncDotNet.S100.Rendering.Scene/WebMercator.cs` | Optional; no polar projection. |

### Part 4 — Chart related functions

| Test | Reference | v2.0.0 data | Status | Evidence | Note |
|---|---|---|---|---|---|
| ModeOrientation1 | — | PowerUp | Partial | `src/EncDotNet.S100.Viewer/Views/CompassRoseView.cs` | Compass rose shows north; not placed as the S-52 north arrow. |
| ModeOrientation2 | — | — | Gap | — | Own ship shown but the view never re-centres on it. |
| ModeOrientation3 | — | — | Partial | `src/EncDotNet.S100.Viewer/Services/MapInteractionController.cs` | Manual pan, zoom and rotate; own ship can't be offset on screen. |
| NoDataAvailable | — | — | Gap | — | No no-data indication or pattern. |
| NonNorthUp | — | — | Partial | `src/EncDotNet.S100.Rendering.Scene/RotatedViewport.cs` | Free rotation with correct symbol frames; no course-up or head-up. |
| ScaleBar | S-98 12.10.1 | PowerUp | Partial | `src/EncDotNet.S100.Viewer/Views/ScaleBarView.axaml` | Horizontal bar at top-centre, not the S-52 vertical bar. |
| LatitudeBar | S-98 12.10.1 | PowerUp | Gap | — | No latitude bar. |
| FeatureInformation1 | S-98 15.1 | PowerUp | Supported | `src/EncDotNet.S100.Viewer/Services/PickService.cs` | Click lists every feature under the cursor. |
| FeatureInformation2 | S-98 15.1 | — | Partial | `src/EncDotNet.S100.Core/DatasetPipelines/PickAttribute.cs` | Decoded names and values; S-98 ordering and explanations not checked. |
| FeatureInformation3 | S-98 15.2.2 | — | Gap | — | Pick report content isn't configurable. |
| FeatureInformation4 | S-98 15.1 | — | Gap | `src/EncDotNet.S100.Viewer/Tools/PickTool.cs` | Optional. No hover pick. |
| FeatureInformation5 | S-98 15.1 | — | Partial | `src/EncDotNet.S100.Core/DatasetPipelines/PickAttribute.cs` | Unknown attributes fall back to codes. |
| InteroperabilityIdentifer | 15.1 (10) | — | Gap | `src/EncDotNet.S100.Core/DatasetPipelines/NavigationalWarningInfo.cs` | Identifier read but not used to link products in the pick report. |
| TidalStreamPanelData | S-98 15.4 | PowerUp | Partial | `src/EncDotNet.S100.Viewer/ViewModels/PickReportViewModel.cs` | Generic nested attributes; no tidal-stream panel. |
| Tidal panels (2) | S-98 15.4 | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/PickReportViewModel.cs` | As above. |
| SupplementaryFile2 | S-98 15.2.2 | — | Supported | `src/EncDotNet.S100.Datasets.Pipelines/FeatureInfoBuilder.cs` | Text files shown; palette handling not checked. |
| PictorialRepresentation | S-98 15.2.2 | — | Gap | `src/EncDotNet.S100.Datasets.S57/DefaultRules.cs` | Only the file name shows. |
| RadarOverlay | S-98 9.2 | PowerUp | N/A | — | Needs radar video. |
| Accuracy1 | — | PowerUp | Partial | `src/EncDotNet.S100.Viewer/Geodesy/MarineGeodesy.cs` | Great circle via route legs only, on a sphere; misses metre tolerance. |
| Accuracy2 | — | — | Partial | `src/EncDotNet.S100.Viewer/Geodesy/MarineGeodesy.cs` | As Accuracy1. |
| Accuracy3 | — | — | Partial | `src/EncDotNet.S100.Viewer/Geodesy/MarineGeodesy.cs` | As Accuracy1. |
| Accuracy4 | — | — | Partial | `src/EncDotNet.S100.Core/Geodesy/GeodesicHelper.cs` | Spherical direct problem in the library; no UI. |
| Accuracy5 | — | — | Partial | `src/EncDotNet.S100.Core/Geodesy/GeodesicHelper.cs` | As Accuracy4. |
| Accuracy6 | — | — | Partial | `src/EncDotNet.S100.Core/Geodesy/GeodesicHelper.cs` | As Accuracy4. |
| Accuracy7 | — | PowerUp | Partial | `src/EncDotNet.S100.Viewer/Tools/MeasurePathState.cs` | Measure tool is rhumb line on a sphere. |
| Accuracy8 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/MeasurePathState.cs` | As Accuracy7. |
| Accuracy9 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/MeasurePathState.cs` | As Accuracy7. |
| Accuracy10 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/RouteOverlayScene.cs` | Geodesic route legs draw as curves on a sphere; no geodesic circle. |
| Accuracy11 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/RouteOverlayScene.cs` | As Accuracy10. |
| Accuracy12 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/RouteOverlayScene.cs` | As Accuracy10. |
| Accuracy13 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/RouteOverlayScene.cs` | Rhumb legs drawn straight on spherical Web Mercator. |
| Accuracy14 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/RouteOverlayScene.cs` | As Accuracy13. |
| Accuracy15 | — | — | Partial | `src/EncDotNet.S100.Viewer/Tools/RouteOverlayScene.cs` | As Accuracy13. |
| SymbolSize | S-98 10.2.3 | PowerUp | Partial | `src/EncDotNet.S100.Renderers.Mapsui/MapsuiDisplayListRenderer.cs` | Nominal 0.32 mm pixel, not the real screen; SymbolScale can shrink symbols. |
| Chart1Symbols | S-98 19.6.1; S-98 10.2.2 | Chart1 | Gap | — | No check symbol or size calibration. |
| CheckSym | S-98 10.2.2 | — | Gap | — | No check symbol. |
| TextSize | S-52 [3.1.5] | PowerUp | Partial | `src/EncDotNet.S100.Viewer/ViewerSettings.cs` | PC sizes plus a scale setting; no minimum height. |
| Chart 1 | S-98 Chart 1 | — | Partial | `tests/datasets/S101/CATALOG.XML` | Chart 1 sets load like any other; nothing built in. |
| Redraw | S-98 19.1 | PowerUp | Partial | `docs/design/mapsui-performance.md` | Perf tooling exists; no 5-second redraw test. |
| UnitsLegend | S-98 9.1.5 | PowerUp | Gap | — | No chart legend for a position. |
| LegendItems | S-98 9.1.5 | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/LayerStackViewModel.cs` | Coverage colour legends; no cross-product legend. |
| ChartOne | S-52 19.6.1 | — | Partial | `src/EncDotNet.S100.ExchangeSets/ExchangeCatalogueReader.cs` | Not-for-navigation flagged; Chart 1 not built in. |
| ChartOne2 | S-52 19.6.1 | — | Partial | `src/EncDotNet.S100.Viewer/Services/PickService.cs` | Picking would work on a loaded Chart 1; not verified. |
| ColourDifferentiation | S-98 19.6.2 | — | Gap | `src/EncDotNet.S100.Viewer/PaletteTypeNameConverter.cs` | Palettes exist; no colour differentiation diagram. |
| ColourCalibration | S-98 19.3.5 | — | N/A | — | Bridge display calibration. |
| DataQualityPortrayal | S-98 12.10.10 | — | Partial | `src/EncDotNet.S100.Viewer/Services/EcdisDisplayDefaults.cs` | Quality patterns behind a viewing group, off by default. |
| ENC encoding | S-98 DC Annex; S-101PC; S-100 Part 10a Part 10b | — | Partial | `src/EncDotNet.S100.Datasets.S101/S101FeatureXmlSource.cs` | ISO 8211 reading works; edge cases not verified. |

### Part 5 — Navigational hazards

| Test | Reference | v2.0.0 data | Status | Evidence | Note |
|---|---|---|---|---|---|
| NavigationalHazards | S-98 12.10.6 | NavigationalHazards | Partial | `src/EncDotNet.S100.Navigation/ChartCheck.Dangers.cs` | Planning check covers wrecks, obstructions and rocks with its own rules, not the S-101 alert catalogue. |
| UserSelectionProhibited Areas | — | — | Gap | — | Area kinds are fixed in code. |
| NavigationalHazardsLS | S-98 12.10.6 | NavigationalHazards, NavigationalHazardsOverview | Supported | `src/EncDotNet.S100.Core/DataModel/CellScaleRanking.cs` | Dangers counted from the finest covering cell. |
| NavigationalHazardsMon | — | — | Gap | `src/EncDotNet.S100.Viewer/Services/DynamicSources/OwnShip/SteerableOwnShipPositionProvider.cs` | A simulated own ship exists; no monitoring look-ahead or alarm. |
| NavigationalHazardsMonLS | — | NavigationalHazards, NavigationalHazardsOverview | Gap | — | No monitoring mode. |
| DataQuality | — | — | Gap | — | No quality-driven check. |

### Part 6 — Areas with special conditions

| Test | Reference | v2.0.0 data | Status | Evidence | Note |
|---|---|---|---|---|---|
| SpecialConditions | S-98 12.10.7 | SpecialConditions | Partial | `src/EncDotNet.S100.Navigation/AreaCheck.cs` | Eight S-101 area kinds plus S-122; many alert catalogue area types missing. |
| SpecialConditionsLS | S-98 12.10.7 | NavigationalHazardsOverview | Supported | `src/EncDotNet.S100.Navigation/AreaCheck.cs` | Finest-cell rule. |
| SpecialConditionsMon | S-98 12.10.7 | — | Gap | — | No monitoring mode. |
| SpecialConditionsMonLS | S-98 12.10.7 | — | Gap | — | No monitoring mode. |
| SafetyContour | S-98 12.10.9 | SafetyContour | Supported | `src/EncDotNet.S100.Navigation/ChartCheck.Depths.cs` | Crossing and in-corridor safety contour findings; own rules. |

### Part 7 — Safety contour

| Test | Reference | v2.0.0 data | Status | Evidence | Note |
|---|---|---|---|---|---|
| SafetyContourLS | S-98 12.10.9 | NavigationalHazardsOverview | Supported | `src/EncDotNet.S100.Navigation/ChartCheck.cs` | Depth areas from the finest cell. |
| SafetyContourNextLeg | S-98 12.10.9 | — | Partial | `src/EncDotNet.S100.Navigation/ChartCheck.cs` | All legs checked at planning; no next-leg rule. The S-164 test is TBD. |
| SafetyContourWLA | S-98 Appendix D-3 | — | Partial | `src/EncDotNet.S100.Navigation/ChartCheck.Depths.cs` | S-102 shoals and S-104 water level in clearance; no adjusted safety contour. |
| SafetyContourMon | S-98 12.10.9 | — | Gap | — | No monitoring mode. |
| SafetyContourMonLS | S-98 12.10.9 | — | Gap | — | No monitoring mode. |
| DualFuelSimple | S-98 18.2 | — | Partial | `src/EncDotNet.S100.Viewer/Services/ExchangeSetService.cs` | S-57 and S-100 sets load side by side; one set with both catalogues takes the S-57 path only. |

### Part 9 — Dual fuel mode and SECOM

| Test | Reference | v2.0.0 data | Status | Evidence | Note |
|---|---|---|---|---|---|
| DualFuelSimpleUpdate | S-98 18.2 | — | Partial | `src/EncDotNet.S100.Datasets.Pipelines/S101ExchangeSetUpdatePlan.cs` | Each product's updates apply; not as one dual-fuel set. |
| DualFuelFeatureInformation | S-98 18.2 | — | Supported | `src/EncDotNet.S100.Viewer/ViewModels/PickHit.cs` | One pick list across products, each hit tagged S-57 or S-101. |
| NavigationalHazardsDF | — | NavigationalHazards | Partial | `src/EncDotNet.S100.Datasets.Pipelines/Navigation/CatalogRouteChartSource.cs` | Translated S-57 feeds the same danger check; untested on mixed data. |
| NavigationalHazardsDFLS | S-101PC | — | Partial | `src/EncDotNet.S100.Datasets.Pipelines/Navigation/CatalogRouteChartSource.cs` | S-57 (CSCL) and S-101 ranked together; no product preference at equal scale. |
| NavigationalHazardsDFMon | — | — | Gap | — | No monitoring mode. |
| NavigationalHazardsDFMonLS | — | — | Gap | — | No monitoring mode. |
| SpecialConditionsDF | — | — | Partial | `src/EncDotNet.S100.Datasets.Pipelines/Navigation/CatalogRouteAreaSource.cs` | S-57 areas read from translated cells; same limited set. |
| SpecialConditionsDFLS | — | — | Partial | `src/EncDotNet.S100.Navigation/AreaCheck.cs` | Finest-cell rule spans products; untested on mixed data. |
| SpecialConditionsMonDF | — | — | Gap | — | No monitoring mode. |
| SpecialConditionsDFLSMon | — | — | Gap | — | No monitoring mode. |
| SafetyContourDF | — | — | Partial | `src/EncDotNet.S100.Datasets.Pipelines/S57DatasetProcessor.cs` | S-57 drawn through the S-101 PC with the mariner's contour; not compared with plots. |
| SafetyContourDFLS | — | — | Partial | `src/EncDotNet.S100.Core/DataModel/CellScaleRanking.cs` | Equal-scale overlap resolved by shallowest depth, not product. |
| SafetyContourDFMonLS | — | — | Gap | — | No monitoring mode. |
| SafetyContourDFMon | — | — | Gap | — | No monitoring mode. |
| SECOMSelection | S-98: 20.4.3 (i); IEC 63173-2 | — | Supported | `src/EncDotNet.S100.Collections/Secom/SecomRegistry.cs` | Registry search, TLS or signed requests, product filters. |
| SECOMManagement | S-98: 20.4.3 (i) | — | Partial | `src/EncDotNet.S100.Viewer/ViewModels/Secom/SecomConnectionState.cs` | Connection state and refresh; no subscriptions or update-available status. |
| SECOMAcknowledgment | S-98: 20.4.3 (iii) | — | Partial | `src/EncDotNet.S100.Collections/Secom/SecomSignature.cs` | Signatures checked on download; no acknowledgement sent. |
