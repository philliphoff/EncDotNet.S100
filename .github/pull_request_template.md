<!-- Thank you for contributing to EncDotNet.S100! -->

## Summary

<!-- What does this PR change and why? -->

## Spec alignment

Check each area this PR touches and confirm the matching skill was
consulted (`.claude/skills/<skill>/SKILL.md`; see the table in `CLAUDE.md`):

- [ ] S-100 framework / GML products (`s100-framework`, `s100-gml-products`)
- [ ] Product specification(s): <!-- e.g. s101-enc, s104-water-level -->
- [ ] Cross-cutting (`marine-weather`, `s100-data-protection`,
      `s100-interoperability`, `route-check`, `chart-cartography`)
- [ ] N/A — change is purely infrastructural (build, CI, docs, tooling)

**Spec section references cited in code/docs:**

<!-- e.g. "S-102 §10.2.3 for BathymetryCoverage attribute names" -->

## Tests

- [ ] Added/updated xunit tests under `tests/`
- [ ] Tests requiring real data files skip via `Assert.SkipUnless`/`Assert.SkipWhen`
- [ ] `dotnet test --configuration Release` passes locally
- [ ] New or changed viewer UI has automation ids and accessible names (see
      `.claude/rules/viewer.md`, Accessibility and automation)

## Documentation

- [ ] Updated the affected project's `src/<project>/README.md`
- [ ] Updated conceptual docs under `docs/` if user-facing behaviour
      changed
- [ ] New public APIs have XML doc comments

## Dependencies

- [ ] No new NuGet dependencies, OR versions added to
      `Directory.Packages.props` (not in the `.csproj`)
- [ ] New dependencies checked for known advisories (`dotnet list package --vulnerable`)

## Breaking changes

<!-- List any binary or behavioural breaking changes, or write "None". -->
