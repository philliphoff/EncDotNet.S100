# EncDotNet.S100.Cli (`s100`)

This project builds `s100`, the cross-platform command-line tool for S-100
datasets. `s100` renders datasets and exchange sets to images and web-map
tile sets, and inspects,
identifies, validates, converts and serves them. It uses the same portrayal and
validation code as the libraries and SoundCharts, with a headless Skia
renderer, so it runs in scripts and CI without a UI.

To install `s100` and for every command and option, see
[Command-line rendering](../../docs/cli.md). This page covers building the tool
and working on it.

## Build and run from source

You need the .NET 10 SDK. From the repository root, run any command through
`dotnet run`, with the `s100` arguments after `--`:

```bash
dotnet run --project tools/EncDotNet.S100.Cli -- list-specs
dotnet run --project tools/EncDotNet.S100.Cli -- render path/to/dataset.h5 out.png
```

To build once and run the output directly:

```bash
dotnet build tools/EncDotNet.S100.Cli -c Release
dotnet tools/EncDotNet.S100.Cli/bin/Release/net10.0/s100.dll list-specs
```

A source build reports its version as `0.0.0-dev` and doesn't check for
updates.

## How it's distributed

The assembly is named `s100`. The project isn't packed as a `dotnet tool`,
because a packed tool doesn't reliably lay out SkiaSharp's native libraries.
Instead, CI publishes it self-contained for each runtime identifier:

- As the standalone `s100-<version>-<rid>` archives attached to each GitHub
  release.
- Into a `cli/` folder inside the SoundCharts download. On macOS that's
  `SoundCharts.app/Contents/MacOS/cli/s100`, signed and notarized with the app.

On Linux the project ships SkiaSharp's self-contained `NoDependencies` native
library (the `UseSkiaSharpLinuxNoDependencies` property), so `render` draws
text with a font embedded in the renderer when fontconfig and system fonts
aren't installed. Earlier releases bundled the regular native library, which
failed to load without `libfontconfig.so.1` on `linux-x64` and failed with
`undefined symbol: uuid_parse` on `linux-arm64`.

Release builds check the latest GitHub release at most once every 24 hours and
cache the result in the local application-data folder. See
[Update notices](../../docs/cli.md#update-notices) for what the user sees.

## Project layout

| Path | What it holds |
|---|---|
| [`Commands/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tools/EncDotNet.S100.Cli/Commands) | One Spectre.Console.Cli command class for each command, with its settings. |
| [`Infrastructure/CliApp.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tools/EncDotNet.S100.Cli/Infrastructure/CliApp.cs) | Registers each command with its description and examples. |
| [`Infrastructure/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tools/EncDotNet.S100.Cli/Infrastructure) | Shared code: input resolution for datasets, layers and exchange sets, the headless MCP session, feed publishing, the tile containers (`Tiles/`) and the update check. |
| [`Skill/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tools/EncDotNet.S100.Cli/Skill) | Guidance that `s100 --skill` adds to the generated command reference. |
| [`tests/EncDotNet.S100.Cli.Tests`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/EncDotNet.S100.Cli.Tests) | Tests for every command. |

## Add or change a command

1. Add or edit the command class in `Commands/`. Give every option a
   `[Description]`; it appears in `--help` and in the skill document.
2. Register a new command in `Infrastructure/CliApp.cs` with
   `WithDescription` and at least one `WithExample`.
3. If the command needs guidance beyond its options, such as workflows or
   output formats, add `Skill/Commands/<Command>.md` and list it in
   `Infrastructure/SkillContent.cs`.
4. Add tests in `tests/EncDotNet.S100.Cli.Tests`. `SkillOutputTests` checks
   that the skill document covers every command and option.
5. Update [Command-line rendering](../../docs/cli.md), including its command
   table and exit codes if they change.

When you write a command:

- Use the shared exit codes listed in
  [Exit codes](../../docs/cli.md#exit-codes).
- In `mcp serve`, write every human-readable message to standard error,
  because standard output carries the MCP protocol.
