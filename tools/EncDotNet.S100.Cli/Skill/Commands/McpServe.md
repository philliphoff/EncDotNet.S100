#### Agent guidance

`s100 mcp serve` turns this CLI into a Model Context Protocol server: it hosts
the read-only S-100 query tools (`list_datasets`, `describe_feature`,
`describe_feature_type`, `query_features`, `find_at`, `identify_features`,
`nearest_features`, `count_features`, `search_features`, `sample_coverage`,
`sample_coverage_along`, `list_specs`, `list_time_steps`) over the **stdio**
transport, so an MCP client that spawns this process can query the datasets
without a GUI.

Specify the datasets to serve up front, using the same input form as
`identify`: one positional dataset, repeated `--layer` values, or an exchange
set via `--from`/`--exchange-set` (also auto-detected positionally). They are
loaded once and served read-only; the process is the session — spawn another
`serve` process to serve a different set.

Configure it as an MCP server with `command: "s100"` and
`args: ["mcp", "serve", "<dataset-or-exchange-set>"]`. Standard output carries
the MCP protocol, so **do not** parse it as text; startup notices, load
warnings, and errors go to standard error. The server runs until the client
disconnects (stdin end-of-file) or it is interrupted. It never mutates data,
loads or unloads datasets, or writes files.
