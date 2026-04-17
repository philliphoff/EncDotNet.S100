---
agent: agent
description: Run a spec-alignment review before implementing a feature.
---

# New feature — spec alignment check

You are reviewing a proposed feature for the EncDotNet.S100 repository
**before any implementation work begins**.

## Steps

1. **Identify affected specs.** From the feature description and any
   files/paths mentioned, list every S-10x spec (including S-100
   framework) that is touched.
2. **Load the matching skills** for each affected spec from
   `.github/skills/<spec>/SKILL.md`. For cross-spec features, load all.
3. For each spec, produce:
   - Spec section numbers that constrain the design.
   - API shape implications (new types, changes to existing interfaces,
     which pipeline — coverage vs. vector).
   - HDF5/GML/ISO 8211 encoding details that must be honored.
   - Test fixtures required (synthetic vs. real-data `SkippableFact`).
   - Breaking-change risk vs. prior spec editions supported by the repo.
4. **Reconcile** conflicts or overlaps between spec guidance (e.g. a
   `CoveragePipeline` change that has to satisfy S-102, S-104, and
   S-111 simultaneously).
5. Produce a **final implementation plan** with an ordered task list,
   and only then proceed (or hand off) to coding.

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
