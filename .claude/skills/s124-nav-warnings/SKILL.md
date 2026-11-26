---
name: s124-nav-warnings
description: |
  S-124 Navigational Warnings (Ed 2.0.0 and 1.0.0, GML) specifics:
  edition differences, warning lifecycle and bulletins, multilingual
  attributes, how warnings reach pick card, panel and MCP. USE FOR:
  changes under src/EncDotNet.S100.Datasets.S124/,
  tests/EncDotNet.S100.Datasets.S124.Tests/,
  src/EncDotNet.S100.Specifications/content/S124/, S124DatasetProcessor /
  S124WarningInfo, NavigationalWarningInfo / NavigationalWarningSet, the
  viewer's Navigational warnings panel. Load s100-gml-products too.
  DO NOT USE FOR: S-412 weather warnings or CAP, S-129 UKC (s129-ukc),
  S-101 ENC (s101-enc).
---

# S-124 Navigational Warnings

Load `s100-gml-products` too; this skill records only S-124 specifics.

## Code map
- Reader/typed model: `src/EncDotNet.S100.Datasets.S124/` (`DataModel/`).
- Processor and pick bridge: `S124DatasetProcessor.cs`, `S124WarningInfo.cs`
  in `src/EncDotNet.S100.Datasets.Pipelines/`.
- Product-neutral warning model: `NavigationalWarningInfo`,
  `NavigationalWarningSet`, `WarningValidity`, `NavigationalWarningSection`
  in `src/EncDotNet.S100.Core/DatasetPipelines/`.
- Fixtures `tests/datasets/S124/`: Ed 1.0.0 synthetic, Ed 2.0.0 CCG /
  AUSCOAST / NAVAREA bulletins.

## Spec anchors
- Canonical: **S-124 Edition 2.0.0** (PDF in `docs/`; bundled FC and PC
  2.0.0, Day/Dusk/Night). Reader and typed model also accept **Ed 1.0.0**.

## Editions
| | Ed 1.0.0 | Ed 2.0.0 |
|---|---|---|
| App namespace | `http://www.iho.int/S124/1.0` | `http://www.iho.int/S124/gml/2.0` |
| s100gml | `S100/profile/s100gml/1.0` | `s100gml/5.0` |
| Root / wrappers | `DataSet`, `member`/`imember` | `Dataset`, `members`/`imembers` |
| Agency / country | `productionAgency` / `country` | `agencyResponsibleForProduction` / `nationality` |
| Part text | `warningInformation/information` | `warningInformation/information/text`, one per language |

Wrappers are as seen in fixtures; the reader accepts either shape. Keep
both editions mapped into one typed model.

## Lifecycle and time
1. Features `NavwarnPart`, `NavwarnAreaAffected`, `TextPlacement`; info
   types `NavwarnPreamble`, `References`, `SpatialQuality`. Preamble
   references, cancel relationships and `messageSeriesIdentifier` must
   round-trip.
2. Ed 2.0.0 has `fixedDateRange` only (a date with optional
   `timeOfDayStart` / `timeOfDayEnd`, §4 Figure 4-5) and
   `NavwarnPreamble.cancellationDate`; there is no `dailyDateRange` or
   periodic range.
3. A warning stays in force until its `cancellationDate`, or until a
   cancellation dataset or the latest in-force bulletin drops it
   (§9.2.1). `NavigationalWarningInfo.StatusAt` judges one warning
   alone; anything that shows or uses a status must go through
   `NavigationalWarningSet.StatusAt`, which also applies cancellations
   and bulletins.
4. A bulletin is a `References` with `referenceCategory` 3 (In-Force)
   listing `messageSeriesIdentifier`s, or `noMessageOnHand`
   (Table 8-1, §8.1.3–8.1.4); `navwarnTypeGeneral` 8 and its own
   `warningType` 9–12 are supporting signals. Values 9–12 are
   `warningType` codes, not reference categories. A warning a bulletin
   leaves out is reported cancelled (S-98 §12.12);
   `NavigationalWarningSet.LatestBulletins` lists what a bulletin names
   but isn't loaded (§9.2.2).
5. Warnings link across datasets by `messageSeriesIdentifier`, not
   `xlink:href`.

## Language-bearing attributes
`warningInformation/information`, `generalArea/locationName` and
`navwarnTitle` carry one language per instance, repeated per language
(§4 Figure 4-5). Read every instance (`S124ComplexAttribute.Groups`,
`S124NavwarnPart.Information`), not only the first.

## Display
- Cancellations and text-only warnings (e.g. weekly "in force"
  bulletins) have no geometry; portrayal draws nothing for them and the
  viewer's Navigational warnings panel is where they are found.
- Object information shows a warning via the `NavigationalWarningSection`
  in `FeatureInfo.Sections` (built by `S124WarningInfo`), not attribute
  rows. Add new warning fields there so card, panel and MCP agree.
