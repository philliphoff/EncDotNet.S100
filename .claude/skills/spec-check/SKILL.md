---
name: spec-check
description: |
  Spec-alignment review to run BEFORE implementing a feature that touches
  S-100 product semantics. Identifies every affected spec, loads the
  matching skills, records per-spec constraints (section refs, API and
  encoding implications, fixtures, breaking-change risk), reconciles
  conflicts, and ends with an ordered implementation plan. USE FOR: "check
  this feature against the specs", planning a cross-spec change (e.g. a
  CoveragePipeline change affecting S-102/S-104/S-111), or any new
  product/edition support. DO NOT USE FOR: pure refactors, build/CI, docs,
  or UI-only changes with no spec semantics.
disable-model-invocation: true
---

# New feature — spec alignment check

You are reviewing a proposed feature **before any implementation work
begins**. The feature is: $ARGUMENTS

## Steps

1. **Identify affected specs.** From the feature description and any
   files/paths mentioned, list every spec (including the S-100 framework)
   that is touched. The skill routing table in the root `CLAUDE.md` maps
   signals to skills.
2. **Load the matching skills** for each affected spec. For any
   GML-encoded product also load `s100-gml-products`. For cross-spec
   features, load all of them.
3. For each spec, produce:
   - Spec section numbers that constrain the design (PDFs are in `docs/`
     and `docs/specs/`).
   - API shape implications (new types, changes to existing interfaces,
     which pipeline — coverage vs. vector), checked against
     `docs/design/api-conventions.md`.
   - HDF5/GML/ISO 8211 encoding details that must be honoured.
   - Test fixtures required (synthetic vs. real data skipped via
     `Assert.SkipUnless`).
   - Breaking-change risk vs. prior spec editions the repo supports.
4. **Reconcile** conflicts or overlaps between the specs' guidance.
5. Produce a **final implementation plan** with an ordered task list, and
   only then proceed (or hand off) to coding.

## Output format

```
## Affected specs
- <spec>: <one-line reason>

## Per-spec constraints
### <spec>
- Section refs:
- API implications:
- Encoding details:
- Tests:
- Breaking-change risk:

## Reconciliation notes
...

## Implementation plan
1. ...
2. ...
```

Do not write code until this review is complete and acknowledged.
