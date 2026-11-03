#### Agent guidance

Pass an S-101 base dataset (`.000`), `--to s57` and an explicit output path.
Sibling update datasets are folded in first unless `--no-updates` is given. Use
`--report` to capture what S-65 Annex C dropped, folded into `INFORM` or
restructured, as JSON with the Annex C clause for each drop.

The output is an S-57 Edition 3.1 base cell for testing and for tools that read
only S-57 (GDAL/OGR, OpenCPN). It is not a production ENC: nothing checks it
against S-58, and Annex C expects the producer to finish the cell by hand. Don't
present it as a chart for navigation.

With `--exchange-set`, `-o` names a directory and the command writes
`ENC_ROOT/CATALOG.031` and `ENC_ROOT/<cell>/<cell>.000` inside it. Give an
8-character S-57 cell name with `--cell-name`; it defaults to the source file
name and warns when that isn't one. `s100 validate <dir>/ENC_ROOT/CATALOG.031`
checks the catalogue CRCs, and `s100 info` reads the cell back as `S-57`.

The Annex C choices have options: `--no-optional-inform`,
`--inform-separator`, `--max-inform-length` with `--inform-length keep|truncate`,
`--national-language <ISO 639-2>` and `--name-without-usage by-language|drop`.
Leave them at their defaults unless the user asks.
