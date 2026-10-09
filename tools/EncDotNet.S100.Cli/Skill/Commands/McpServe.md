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
  headless Skia session — no GUI. `open_dataset` accepts the same file,
  exchange-set and folder paths as the up-front form and adds them to the live
  catalog; and
- the Library tools, the same as the viewer's: `list_known_sources`,
  `list_library_sources`, `query_library_items`, `describe_library_item`,
  `add_library_source`, `refresh_library_source`, `library_action`,
  `remove_library_source`, `set_library_source_options`,
  `await_library_idle`, `list_secom_services` and `set_secom_identity`. Library
  items are opened into the same catalog, so the query tools see them.

Datasets to open up front: one positional dataset, exchange set or plain folder
of datasets (searched recursively), repeated `--layer` values, and an exchange
set via `--from`/`--exchange-set`, in any combination. None are required: the
server can start empty and open datasets or Library items later. The process is
the session — spawn another `serve` process to serve a different set.

Library sources to add at startup: repeat `--collection` with a local path
(folder, exchange set, `*.s100collection.json` manifest or S-128 catalogue), a
known-source id (as `list_known_sources` lists them) or a catalogue URL. Append
`#choice,choice` to include only some of a catalogue, for example
`--collection noaa-s111#sfbofs`. A source the Library already has is skipped,
so the same command line can be reused with a kept data directory.

The Library keeps its index cache, catalogue cache and downloads in
`--data-dir`. Without it a temporary directory is used and removed on exit, so
nothing is kept between runs. `--collections <collections.json>` uses an
existing Library file, such as the viewer's, and saves changes to it.

A typical collection flow: `add_library_source` (or `--collection`) →
`query_library_items` → `library_action download` → `await_library_idle` →
`sample_coverage`.

A typical headless-validation flow: `set_palette` (or `set_time_step`) →
`render_to_image` → inspect the returned PNG.

Configure it as an MCP server with `command: "s100"` and
`args: ["mcp", "serve", "<dataset-or-exchange-set>"]`. Standard output carries
the MCP protocol, so **do not** parse it as text; startup notices, load
warnings, and errors go to standard error. The server runs until the client
disconnects (stdin end-of-file) or it is interrupted. Session tools change only
in-memory state (palette, time step, presentation); the Library writes only to
its data directory (and the `--collections` file), and the server never edits
the source datasets.

Known v1 gaps: the viewport auto-fits the loaded datasets (no `set_viewport`
yet), and `set_display_category` updates session state but the headless composite
render does not yet reflect ECDIS category / viewing-group selections.
