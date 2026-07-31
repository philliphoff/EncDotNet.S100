#### Agent guidance

For a dataset, this command runs the product's available normative rule pack.
For an S-100 directory, `CATALOG.XML`, or ZIP it verifies exchange-set
integrity and signatures. For `CATALOG.031` it verifies S-57/S-63 integrity.

Prefer `--format json` when consuming findings programmatically. Exit code `6`
means retained findings failed the policy: errors normally fail, and warnings
also fail with `--strict`. `--suppress` removes matching rule IDs from both the
report and exit-code calculation; use it only for explicitly accepted findings.
