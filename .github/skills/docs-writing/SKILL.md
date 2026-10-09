---
name: docs-writing
description: |
  Writing and reviewing the EncDotNet.S100 developer documentation —
  pages under docs/, per-project src/*/README.md files and sample
  READMEs — to the repository's documentation style guide
  (docs/docs-style.md): page types and their section structure, plain
  task-focused tone with no marketing language, runnable steps and code,
  exact UI labels, DocFX links/alerts, and the docfx build check.
  USE FOR: adding or rewriting a docs page or README; restructuring
  sections; reviewing a docs change for structure, tone or accuracy;
  converting pages that still use the old "Why it matters / Quick win /
  Deep dive" template. DO NOT USE FOR: the soundcharts.app landing page
  in site/ (marketing copy is allowed there); XML doc comments (follow
  docs/coding-style.md); viewer UI strings (see viewer.instructions.md).
---

# Writing EncDotNet.S100 docs

The normative rules are in
[`docs/docs-style.md`](../../../docs/docs-style.md). Read it in full before
writing. This skill adds the working procedure and a review checklist.

The reference examples are
[`docs/getting-started.md`](../../../docs/getting-started.md) (quickstart) and
[`docs/index.md`](../../../docs/index.md) (docs home). When unsure, match
them.

## Procedure

1. **Pick the page type** (quickstart, how-to guide, guide, reference, design
   note, project README) and use its structure from the style guide. If a page
   mixes types, split it or move material to the page that owns it.
2. **Gather facts from the code, not from other docs.** Other pages may be
   stale.
   - API names and signatures: the source under `src/`, and a sample or test
     that calls them (`samples/`, `tests/`).
   - Viewer UI labels: `src/EncDotNet.S100.Viewer/Resources/Strings.resx`.
     Menu commands are built in the viewer's `NativeMenuBuilder`.
   - `s100` commands and options: `s100 --skill`, or `tools/EncDotNet.S100.Cli`.
   - Release asset names and platforms: `.github/workflows/release.yml`.
3. **Check inbound links before renaming or removing anything.** Search the
   repository for the file name and `#anchor`, including `README.md`,
   `docs/toc.yml`, `src/toc.yml` and `site/src/data/content.ts` (the landing
   page links into the docs).
4. **Write**, following the style guide. Prefer linking to an existing page
   over restating it.
5. **Review** with the checklist below.
6. **Build and preview** the site; see
   [Check your page](../../../docs/docs-style.md#check-your-page). Fix every
   warning; CI runs with `--warningsAsErrors`.

## Review checklist

- [ ] The page has one `#` title that matches its `toc.yml` entry.
- [ ] Sections follow the structure for the page type.
- [ ] No template headings: "Why it matters", "Quick win", "Deep dive", or a
      catch-all "Troubleshooting".
- [ ] No marketing or filler words, emoji, badges or time claims. Quick scan:

      ```bash
      grep -n -i -E "seamless|powerful|robust|blazing|best-in-class|world-class|cutting-edge|batteries-included|out of the box|on-ramp|leverage|unlock|in minutes|simply|easily|deep dive|quick win|why it matters" path/to/page.md
      ```

- [ ] Steps run in the order given. Nothing is used before the step that
      installs or creates it.
- [ ] Code fences have a language tag. Commands have no `$` prompt, and
      output is in a separate block.
- [ ] C# examples compile against the current public API.
- [ ] UI labels are bold and match `Strings.resx` exactly.
- [ ] Links are relative for pages and absolute GitHub URLs for non-page
      files. Link text names the target.
- [ ] Alerts are only used for must-not-miss information, next to the step
      they affect.
- [ ] Renamed headings or removed pages have no remaining inbound links.
- [ ] `docfx docfx.json --warningsAsErrors` succeeds.
