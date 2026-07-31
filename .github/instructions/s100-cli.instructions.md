---
applyTo: "tools/EncDotNet.S100.Cli/**,tests/EncDotNet.S100.Cli.Tests/**,.github/skills/s100-cli/**,.github/instructions/s100-cli.instructions.md"
---

# s100 CLI editing rules

When modifying the `s100` CLI or its agent guidance:

- Load the `s100-cli` skill before proposing non-trivial changes.
- Run `s100 --skill` (or the source invocation documented by that skill)
  before choosing commands or relying on CLI behavior.
- Keep Spectre as the authoritative command hierarchy. Generate factual
  command reference from `ICommandModel`; do not create a parallel command
  catalogue.
- Keep authored agent guidance under
  `tools/EncDotNet.S100.Cli/Skill/`. Update it whenever workflows, option
  interactions, output contracts, limitations, or exit codes change.
- Keep command descriptions, examples, settings attributes, `SkillContent`
  mappings, root `--help`, the CLI README, and `SkillOutputTests` synchronized.
- `s100 --skill` must remain deterministic plain Markdown on standard output,
  with LF line endings, no ANSI sequences, no dataset access, and no automatic
  update check.
- Use the matching product skill too when CLI behavior depends on S-10x
  product semantics.
