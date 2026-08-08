#### Agent guidance

`s100 mcp serve` turns this CLI into a Model Context Protocol server over the
**stdio** transport, so an MCP client that spawns this process can work with the
datasets without a GUI. It hosts:

- the read-only S-100 query tools (`list_datasets`, `describe_feature`,
  `describe_feature_type`, `query_features`, `find_at`, `identify_features`,
  `nearest_features`, `count_features`, `search_features`, `sample_coverage`,
  `sample_coverage_along`, `list_specs`, `list_time_steps`); and
- the mutating session tools (**mutable by default**): `open_dataset`,
  `close_dataset`, `close_all_datasets`, `set_palette`, `set_display_category`,
  `set_display_mode`, `set_time_step`, and `render_to_image` (a headless PNG of
  the current state, returned as an MCP image block). These drive an in-process
  headless Skia session — no GUI. `open_dataset` accepts the same file /
  exchange-set paths as the up-front form and adds them to the live catalog.

Specify the datasets to serve up front, using the same input form as
`identify`: one positional dataset, repeated `--layer` values, or an exchange
set via `--from`/`--exchange-set` (also auto-detected positionally). The process
is the session — spawn another `serve` process to serve a different set.

A typical headless-validation flow: `set_palette` (or `set_time_step`) →
`render_to_image` → inspect the returned PNG.

Configure it as an MCP server with `command: "s100"` and
`args: ["mcp", "serve", "<dataset-or-exchange-set>"]`. Standard output carries
the MCP protocol, so **do not** parse it as text; startup notices, load
warnings, and errors go to standard error. The server runs until the client
disconnects (stdin end-of-file) or it is interrupted. Mutating tools change only
in-memory session state (palette, time step, presentation); the server never
writes files or edits the source datasets.

Known v1 gaps: the viewport auto-fits the loaded datasets (no `set_viewport`
yet), and `set_display_category` updates session state but the headless composite
render does not yet reflect ECDIS category / viewing-group selections.
