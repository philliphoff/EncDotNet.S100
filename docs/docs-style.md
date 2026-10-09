# Documentation style guide

This guide covers how to write the EncDotNet.S100 developer documentation: the
pages under `docs/`, the per-project `README.md` files, and sample READMEs. It
doesn't cover the [soundcharts.app](https://soundcharts.app) landing page in
`site/`, which has a different job.

[Getting started](getting-started.md) and the [documentation home](index.md)
are the reference examples. When this guide doesn't answer a question, do what
they do.

## Principles

1. **Write for someone with a task.** Readers arrive wanting to install,
   open, read, render or validate something. Tell them how, in the order
   they'll do it.
2. **Describe; don't promote.** Say what a feature does and when to use it.
   Don't say how good, fast, easy or complete it is. Persuasion belongs on the
   landing page.
3. **Everything you show must work.** Commands run in the order given, code
   compiles against the current API, and UI labels match the app.
4. **Say it once.** Each fact has one home. Other pages link to it instead of
   restating it.

## Page types

Every page is one of these types. Choose the type before you write, and use its
structure.

### Quickstart

Gets a new reader from nothing to one working result. Example:
[Getting started](getting-started.md).

1. One or two sentences on what the reader will do.
2. **Prerequisites**: what to install or have first. Omit if there are none.
3. Numbered or titled steps that run in order. Show the expected result after
   the step that produces it.
4. **Next steps**: two to five links, each with a few words on what it covers.

Leave options, variations and background to the guides. A quickstart takes
the shortest path that works.

### How-to guide

Solves one specific task for a reader who already has the basics. Example:
the [scenario guides](scenarios/render-s102-to-png.md).

- Title it with the task, as an imperative: "Render S-102 to PNG".
- Start with a sentence on what the result is and when you'd want it.
- Prerequisites, then numbered steps, then the result.
- Put alternatives and edge cases at the end, under headings that name them.

### Guide

Explains one area of the library in depth, with examples. Example:
[Loading datasets](loading-datasets.md).

- Open with a short paragraph on what the area covers and which types are the
  entry points.
- Organize sections around what the reader wants to do, such as "Open a
  folder" or "Apply S-101 updates". Don't organize around the type hierarchy.
- Each section gives a short explanation, a code example, and any caveats.

### Reference

Lists everything in a surface area, for lookup. Examples:
[Command-line rendering](cli.md) and [Top APIs](top-apis.md).

- Make it complete and consistently ordered. Tables suit options, commands
  and exit codes.
- Keep explanation short and link to guides for anything longer.

### Design note

Records why a shipped subsystem works the way it does, for contributors.
Lives in `docs/design/`. Example:
[S-98 interoperability](design/s98-interoperability.md).

- Cover the problem, the constraints, the decision and the alternatives you
  rejected.
- Design notes may discuss internals; the other page types shouldn't.

### Project README

Describes one package in `src/<project>/README.md`.

- First paragraph: what the package does and when to reference it.
- Then: install, a minimal example, the main entry points, and links to the
  relevant guides.

## Headings

- Use one `#` heading per page, matching the `toc.yml` entry.
- Use sentence case: "Read and render a dataset", not "Read And Render A
  Dataset".
- Make headings describe their content. Task sections use imperatives
  ("Install", "Open a dataset"); concept sections use nouns ("Supported
  products").
- Don't skip levels (`##` then `####`).
- Don't use template headings that describe the reader's journey instead of
  the content: "Why it matters", "Quick win", "Deep dive", "Overview" as a
  catch-all, or a closing "Troubleshooting" section that holds general tips.
  Put a real known problem next to the step it affects.
- Heading text becomes the link anchor (`## .NET library` becomes
  `#net-library`). Other pages and the repository README link to anchors, so
  search for links before you rename a heading. When a page repeats a heading
  such as "Next steps", only the first copy gets an anchor.

## Voice and tone

- Address the reader as "you". Use present tense and active voice: "The
  reader detects the product", not "The product will be detected".
- Write steps as imperatives: "Run", "Open", "Replace".
- Use contractions ("don't", "isn't", "you'll"). They read as plain speech.
- Keep sentences short. One idea per sentence; one topic per paragraph.
- Don't use exclamation marks, emoji or badges.
- Don't claim speed or ease ("in minutes", "a few lines", "simply", "just",
  "easy"). If something is short, the reader will see that.
