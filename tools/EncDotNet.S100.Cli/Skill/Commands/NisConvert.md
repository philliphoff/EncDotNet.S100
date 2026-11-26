#### Agent guidance

Pass the NIS file geodatabase (the `.gdb` directory), `--to s57` and an
explicit output path. An NIS usually holds many cells: run
`s100 nis convert --list <source.gdb>` first to see the source cell names
(`DSNM`, such as `US5CA98M.005`) and their feature counts, then choose one with
`--cell`, by its full name or without the extension (`US5CA98M`). With one
cell, `--cell` can be left out.

The reader is managed and read-only: it needs no Esri software and reads only
file geodatabases. Enterprise and mobile geodatabases, and edit versions, are
out of scope. The geodatabase must hold an S-57 NIS (feature classes with
`FCSubtype` and `LNAM` fields); an S-101 Maritime database isn't read.

The NIS holds no edition, issue date or agency code for the cell, so the
command writes edition 1 and today's date unless `--edition` and
`--issue-date YYYYMMDD` say otherwise. Long names give agencies as
two-character S-62 tokens; only `US` (550) is built in, so add others with
`--agency-code TOKEN=CODE`, and set the DSID agency with `--agency`. When the
cell has no M_COVR, the command adds M_COVR objects that tile its extent
(CATCOV 1 over the skin of the earth, 2 elsewhere) unless `--no-coverage` is
given. `--bbox W,S,E,N` keeps only features whose extent overlaps the box.

Use `--report` for the JSON diagnostics: features read and written, soundings
grouped, `-32767` values written as unknown, relationships unresolved,
identities assigned, and dropped object classes or unknown attributes.

The output is an S-57 Edition 3.1 base cell for testing, previewing and tools
that read S-57; it is not a production ENC. Check it with
`s100 validate <cell>.000`, look at it with `s100 render`, and convert it on to
S-101 with `s100 s57 convert`. With `--exchange-set`, `-o` names a directory
and the command writes `ENC_ROOT/CATALOG.031` and `ENC_ROOT/<cell>/<cell>.000`.
