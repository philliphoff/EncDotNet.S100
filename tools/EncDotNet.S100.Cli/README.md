# EncDotNet.S100.Cli (`s100`)

This project builds `s100`, the cross-platform command-line tool for S-100
datasets. `s100` renders datasets and exchange sets to images and web-map
tile sets, and inspects,
identifies, validates, converts and serves them, and checks routes against them. It uses the same portrayal and
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
- As the `ghcr.io/philliphoff/s100` container image for `linux/amd64` and
  `linux/arm64`. The [Container workflow](https://github.com/philliphoff/EncDotNet.S100/blob/main/.github/workflows/container.yml)
  copies the `linux-x64` and `linux-arm64` archives into
  [`docker/Dockerfile`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tools/EncDotNet.S100.Cli/docker/Dockerfile)'s
  chiseled Ubuntu image, so the image runs the release's own binaries. CI
  builds and smoke-tests it on every run, releases push the version tags and
  `latest`, and nightlies push `nightly`. To build it from your working tree,
  run `docker/build-local.sh`. See [Run s100 in Docker](../../docs/docker.md).

On Linux the project ships SkiaSharp's self-contained `NoDependencies` native
library (the `UseSkiaSharpLinuxNoDependencies` property), so `render` draws
text with a font embedded in the renderer when fontconfig and system fonts
aren't installed. Earlier releases bundled the regular native library, which
failed to load without `libfontconfig.so.1` on `linux-x64` and failed with
`undefined symbol: uuid_parse` on `linux-arm64`.

`s100 tiles export` writes MBTiles and GeoPackage through `Microsoft.Data.Sqlite`, so each archive
also carries SQLite's native library for its runtime (`libe_sqlite3`, about
1.5 to 2 MB). Only the CLI references SQLite; SoundCharts doesn't, and
`ViewerSqliteDecouplingTests` keeps it that way.

Release builds check the latest GitHub release at most once every 24 hours and
cache the result in the local application-data folder, unless
`S100_NO_UPDATE_CHECK` is set, as the container image does. See
[Update notices](../../docs/cli.md#update-notices) for what the user sees.

## Project layout

| Path | What it holds |
|---|---|
| [`Commands/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tools/EncDotNet.S100.Cli/Commands) | One Spectre.Console.Cli command class for each command, with its settings. |
| [`Infrastructure/CliApp.cs`](https://github.com/philliphoff/EncDotNet.S100/blob/main/tools/EncDotNet.S100.Cli/Infrastructure/CliApp.cs) | Registers each command with its description and examples. |
| [`Infrastructure/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tools/EncDotNet.S100.Cli/Infrastructure) | Shared code: input resolution for datasets, layers and exchange sets, the headless MCP session, feed publishing, the tile containers, readers and server (`Tiles/`), the host and token rules the serving commands share and the update check. |
| [`Skill/`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tools/EncDotNet.S100.Cli/Skill) | Guidance that `s100 --skill` adds to the generated command reference. |
| [`tests/EncDotNet.S100.Cli.Tests`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/EncDotNet.S100.Cli.Tests) | Tests for every command. |

## Maintainer commands

### `reference probe`

`s100 reference probe --spec S-101` runs each feature type's portrayal rule
over the values it reads and writes what each combination draws, and which
conditions held, to
`src/EncDotNet.S100.Specifications/content/S101/pc/rule-probes.json`. The
chart reference uses the file for its variant tables (symbol dictionary
design note §7, R13a). Commit the file.

Run it from the repository whenever a bundled S-101 feature or portrayal
catalogue changes, not on every build:

```bash
dotnet run --project tools/EncDotNet.S100.Cli -c Release -- reference probe --spec S-101
```

- **Keyed by hashes.** The file records the content hashes of the feature
  and portrayal catalogues it was probed from. `RuleProberTests` fails when
  they don't match the bundled catalogues, and tells you to re-run the
  command. SoundCharts reads the file only when they match; for a catalogue
  the user adds or changes, it probes the rules in the background and caches
  the results under that catalogue's hashes.
- **What's probed.** Each feature type's rule runs on synthetic features in
  a LuaCSharp trace session, whichever engine draws the map. Enumerations
  take the values the rule's conditions test, plus no value; numbers take
  values at and either side of each threshold and of the display settings the
  rule reads; display settings the rule branches on take `true` and `false`.
  At most 512 combinations per feature type (`--cap`): past that, the inputs
  tested least lose values first, and the feature type is marked partial.
- **Output.** Compact JSON, one feature type per line, the same bytes for the
  same catalogues. For S-101 it takes about 5 seconds on 4 cores and writes
  about 240 KB.

| Option | Default | What it does |
|---|---|---|
| `--spec <SPEC>` | `S-101` | The product whose bundled catalogues are probed. Only S-101 is supported; XSLT products aren't probed. |
| `-o`, `--output <FILE>` | the file next to the bundled catalogue | Writes somewhere else: `.json`, or `.json.gz` to compress. |
| `--cap <COUNT>` | `512` | The most combinations per feature type. |
| `--parallel <COUNT>` | the processor count | How many Lua sessions run at once. The output doesn't depend on it. |
| `--feature-type <CODE>` | every feature type | Probes only this feature type; repeat it for more. For trying the prober out. |

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
