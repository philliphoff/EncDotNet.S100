#### Agent guidance

Maintainer tooling for a tide station mirror, such as the scheduled
`tide-stations` workflow; not needed to use tides. Give it the catalogue to
publish and the folder holding the mirror's current files. It refuses (exit
code 9) a catalogue that is undated, older than the published one, from
another provider, or that lost more than `--max-drop` percent of its
stations or predictable stations. When the stations are the same as the
published ones it keeps the published file, so `retrieved` in the manifest
moves only when the data changes. Upload `tide-stations.json.gz` before
`tide-stations.manifest.json`.
