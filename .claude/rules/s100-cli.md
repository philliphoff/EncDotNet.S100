---
paths:
  - "tools/EncDotNet.S100.Cli/**"
  - "tests/EncDotNet.S100.Cli.Tests/**"
---

# s100 CLI editing rules

Load the `s100-cli` skill before a non-trivial CLI change. It covers the
`s100 --skill` contract: Spectre's `ICommandModel` is the command reference,
authored guidance lives in `tools/EncDotNet.S100.Cli/Skill/`, and
`SkillContent`, root `--help`, the CLI README and `SkillOutputTests` stay in
sync. `s100 --skill` output stays deterministic Markdown (LF, no ANSI, no
dataset access, no update check).