- Avoid marketing and filler words, including: *seamless*, *powerful*,
  *robust*, *blazing*, *best-in-class*, *world-class*, *cutting-edge*,
  *batteries-included*, *out of the box*, *on-ramp*, *leverage*, *unlock*,
  *get to value*, *deep dive*.
- Don't use internal language like issue numbers, sprint names, chunk
  letters or "grow-up story" in reader-facing text. Issue links are fine in
  design notes and in caveats that point to a tracked limitation.

## Names and terms

| Write | Not |
|---|---|
| SoundCharts (the desktop app) | the viewer app, S-100 Viewer |
| `s100` (the command-line tool) | the CLI app, S100 |
| EncDotNet.S100 (the project and libraries) | EncDotNet, the engine |
| `EncDotNet.S100` (the facade package, in code style) | the main package |
| S-101, S-102, S-100 Part 10b | S101, S 101, part 10B |
| exchange set, dataset, feature catalogue, portrayal catalogue | exchangeset, data set, FC/PC (spell out on first use per page) |
| macOS, Windows, Linux | Mac OS, OSX |

"Viewer" on its own is fine once you've named SoundCharts on the page.

## Steps and code

- Use a numbered list for a sequence of actions in one place. Use `###`
  headings for steps that each need a code block or more than a short
  paragraph.
- Use one action per step. After a step with a visible result, say what the
  reader should see.
- Tag every code fence with its language: `bash`, `powershell`, `csharp`,
  `xml`, `json`, `text` for output.
- Use one command per line and no `$` prompt. Keep command output in a
  separate `text` block.
- Use placeholders a reader can recognize and replace: `path/to/dataset.000`,
  `<version>`. Don't use fake values that look real.
- C# examples must compile against the current API. Copy them from a sample or
  test where possible, and keep them as short as the point allows. Show the
  `using` directives the example needs.
- Don't show APIs that aren't public, or that need setup the page doesn't
  explain.

## UI text

- Bold UI labels and copy them exactly from
  `src/EncDotNet.S100.Viewer/Resources/Strings.resx`, including ellipses:
  **Open Dataset...**.
- Write menu paths as **File** > **Open Dataset...**.
- Describe the action, not the widget: "Choose **File** > **Open
  Dataset...**", not "Click the Open Dataset menu item in the File menu".

## Links

- Use relative links between pages and to project READMEs, so DocFX resolves
  them: `[Loading datasets](loading-datasets.md)`.
- Use an absolute GitHub URL for repository files that aren't pages, such as
  source files, `LICENSE` and folders.
- Make link text say where the link goes: "see [Command-line
  rendering](cli.md)", not "see [here](cli.md)".
- Link to a page's own topic once, where the reader needs it. Don't repeat the
  same set of links at the top and bottom of a page.

## Alerts, tables and images

- Use DocFX alerts (`> [!NOTE]`, `[!TIP]`, `[!IMPORTANT]`, `[!WARNING]`,
  `[!CAUTION]`) only for information the reader must not miss, placed next to
  the step it affects. Don't open a page with one or use one for decoration.
- Use a table to compare items across the same attributes. Use a list for
  everything else.
- Add a screenshot only when it shows something text can't, such as a
  rendering result or a UI that is hard to describe. Give it alt text that
  says what it shows. Product screenshots for promotion belong on the landing
  page.
- Use Mermaid diagrams for flows and relationships. Keep them to one idea.

## Check your page

Before you open a pull request:

1. Build the site the way CI does, which fails on broken links and anchors:

   ```bash
   docfx docfx.json --warningsAsErrors
   ```

2. Preview the output in `_site/` and read the page from top to bottom as a
   new reader.
3. Run each command and code example, or confirm it against a sample or test
   that does.
4. Check UI labels against `Strings.resx` and API names against the source.
5. Search the page for the words in [Voice and tone](#voice-and-tone).
