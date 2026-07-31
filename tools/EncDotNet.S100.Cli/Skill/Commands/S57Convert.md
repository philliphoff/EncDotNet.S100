#### Agent guidance

Pass an S-57 base cell (`.000`) and an explicit S-101 output path. By default,
sequential sibling updates are discovered and folded into the source state
before translation; use `--no-updates` only when the bare base edition is
required. Use `--report` to capture translation coverage and dropped or
unmapped content as JSON.
