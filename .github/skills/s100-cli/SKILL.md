---
name: s100-cli
description: |
  Operating and maintenance guidance for the `s100` command-line tool.
  USE FOR: choosing or invoking `s100` commands; discovering supported CLI
  capabilities, arguments, outputs, and exit codes; scripting dataset
  inspection, validation, identification, rendering, or S-57 conversion;
  changes to `tools/EncDotNet.S100.Cli/**` or its tests; maintaining the
  agent-oriented `s100 --skill` output. DO NOT USE FOR: driving the Avalonia
  viewer and its MCP server (use `viewer-evaluation`), or product semantics
  without a CLI concern (use the matching S-10x skill).
---

# s100 CLI

## Discover the installed command surface first

Before deciding which `s100` command or arguments to use, run:

```bash
s100 --skill
```

Treat that Markdown as the authoritative guide for the installed build. It
contains the complete command hierarchy, arguments, options, defaults,
examples, output guidance, limitations, and exit codes in one invocation.
Do not crawl progressive `--help` pages unless checking the human help
experience itself.

When working from source without an installed executable, use:

```bash
dotnet run \
  --project tools/EncDotNet.S100.Cli/EncDotNet.S100.Cli.csproj \
  --configuration Release \
  --framework net10.0 \
  -- --skill
```

Use the matching product skill as well when a task depends on S-101, S-102,
S-104, S-111, or another product specification's semantics.

## Maintain the generated skill document

The output is deliberately hybrid:

- Spectre's live `ICommandModel` supplies command paths, descriptions,
  examples, arguments, options, defaults, and required status.
- Embedded Markdown under `tools/EncDotNet.S100.Cli/Skill/` supplies
  agent-oriented workflows, cross-option relationships, output contracts,
  exit codes, and limitations that command metadata cannot express.
- `SkillContent` maps command paths to their authored guidance fragments.

When changing the CLI:

1. Keep Spectre command descriptions, examples, and settings attributes
   accurate; generated reference content follows them automatically.
2. Update `Skill/Overview.md` for global behavior, command selection, shared
   conventions, or exit-code changes.
3. Update the matching `Skill/Commands/*.md` fragment when command workflows,
   option interactions, output semantics, or limitations change.
4. Add or remove the `SkillContent` mapping when a command gains or loses
   authored guidance. Renaming a command requires changing its mapping key.
5. Keep `--skill` documented in root `--help` and in the CLI README.
6. Extend `SkillOutputTests` whenever the output contract changes.

Do not duplicate ordinary option reference tables in authored fragments. The
renderer owns those tables so they stay synchronized with Spectre.

## Review checklist

- Does `s100 --skill` remain a standalone, deterministic UTF-8 Markdown
  invocation with LF line endings and no ANSI sequences?
- Does it avoid dataset access and automatic update checks?
- Does every visible Spectre command, argument, and option appear?
- Do all authored guidance keys resolve to registered command paths?
- Does `s100 --help` advertise `--skill`?
- Does the README describe the flag?
