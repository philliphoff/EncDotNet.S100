Use `s100` for non-interactive inspection, validation, querying, conversion,
and portrayal of nautical datasets. Prefer machine-readable output when a
command offers it, and treat paths as local filesystem paths.

## Choosing a command

| Goal | Command |
|---|---|
| Discover supported product specifications | `s100 list-specs` |
| Detect a dataset's product and available time steps | `s100 info` |
| Validate a dataset or verify an exchange set | `s100 validate` |
| Query features or coverage values at a position | `s100 identify` |
| Render one dataset, multiple layers, or an exchange set | `s100 render` |
| Convert an S-57 base cell to S-101 | `s100 s57 convert` |
| Serve the read-only query tools to an MCP client over stdio | `s100 mcp serve` |

## General operating guidance

- Run `info` before `render` when the product, edition, display modes, or
  time-step indices are unknown.
- Use `--format json` for automation where supported. Diagnostics and update
  notices are written to standard error so standard output remains parseable.
- A dataset path may be an HDF5 coverage (`.h5`), ISO 8211 cell (`.000`), or
  GML feature dataset (`.gml`). Commands that accept exchange sets also accept
  a directory, `CATALOG.XML`, or a ZIP whose root contains `CATALOG.XML`.
- Coordinates are WGS 84 longitude/latitude unless an option explicitly says
  otherwise. `identify` takes separate latitude and longitude options;
  `render --bbox` and `--center` use longitude before latitude.
- S-101 sibling sequential updates (`.001`, `.002`, and so on) are normally
  discovered beside a base `.000` cell. Read each command's guidance because
  composite rendering deliberately handles updates differently.
- Do not infer that exit code `0` from `validate` means rules were available.
  Its text or JSON result distinguishes “no rules available” from a conformant
  result.

## Global flags

| Flag | Purpose |
|---|---|
| `--help` | Show progressive human-oriented help. |
| `--version` | Print the informational version. |
| `--skill` | Print this complete agent-oriented Markdown document. |

`--skill` is a top-level standalone invocation. It performs no dataset access
and no network update check.

## Exit codes

| Code | Meaning |
|---|---|
| `0` | Success. |
| `1` | Unexpected processing or I/O error. |
| `2` | Invalid input, unresolved dataset, or undetected product. |
| `3` | Render output or viewport could not be produced. |
| `4` | Recognized operation or dataset capability is not supported. |
| `5` | Dataset schema is non-conforming. |
| `6` | Validation findings failed the selected validation policy. |
