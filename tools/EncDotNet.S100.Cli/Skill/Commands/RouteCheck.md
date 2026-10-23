#### Agent guidance

Pass the route first (RTZ 1.0–1.2 or S-421 GML), then the datasets to check it
against: dataset files, one exchange set and/or one folder. Prefer
`--format json`: `check` is the `check_route` MCP result, with the verdict, each
check's status, the findings and the clearance.

Set `--safety-contour` and `--safety-depth` to the mariner's values; the
defaults (30 m) flag most coastal water. The UKC check runs only when every leg
has a draft (from the file or `--draft`); `check.checks` says when it was
skipped and why. Time-dependent checks (S-124
warnings, seasonal areas, S-104 water level) use the departure and speeds, so
pass `--departure` and `--speed` when the file has no schedule.

Exit code 7 means findings at or above `--fail-on` (default `must-review`);
treat it as a result, not an error. Results are not for navigation.
