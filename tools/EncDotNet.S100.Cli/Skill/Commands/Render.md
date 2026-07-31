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

Use `--bbox` or the `--center` plus `--scale` pair for an explicit vector or
composite viewport. Those viewport forms are rejected for single coverage
products and for display-list JSON. `--format json` is single-dataset vector
output describing portrayal instructions, not pixels.

Use `info` first to discover time-step indices and S-411 display modes.
`ice-navigational` is only a provisional visual preview and is not a
POLARIS/RIO risk calculation.
