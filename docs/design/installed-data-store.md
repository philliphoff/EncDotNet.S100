# Installed-data store — Design Note

> Status: **Design** ([#1129](https://github.com/philliphoff/EncDotNet.S100/issues/1129)).
> No production code yet. The note proposes a store of installed datasets
> that later exchange sets update: new editions, updates, re-issues,
> cancellations and support files. It also proposes the S-98 Appendix C
> update status report built from that store and from merged S-128
> catalogues. Choices that are the user's to make are listed in §9 and
> aren't decided here.

## 0. Problem

S-98 assumes an ECDIS has a **System Database**: the datasets it has
installed, each at a known edition and update. Each exchange set changes
that state. A new edition replaces a dataset and its updates, an update
extends it, a re-issue replaces it, and a cancellation removes it (S-98
§20.5). Support files are added, replaced and deleted the same way (§21.2).
An update status report (Appendix C) then shows the mariner, and an
inspector, whether the installed data is up to date.

SoundCharts has no System Database. The Library ([Dataset
collections](dataset-collections.md)) **indexes sources where they are**
and never copies them. Its index (`SourceIndex`) is derived data that can
always be rebuilt from the source. As a result:

- **Updates apply only within the same folder or exchange set as the base cell.**
  - `S101ExchangeSetUpdatePlan` matches updates to base cells within one catalogue and says "cross-exchange-set application is intentionally not supported".
  - `S101FilesystemUpdateDiscovery` looks only in the base cell's folder.
  - The viewer skips an update with no base in its set (`Status_ExchangeSetOrphanUpdate`).
  - The library loaders throw on such an update.
- **A re-issue or new edition in a later set doesn't replace the earlier one.** Both stay in the Library as separate items.
- **A cancellation doesn't remove anything already loaded.**
  - `DatasetDiscoveryMetadata.IsWithdrawal` drops a fileless cancellation record from the set that carries it. A dataset installed from an earlier set stays.
  - `S101UpdateApplicator` has no cancellation handling.
- **Replacement support files aren't kept.** Support files are read from the same set as their dataset.
- **There is no update status report.**
  - S-128 datasets load and are listed (`AddSessionCatalogue`), but they aren't merged.
  - Nothing compares them with installed data. `S128ProductEntry` derives a status from `serviceStatus`/`distributionStatus` only.
  - Library items downloaded from a feed are flagged out of date against that feed, not against S-128.

### What is already done

| Where | What | Covers |
|---|---|---|
| S-164 slice 3 | `S101UpdateApplicator` refuses an update for another edition (DSID `DSED`) and stops at a gap in the sequence | NewerEditionUpdate, OlderEditionUpdate, InvalidSequence (library verdict) |
| S-164 slice 3 | `CATALOG.SIGN` checked | Catalogue signature tests |
| [#1241](https://github.com/philliphoff/EncDotNet.S100/issues/1241) | `S57UpdateApplicator`: edition, update number and record version (`RVER`) checks; updates a re-issue already holds are skipped; `EDTN` 0 reported as a cancellation; update-only S-57 exchange sets CRC-checked; SoundCharts warns | S-64 §2.2.1–2.2.7 (library verdict) |
| Library | `CollectionItem` carries `Edition`, `Update`, `IssueDate`, `UpdateApplicationDate`, `Status` (Unknown, Active, Superseded, Cancelled, Planned); feed downloads replace an older cell folder (`EncCellDownloader`) | PowerUp (per item) |

What's missing is state that **lasts between loads**: which datasets are
installed, at what edition and update, and from which data server. Every
row of §6 that is still Partial is blocked on that.

## 1. Constraints

- **S-98 §20.4.2 c.** Keep a log entry for every update applied or
  rejected: date and time, edition and update numbers, anomalies, and
  whether the update was manual or automatic.
- **S-98 §20.4.3 b.** Apply updates in an uninterrupted sequence. Where an
  update can't be applied, show "Chart information not up to date" for as
  long as the chart is in use (§20.5, SSE 127 and SSE 130).
- **S-98 §20.4.3 e.** After an exchange set is received, give a summary
  report: the issuing authority, the update numbers, the datasets affected,
  their edition and date, and the number of updates.
- **S-98 §20.5.** `S100_Purpose` says whether an entry is a new dataset, a
  new edition, an update, a re-issue or a cancellation.
  - A re-issue replaces the dataset and its updates up to the re-issue's
    date.
  - A cancelled dataset is either hidden or shown with "Dataset `<name>`
    has been cancelled and may not be up to date. Under no circumstances
    should it be used for primary navigation". The choice is the OEM's.
- **S-98 §21.2.** Support files carry `new`, `replacement` or `deletion`.
  A support file is deleted only when no other dataset references it.
- **S-98 §20.4.1.** S-57 updates follow S-52 Appendix 1. In dual-fuel mode
  the same store holds S-57 cells (Appendix C, "hybrid exchange set").
- **Dataset names are unique** (S-100 Part 17). An update, re-issue or
  cancellation finds its dataset by name, never by position or coverage.
- **The Library's rule stays.** In-place sources are referenced, never
  copied, and their index is derived. The store is a different thing: its
  state is the record of what was installed, so it **is not** derived and
  can't be rebuilt from the sources.
- **Packages are preview**, so the Library model can change without
  keeping the old shape.

## 2. Where installed data lives

**Proposal: a store separate from the Library's sources, holding its own
copies of installed files.**

- **Location.** The store sits under the app data folder:
  `LocalAppData/EncDotNet.S100/Installed`, or under `--data-dir` when one
  is given. That's next to the existing `Downloads` and
  `CollectionIndexCache` folders, and it uses the same rules as
  `ViewerDataPaths`.
- **Layout.** One folder per data server, then one per dataset name:
  `Installed/<data server>/<product>/<dataset name>/`. Each dataset folder
  holds the current base file, the updates applied since, and the support
  files it references. Support files are kept once per data server and
  referenced from the datasets (§4.5).
- **Index.** The state is in `Installed/store.json` (§3), written
  atomically like `collections.json`. The log goes in
  `Installed/log.jsonl`, one line per event.
- **In the Library.** The store shows as one source of a new kind,
  `installed`. Its items are the installed datasets, with the same
  load, load-as-you-pan and coverage behaviour as other items. Its index is
  built from `store.json` rather than by scanning.
- **Installing.** Installing an exchange set (§4) copies the files the
  store keeps. The set itself can then be moved or deleted.
- **In-place sources stay.** A local folder, a ZIP or a manifest is still
  browsed and loaded in place. Its datasets are **not installed**, so the
  update status report lists them as Unknown, as Appendix C asks for data
  not delivered by a data service.

Alternatives rejected:

- **Track installed state over in-place sources.** Updates for one cell
  would sit in several folders, any of which could move or be deleted. A
  re-issue would have to override a file the store doesn't own. Rebuilding
  the index would lose the history.
- **Install by merging into one document per cell.** Writing the merged
  S-57 or S-101 document would hide which updates were applied, which the
  log, the summary report and "review updates" (§20.4.3 f) need. Keeping
  the original files and applying them at load costs little:
  `S57UpdateApplicator` and `S101UpdateApplicator` already do it on every
  load.

Downloads from NOAA and SECOM land in `Downloads` today and replace older
cell folders. Whether they install into the store automatically is
open question Q2.

## 3. The installed-dataset table

`store.json` holds one record per installed dataset:

| Field | S-101 / S-100 | S-57 | Notes |
|---|---|---|---|
| Data server | `dataServerIdentifier` (`CATALOG.XML`); S-128 producer code | `CATALOG.031` has none: the cell's agency (DSID `AGEN`), or the exchange set's producer | Groups the report (Appendix C "Data Server Name"). See Q4. |
| Product | `productSpecification` | `S-57` | |
| Dataset name | file stem | cell name (`DSNM` stem) | The key. Unique within a data server. |
| Edition | DSID `DSED`; catalogue `editionNumber` | DSID `EDTN` | |
| Update | DSID `UPDN` of the last applied update | DSID `UPDN` of the last applied update | `-` for products without updates. |
| Base update | update number of the installed base file | `UPDN` of the base (non-zero after a re-issue) | |
| Issue date | issue date of the last applied update | `ISDT` of the last applied update | Appendix C column. |
| Update application date | `UADT` where given | `UADT` | |
| Files | base, updates in order, support files | base, updates in order, text and picture files | Paths relative to the dataset folder. |
| State | Active, Cancelled, Withdrawn | same | §4.4. |
| Pending updates | updates received but not applied, with the reason | same | §4.3. |
| Installed from | exchange set id, install time | same | Last install that changed the record. |
| Last report | the `S101UpdateReport` / `S57UpdateReport` of the last apply | same | |

Products that are replaced whole rather than updated, such as S-102,
S-104, S-111 and S-124, use the same record with no updates. A new issue
replaces the base file.

The log (`log.jsonl`) records each install, update, refusal, re-issue,
new edition, cancellation and support-file change. Each entry gives the
time, the dataset, the edition and update, the outcome, any anomaly
message, and `automatic` or `manual` (§20.4.2 c). Manual updates are out of
scope (S-164 ManualUpdate\* stay Gap), so every entry is `automatic` until
they exist.

## 4. Installing an exchange set

`InstalledDataStore.Install(exchange set)` reads the set's catalogue and
handles each entry in a fixed order. Support files come first, then base
datasets (new datasets, new editions and re-issues), then updates in
update-number order per dataset, then cancellations. The result is a
summary report (§20.4.3 e), and every step is logged.

### 4.1 Classifying an entry

| Kind | S-100 (`CATALOG.XML`) | S-57 (`CATALOG.031` + DSID) |
|---|---|---|
| New dataset | `purpose` = `newDataset`, name not installed | `.000`, name not installed |
| New edition | `purpose` = `newEdition`, or a base with a higher edition than installed | `.000` with a higher `EDTN` than installed |
| Re-issue | `purpose` = `reissue` | `.000` with the installed `EDTN` and `UPDN` > 0 |
| Update | `purpose` = `update` | `.001`… with `EDTN` ≥ 1 (`EXPP` = R) |
| Cancellation (file) | an update file with edition 0 | an update with `EDTN` 0 |
| Cancellation (no file) | `purpose` = `cancellation` on a non-sequential file name (`IsWithdrawal`) | — |
| Support file | `S100_SupportFileRevisionStatus` (`SupportFileDiscoveryMetadata.RevisionStatus`) | `.TXT` / `.TIF` named by `TXTDSC` / `PICREP` |

Where the catalogue and the dataset's own header disagree, the header
wins and the disagreement is logged.

### 4.2 Base datasets

- **New dataset.** Install it with any updates for it in the same set.
- **New edition.** Replace the base, drop the installed updates and any
  pending updates for the old edition, and apply the set's updates for the
  new edition. Updates for the old edition that arrive later are refused by
  the applicators' edition check.
- **Re-issue.** Replace the base and drop the installed updates up to the
  re-issue's update number. Pending updates after it stay pending and are
  retried. Both applicators already skip updates the base holds, so
  updates kept from before the re-issue cause no harm.
- **Older edition or older re-issue.** If the edition or update number is
  lower than what's installed, refuse it and log it.

### 4.3 Updates

The updates for a dataset are applied with the existing applicators,
starting from the installed base. All of the dataset's updates go in: the
installed ones followed by the new ones. That makes the result the same
whether the updates came in one set or in ten.

- An update that applies is copied into the dataset folder, and `Update`,
  `Issue date` and the report are updated.
- An update refused for its **edition** is logged and dropped.
- An update after a **gap** is kept under *Pending updates*, so a later set
  that supplies the missing update completes the sequence. The dataset
  shows "Chart information not up to date" until then (SSE 130).
- An update whose **records don't follow on** (#1241's `RVER` check) is
  logged and dropped. The sequence stops there, as for a gap.

### 4.4 Cancellations

- **The cancellation is recorded.** An edition-0 update or a fileless
  cancellation sets the dataset's state to **Cancelled**. It is reported
  at install time with the dataset name (§20.5).
- **What the record covers.** A cancellation for a dataset that isn't
  installed is logged only. A dataset that its data server's latest S-128
  no longer lists, or lists as withdrawn, is marked **Withdrawn** by the
  status report (§5), not by install.
- **Who needs S-101 changes.** `S101UpdateApplicator` needs the
  edition-0 rule that `S57UpdateApplicator` has. That is slice 1.
- **Hide or warn.** Whether a cancelled dataset is hidden or shown with
  the S-98 warning is open question Q3.
- **When its files go.** When a cancelled dataset's files are deleted is
  open question Q5.

### 4.5 Support files

- **One copy per data server.** Support files are stored once per data
  server, with a reference count of the datasets that refer to them.
  Their discovery metadata names those datasets, and for S-57 the
  `TXTDSC` and `PICREP` attributes do.
- **Revision status.**
  - `new` adds the file.
  - `replacement` overwrites it.
  - `deletion` removes it only when no other installed dataset refers to it
    (§21.2). Otherwise it is logged and kept.
- **Loading.** When an installed dataset loads, its support-file lookups
  resolve to the store, not to the exchange set it came from.

### 4.6 Where install happens

These are the proposed entry points:

- **Library and file menu.** An **Install** action next to **Load** on an
  exchange set: on an item group in the Library, and on File › Open of an
  exchange set.
- **`s100`.** `s100 install <exchange set>` and `s100 installed list` work
  over `--data-dir`.
- **MCP.** `install_exchange_set` and `list_installed_datasets`, plus a
  `library_action` value of `install`.

Loading an exchange set without installing it keeps working as it does
now.

## 5. Update status report (S-98 Appendix C)

### 5.1 Merging S-128

- **Install, don't just load.** S-128 datasets are installed like any
  other product: a new S-128 issue replaces the last one from the same
  data server.
- **Reference catalogue.** For each data server, the merged S-128 is built
  per product: the revision information for product *P* comes from the
  latest installed S-128 that covers *P*. A later S-128 holding only S-104
  doesn't change the reference for S-101 (Appendix C "Product
  Independence").
- **Reference date.** The update reference date for a product is the
  `issueDate` of that S-128.
- **Where it lives.** `S128CatalogueQuery` already reads products,
  editions, updates and dates. The merge goes in `EncDotNet.S100.Datasets.S128`
  with no viewer dependency, so `s100` and MCP can use it too.

### 5.2 Status per dataset

| Status | Rule (Appendix C Table C-2) |
|---|---|
| Up to date | The installed edition and update equal the merged S-128's, and the reference date is less than four weeks before the report is run. |
| Not up to date | The installed edition or update is behind the S-128's, or the reference date is four weeks old or more, or the dataset has pending updates (§4.3). |
| Withdrawn | The dataset is installed but the merged S-128 marks it withdrawn or cancelled. |
| Cancelled | The dataset was cancelled by an exchange set (§4.4) and is still in the store. The summary counts it separately. Table C-2 doesn't define it. |
| Unknown | No S-128 from its data server covers the product. Or the S-128 doesn't list the dataset, which Appendix C treats as partial S-128 information. Or the dataset isn't installed: it comes from an in-place source. |

Every Library item carries this status. It replaces the feed-based
"out of date" flag for installed items; feed sources keep theirs.

### 5.3 The report

- **Two reports.**
  - The **ENC** report covers S-101, S-57, S-102 and S-104.
  - The **ENP** report covers every other installed product except
    S-128.
- **Header.** The report name, the vessel name and identifier, the update
  reference date, the report date, and the content (all, or the route
  between named waypoints).
- **Summary.** Total, Up to date, Not up to date, Withdrawn, Cancelled and
  Unknown counts.
- **Tables.** One table per data server, grouped by product, with these
  columns: dataset name, edition, update ("-" for products without
  updates), issue date and status.
- **Route filter.** The datasets whose coverage meets the route corridor.
  The corridor width is the "user-specified distance" S-98 quotes from
  MSC.532(106) 11.3.5, not the cross-track distance. The nearest setting
  today is the route check's `RouteCheckSettings.NearbyDistance`, measured
  beyond the cross-track limit; slice 7 settles whether to reuse it.
- **Output.**
  - SoundCharts shows the report in a dialog that can be printed or saved
    as PDF.
  - `s100 installed report --enc|--enp [--route <file>]` writes text or
    JSON.
  - MCP adds `get_update_status_report`.

The vessel name and identifier don't exist in SoundCharts yet; see Q7.

### 5.4 Completeness

Appendix C also allows a **completeness** check: what the merged S-128
lists that isn't installed, per product. For S-124 it can check against
the in-force bulletin instead (S-164 RevisionCompleteness).
`NavigationalWarningSet` already reads bulletins
([#992](https://github.com/philliphoff/EncDotNet.S100/issues/992)). This
is the last slice.

## 6. Tests each part unblocks

S-164 v2.0.0 Part 2 rows that are Partial or Gap today (see
[S-164 conformance](s164-conformance.md) §7):

| Test | Today | Unblocked by | After |
|---|---|---|---|
| PowerUp | Partial: per item only | §3 table, Library `installed` source | Passes: table of everything installed |
| RemoveCell | Partial | Uninstall a dataset | Passes |
| SequentialUpdates | Passes in library; apps only within one set | §4.3 | Passes in the apps across sets |
| InvalidSequence | Passes in library; toast only | §4.3 pending updates, "not up to date" indication | Passes with a lasting indication |
| ReIssueAndUpdate | Partial | §4.2 re-issue | Passes |
| CancelledDatasets | Partial | §4.4 | Passes |
| Reissues | Partial | §4.2 | Passes |
| ECDISManagement | Partial: orphan updates skipped | §4.3: a later set's updates find their base | Passes |
| SupplementaryFiles | Partial | §4.5 | Passes |
| Fileless Cancellation | Partial | §4.4 | Passes |
| UpdateStatusReportENC | Partial | §5 | Passes |
| UpdateStatusReportENP | Gap | §5 | Passes |
| MissingRevisionInformation | Gap | §5.2 Unknown | Passes |
| MultipleRevisionInformation | Gap | §5.1 merge | Passes |
| RevisionInformation | Gap | §5.2 | Passes |
| RevisionUpdate | Gap | §5.1: a later S-128 replaces the earlier one | Passes |
| RevisionCompleteness | Gap | §5.4 | Passes |

The §2.4–2.7 permit, signature and protected-data tests stay with
[#1128](https://github.com/philliphoff/EncDotNet.S100/issues/1128). The
manual-update tests (UpdateRejection, ManualUpdate\*) need a
manual-update editor and stay Gap. Catalogue installation
(InitialCatalogues, UpdateCatalogues, OutOfCatalogueSequenceDataset,
MultipleRevisions) installs feature and portrayal catalogues rather than
datasets; it could use the same store, but this note doesn't cover it
(Q8).

S-64 §2.2 tests already pass in the library (#1241). Each one gets an
app-level counterpart that installs the S-64 folders one after another, as
the manual delivers them, and checks the store's table and log:

| Test | Installed sets | Checked |
|---|---|---|
| 2.2.2 | Power Up, then 2.2.2 | Update 5 |
| 2.2.3 | 001–005 | Stops at the gap, with an indication |
| 2.2.4 / 2.2.5 | The new edition | Takes its update; the older update is refused |
| 2.2.6 | The re-issue, then update 4 | Update 4 applies |
| 2.2.7 | The cancellation | Cancelled |
| 2.1.5 / 2.2.1 | Corrupt data | Refused at install (CRC) |

## 7. Slices

| Slice | Scope | Depends on |
|---|---|---|
| 1 | **S-101 cancellation and re-issue in the applicator.** Edition-0 update reported as a cancellation, updates the base holds skipped, both as `S57UpdateApplicator` does. Library only. | — |
| 2 | **Store core** (`EncDotNet.S100.Collections` or a new `EncDotNet.S100.Installed`): `store.json`, `log.jsonl`, install of new datasets, new editions, re-issues and in-set updates for S-101 and S-57, a summary report, and uninstall. Tests on S-164 Part 2 and S-64 §2 sets installed in sequence. | 1 |
| 3 | **Cross-set updates and pending updates**: a later set's updates apply to installed bases; pending updates retried. | 2 |
| 4 | **Cancellations and support files** (§4.4, §4.5). | 2 |
| 5 | **Library and apps**: the `installed` source, the Install action, `s100 install` and `installed list`, the MCP tools, the "Chart information not up to date" and cancelled indications on the chart, and the installed-data table view. | 2 (3 and 4 add to it) |
| 6 | **S-128 merge and status** (§5.1, §5.2) in the S-128 package, with status on every Library item. | 2 |
| 7 | **Update status report** (§5.3): dialog, `s100 installed report`, MCP; route filter. | 5, 6 |
| 8 | **Completeness** (§5.4). | 7 |

Slices 3, 4 and 6 are independent of each other once slice 2 lands.

## 8. Not in scope

- **Protected data.** Permits, encrypted datasets and refusing
  unauthenticated data stay with
  [#1128](https://github.com/philliphoff/EncDotNet.S100/issues/1128). The
  store installs only what loads today, and records each file's signature
  outcome so #1128 can refuse at install time later.
- **Manual updates** (S-98 §20.4.4).
- **Installing feature and portrayal catalogues** (Q8).
- **Review of applied updates** (S-98 §20.4.3 f), which highlights what
  each update changed. The store keeps the update files that review would
  need.

## 9. Open questions

These are the user's to decide. The slices above work with either answer
unless noted.

- **Q1. Install by copy, or by reference for local sets?** Copying costs
  disk space and makes the store independent of the user's folders. A
  reference to a local exchange set would avoid the copy, but breaks when
  the folder moves. Copying is assumed here.
- **Q2. Do downloads install automatically?** NOAA cells, SECOM exchange
  sets and other feed downloads could install into the store as they
  arrive, and then show in the report. Or they could stay in `Downloads`
  as in-place items, which the report lists as Unknown, until the user
  installs them.
- **Q3. Hide or warn for a cancelled dataset?** S-98 §20.5 allows either:
  hide it, or show it with the S-98 warning.
- **Q4. What is the data server for S-57 sets?** A `CATALOG.031` doesn't
  name one.
  - The options are the producing agency (DSID `AGEN`), the source or feed
    the set came from, or a name the user gives at install.
  - This decides how S-57 datasets are grouped in the report, and whether
    an S-128 from that service covers them.
- **Q5. Keep cancelled datasets' files?** They could be deleted when
  cancelled, kept until the user removes them, or kept for a set period.
  Keeping them lets a cancelled dataset still be viewed with the warning.
- **Q6. Is the store per data folder or per user?** One store per
  `--data-dir` keeps test and real installs apart; `s100 mcp serve` and
  SoundCharts would then share one only when given the same folder.
- **Q7. Vessel name and identifier.** The report header needs them, and
  SoundCharts has no vessel settings. Options: add them to own ship's
  settings, or leave the header fields empty.
- **Q8. Should the store also install feature and portrayal catalogues?**
  That would cover the S-164 catalogue rows. Today one persisted override
  is kept per product.
