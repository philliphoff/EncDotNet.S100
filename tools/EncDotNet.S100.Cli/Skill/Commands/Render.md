#### Agent guidance

Choose exactly one input form:

1. `render <dataset> <output>` for one dataset.
2. Repeated `--layer <path>` plus an output for an explicit composite.
3. A positional exchange set, or `--from`/`--exchange-set`, for automatic
   exchange-set discovery.

The S-98 authority determines cross-product display-plane order; repeated
`--layer` order is only a within-plane tiebreak. Composite forms do not apply
S-101 sequential updates. Unsupported, missing, or protected exchange-set
members are skipped with warnings.

Use `--bbox` or the `--center` plus `--scale` pair for an explicit
single-dataset or composite viewport. Gridded S-102, S-104, and S-111 products
sample only the intersecting region; projected grids transform the WGS-84
request into their native CRS before sampling. Positioned S-104 DCF1/8 and
S-111 DCF1/3/8 station/node datasets render point glyphs using their exact
time axis. Viewport options are rejected
for display-list JSON because `--format json` describes vector portrayal
instructions, not pixels.

Use `info` first to discover time-step indices and S-411 display modes.
`ice-navigational` is only a provisional visual preview and is not a
POLARIS/RIO risk calculation.
