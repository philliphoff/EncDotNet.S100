#### Agent guidance

Pass an S-57 base cell (`.000`) and an explicit output path. By default,
sequential sibling updates are discovered and folded into the source state
before translation; use `--no-updates` only when the bare base edition is
required. Use `--report` to capture translation coverage and dropped or
unmapped content as JSON.

The product written depends on the cell. An inland ENC (its `DSID` declares
`PRSP` = 10, e.g. a USACE river chart) is written as an S-401 (IEHG inland ENC)
dataset; any other cell is written as S-101. The choice is made after sibling
updates are folded. Leave `--target` at `auto` unless a specific product is
required:

- `--target s101` on an inland cell writes S-101, which drops the inland object
  classes and attributes that have no S-101 equivalent.
- `--target s401` on a maritime cell writes S-401 and prints a warning; maritime
  content that S-401 does not define is dropped.

The summary line names the product and edition written (`as S-401 1.3.0`). The
`--report` JSON carries `product`, `productEdition` and `detectedProduct` (what
`auto` would have chosen). The output file name is taken as given; `s100 info`
on the result reports `S-401` or `S-101`.
