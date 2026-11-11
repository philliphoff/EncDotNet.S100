# NDBC fixtures

Fixtures for `EncDotNet.S100.DynamicSources.Ndbc.Tests`: two `realtime2`
standard meteorological files and a station table, captured from NOAA's
National Data Buoy Center at **2026-10-29T00:14:23Z**. NOAA data is US
government work and public domain.

| File | Source | Size | Contents |
| --- | --- | --- | --- |
| `44013.txt` | `https://www.ndbc.noaa.gov/data/realtime2/44013.txt` (602,634 bytes, 6,409 rows) | 13,630 bytes, 143 rows | Moored buoy 44013, BOSTON 16 NM East of Boston, MA: the 2 header lines and the newest 24 hours, 2026-10-27 23:50 to 2026-10-28 23:40 UTC |
| `BUZM3.txt` | `https://www.ndbc.noaa.gov/data/realtime2/BUZM3.txt` (103,870 bytes, 1,103 rows) | 2,444 bytes, 24 rows | C-MAN station BUZM3, Buzzards Bay, MA: the 2 header lines and the newest 24 hours, 2026-10-28 00:00 to 23:00 UTC |
| `activestations.xml` | `https://www.ndbc.noaa.gov/activestations.xml` (272,488 bytes, 1,352 stations, `created="2026-10-29T00:10:05UTC"`) | 1,856 bytes, 8 entries | An excerpt; see below |

The text files are byte-for-byte the head of NDBC's files (LF line endings,
93 characters a row, no trailing whitespace), cut after the last row less
than 24 hours older than the newest.

## What the real files show

- **Header.** `#YY  MM DD hh mm WDIR WSPD GST  WVHT   DPD   APD MWD   PRES  ATMP  WTMP  DEWP  VIS PTDY  TIDE`
  and the units line `#yr  mo dy hr mn degT m/s ...`, as NDBC documents.
- **`MM`** is right-aligned in each column, like the numbers. Whole groups go
  missing together (waves), or single values (44013's 22:30 water
  temperature; its 09:10 row has a wave height but no dominant period).
- **Rows can be missing altogether**: 44013 has no 23:30 row, so it has 143
  rows rather than 144.
- **Cadence.** 44013 reports wind every 10 minutes and waves at :20 and :50.
  BUZM3 reports hourly only, with no wave or water temperature sensors (those
  columns, and in this excerpt the air temperature and dew point, are `MM`).
- **Calm.** BUZM3's 17:00 and 18:00 rows have `WDIR` `MM` and `WSPD` `0.0`.
- **Pressure tendency** is on the hour and always signed, even at zero
  (`+0.0`, `-0.0`).
- **Station table.** Ids are lower case for C-MAN and other fixed stations
  (`buzm3`); the parser upper-cases them to match the `realtime2` file names.
  Not every entry has `elev`, and the 48 TAO buoys have only `id lat lon elev
  name owner pgm type`, with no `met` attribute (and no `realtime2` file).
  Names can hold escaped characters (`&amp;`). The file is one line of XML
  declaration and `<stations created count>`, then a comment, then one
  `<station/>` per line.

## The station table excerpt

The entries are copied verbatim from the capture, in file order, and the
`count` attribute is set to the excerpt's 8 entries:

| Entry | Why it's there |
| --- | --- |
| `32303` | TAO buoy with no `met` attribute: not meteorological |
| `44013` | The moored buoy fixture |
| `44020` | NANTUCKET SOUND, a meteorological buoy inside the source tests' New England viewport that has no fixture file, so the fake handler answers 404 for it. **The live `realtime2/44020.txt` exists** (200 when captured): no meteorological station in that area lacked a file (all 102 in 38–46°N, 77–65°W were probed), so the failing station is simulated |
| `44403` | DART station (SABLE ISLAND BANK) with `met="n"`. It replaces 44401, which is no longer in the live table |
| `46026` | SAN FRANCISCO, outside the New England viewport |
| `buzm3` | The C-MAN fixture, lower case as published |
| `kgbk` | Oil rig with `&amp;` in its name and no `elev` |
| `nolat` | **Hand-edited, not from NDBC.** The live file had no malformed entry, so this one (no `lat`) is kept to test that such entries are skipped. A comment in the file labels it |

## Updating the fixtures

To refresh from a new capture, keep the file names, trim the text files the
same way, and update:

- `NdbcRealtimeParserTests`: the newest row of each file, the 23:20 wave
  row, and the rows named in `Missing_values_are_null` and the C-MAN calm.
- `NdbcStationTableTests`: the ids in `Reads_every_well_formed_entry`.
- `NdbcObservationSourceTests`: `A_partial_content_response_...` expects the
  newest wave height within its first 1,000 bytes.

The source tests take the clock from each file's newest row, so they don't
depend on the dates.

Format: NDBC, "Measurement Descriptions and Units",
`https://www.ndbc.noaa.gov/faq/measdes.shtml`.
