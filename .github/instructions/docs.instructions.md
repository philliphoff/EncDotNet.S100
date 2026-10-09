---
applyTo: "docs/**/*.md,src/**/README.md,samples/**/README.md"
---

# Documentation editing rules

When adding or editing developer documentation, follow
[`docs/docs-style.md`](../../docs/docs-style.md). For a new page or a
substantial rewrite, load the `docs-writing` skill. In particular:

- Choose the page type first (quickstart, how-to guide, guide, reference,
  design note or project README) and use its section structure.
- Describe what things do. Don't promote them: no marketing words, emoji,
  badges or claims about speed or ease. That copy belongs on the landing page
  in `site/`.
- Don't use the "Why it matters / Quick win / Deep dive / Troubleshooting /
  Next step" template headings.
- Every command and code example must work in the order shown, against the
  current API. Copy UI labels exactly from
  `src/EncDotNet.S100.Viewer/Resources/Strings.resx`.
- Build with `docfx docfx.json --warningsAsErrors` before opening a pull
  request.
