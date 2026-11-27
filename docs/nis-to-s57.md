# Converting an NIS to S-57

Hydrographic offices that produce charts with Esri's ArcGIS Maritime keep
their chart data in a **Nautical Information System (NIS)**: a geodatabase of
nautical features from which every product is cut. EncDotNet.S100 can read an
S-57 NIS stored in a file geodatabase and write any of its cells as an
[IHO S-57](https://iho.int/en/standards-and-specifications) Edition 3.1 base
cell, with no Esri software. Once a cell is S-57, the rest of the library
applies: validate it, render it, or [bring it into the S-101
pipeline](s57-to-s101.md).

Use it to look at production data as a mariner would see it without an Esri
export: convert a cell, or [open the NIS directly](#open-an-nis-directly) with
`tiles serve`, `render` and `identify`.

> [!IMPORTANT]
> The output isn't a production ENC and mustn't be used for navigation. It
> isn't checked against S-58, and the converter writes dataset records the NIS
> doesn't hold (edition, issue date, coverage) from defaults you can override.
> Producers should issue cells with ArcGIS Maritime's own export.

## Convert a cell

An NIS usually holds many cells. List them first:

```bash
s100 nis convert --list path/to/production.gdb
```

```text
╭──────────────┬──────────╮
│ Cell (DSNM)  │ Features │
├──────────────┼──────────┤
│ US5CA98M.005 │     4190 │
│ US5NY1DM.010 │     8128 │
╰──────────────┴──────────╯
```

Then convert one, by its full name or without the extension:

```bash
s100 nis convert --to s57 --cell US5CA98M -o US5CA98M.000 path/to/production.gdb
```

Add `--exchange-set` to write `ENC_ROOT/CATALOG.031` and
`ENC_ROOT/US5CA98M/US5CA98M.000` under the `-o` directory instead, and
`--report report.json` to keep the diagnostics. The
[CLI reference](cli.md#nis-convert) lists every option.

Check the result and look at it:

```bash
s100 validate US5CA98M.000
s100 render US5CA98M.000 tahoe.png
s100 s57 convert -o 101US5CA98M.000 US5CA98M.000
```

## Open an NIS directly

The dataset commands open an NIS without a separate conversion. A `.gdb`
folder counts as a folder of datasets, with one S-57 dataset per cell, and
`<path>.gdb/<DSNM>` names one cell wherever a dataset file goes:

```bash
s100 tiles serve path/to/production.gdb
s100 identify path/to/production.gdb --lat 39.0956 --lon -120.0386
s100 render path/to/production.gdb/US5CA98M.005 tahoe.png
s100 render --layer path/to/production.gdb/US5CA98M --layer path/to/production.gdb/US5NY1DM -o both.png
s100 info path/to/production.gdb/US5CA98M
```

A cell path takes the source name with or without its extension. Each cell is
translated the way `nis convert` translates it, written as a `.000` and kept
under your local application data (`EncDotNet.S100/NisCells`), so the
commands then read an ordinary S-57 cell: `info` shows that cached file. The
cache is keyed on the geodatabase's files, their sizes and write times. An
edited geodatabase is translated again the next time it's opened, and a
geodatabase open in ArcGIS isn't, because its lock files are ignored. The
first time, translating takes a few seconds for each cell.

`tiles serve` with a `.gdb` checks its files for changes as it does any
folder, so an edit is picked up while serving. A cell path given with
`--layer` is read when the command starts.

To write S-101 instead, use `nis convert --to s101`. It translates the S-57
cell on to S-101 the way `s100 s57 convert` does.

## What the NIS holds

An S-57 NIS keeps S-57 objects as rows of feature classes grouped by theme
and geometry, such as `AidsToNavigationP`, `DepthsA` or `CoastlineL`.

- **Object class.** Each feature class holds several S-57 object classes as
  subtypes, in an `FCSubtype` field. The subtypes are named after the object
  class: `BOYSPP_BuoySpecialPurpose`, `M_COVR_Coverage`.
- **Attributes.** S-57 attributes are fields named by their acronym, such as
  `BOYSHP`, `COLOUR` or `DRVAL1`. Beside them sit Esri's own fields: editor
  tracking, verification, `NIS_PRODUCTS`, and others.
- **Identity.** `LNAM` holds the feature's S-57 long name, and `DSNM` the
  name of the cell it came from.
- **Scale.** `PLTS_COMP_SCALE` holds the compilation scale.
- **Collections.** S-57 collection objects (`C_AGGR`, `C_ASSO`) are rows of
  `PLTS_COLLECTIONS`, with no geometry.
- **Relationships.** `PLTS_FREL` holds feature relationships, each a source
  and destination long name and a relationship indicator (master, slave or
  peer).
- **Soundings.** Each sounding is a `SoundingsP` row with its depth as Z,
  and has no long name. ArcGIS Maritime groups soundings and names the groups
  when it exports.

## How it's converted

The conversion runs in two steps, kept apart on purpose:

1. **A source reads the NIS into a source-neutral model.** `NisFeature`,
   `NisRelationship` and the `INisSource` interface describe an NIS without
   saying how it's stored. `FileGdbNisSource` reads a file geodatabase with a
   managed, read-only reader. It decodes only storage: subtype codes become
   object class acronyms, and shape buffers become geometry. An ArcGIS Pro
   add-in can implement `INisSource` over Pro's geodatabase API, which also
   reaches enterprise geodatabases, edit versions and unsaved edits.
2. **`NisToS57Translator` turns the model into an S-57 document.** It
   interprets the NIS conventions, the same way for every source.

The translator:

- writes fields named after S-57 attributes as attributes, and national ones
  (`NOBJNM`, `NINFOM`, `NTXTDS`) as NATF. Esri's fields are skipped;
- writes `-32767`, which the NIS stores for an unknown value, as an empty
  value, S-57's way of saying a value is unknown;
- keeps list values (`COLOUR = '1,11'`) as they are, and writes enumerations
  stored as floating-point numbers (`BOYSHP = 2.0`) as integers;
- makes the long name the feature object identifier (FOID). The NIS writes
  the agency as a two-character S-62 token (`US`); only `US` (550) is built
  in, and `--agency-code` adds others;
- turns each `PLTS_FREL` row into a feature pointer (FFPT) on its source;
- gives a point's `P_QUAPOS`, `P_POSACC` and `P_HORDAT` to its isolated node
  as QUAPOS, POSACC and HORDAT;
- groups soundings with the same attributes into SOUNDG objects of up to 500
  soundings each.

The NIS stores each feature's geometry on its own, but S-57 uses chain-node
topology, in which features that meet share their edges. The translator
nodes every line and area ring against all the others, so a depth area and
the land beside it use the same edge. It then joins the pieces back into the
longest edges the topology allows. Area rings are written exterior first and
clockwise, then interior and anticlockwise.

An ENC's M_COVR objects must tile the cell. When the cell has none, the
translator adds them: CATCOV 1 over the union of the skin-of-the-earth areas
(DEPARE, DRGARE, LNDARE, UNSARE, FLODOC, HULKES, PONTON), and CATCOV 2 over
the rest of the data's extent. `--no-coverage` turns this off.

The NIS doesn't hold a cell's edition, issue date or producing agency. The
command writes edition 1, today's date and the commonest agency of the
features, and `--edition`, `--issue-date` and `--agency` change them. The
compilation scale and the sounding and vertical datums come from the
commonest values among the features.

## The summary

```text
╭───────────────────────────────────────┬──────────╮
│ Conversion                            │    Count │
├───────────────────────────────────────┼──────────┤
│ NIS features read                     │     4190 │
│ S-57 feature records written          │     2389 │
│ Soundings (SOUNDG objects)            │ 1808 (6) │
│ Vector records written                │     5345 │
│ Feature pointers written              │       26 │
│ Unknown values (-32767) written empty │      484 │
│ M_COVR added                          │      yes │
│ Identities assigned                   │        6 │
│ Object classes dropped                │        0 │
│ Unknown attributes                    │        0 │
│ Unknown agencies                      │        0 │
╰───────────────────────────────────────┴──────────╯
```

- **Identities assigned** counts features given a new long name: usually the
  SOUNDG groups, plus any feature whose long name was missing or repeated.
- **Object classes dropped** counts rows whose subtype isn't an S-57 object
  class, such as Esri's `Representation` subtype.
- **Unknown attributes** counts fields that look like S-57 acronyms but aren't
  in the IHO catalogue.
- **Unknown agencies** counts long-name tokens with no numeric code. Those
  features take the producing agency; add the code with `--agency-code`.

## Limits

- Only file geodatabases are read. Enterprise and mobile geodatabases, edit
  versions and writing are out of scope for the CLI.
- Only an S-57 NIS is read. ArcGIS Maritime's S-101 database stores S-101's
  model differently and isn't read yet.
- The NIS's own topology tables (`PLTS_Vector*`) aren't used: the topology is
  rebuilt from each feature's geometry. Spatial attributes on edges
  (`PLTS_SpatialAttributeL`) aren't converted.
- The NIS's product records aren't read. Choose a cell by its source cell name
  instead, and narrow it with `--bbox`.
- Curved segments are read as the straight lines between their vertices.

## Try it on Esri's sample

Esri published a small NIS with its
[ArcGIS for Maritime Charting quick tutorial](https://github.com/enags/maritime-charting-quick-tutorial)
(Apache License 2.0). It holds NOAA's US5CA98M (Lake Tahoe) and US5NY1DM (New
York) cells, in the ArcMap-era data model `NIS (10.3.0.0)`. It isn't part of
this repository: download it with

```bash
tools/nis-fixtures/fetch-tutorial-nis.sh
```

The script fetches it at a pinned commit into `~/.cache/encdotnet-nis-fixtures`
(or `$ENC_DOTNET_NIS_FIXTURES`) and checks every file against
`tools/nis-fixtures/TUTORIAL_NIS.gdb.sha256`. The tests that use it skip when
it's absent.

## See also

- [Datasets.Nis README](../src/EncDotNet.S100.Datasets.Nis/README.md): using
  the library from code, and implementing `INisSource`.
- [Bringing S-57 into the pipeline](s57-to-s101.md): what happens to the cell
  next.
- [CLI reference](cli.md#nis-convert): every option of `nis convert`.
