#### Agent guidance

Choose one input form: one positional dataset, repeated `--layer` values, or an
exchange set via `--from`/`--exchange-set` (also auto-detected positionally).
Always supply `--lat` and `--lon`.

Prefer `--format json`. Vector matches are ranked in ECDIS draw order; coverage
products are sampled at the same location. `--radius` affects point and curve
features, while area features use exact containment. Use `--attributes` only
when full feature payloads are needed because it increases output size.
