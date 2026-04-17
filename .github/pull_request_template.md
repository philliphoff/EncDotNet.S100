<!-- Thank you for contributing to EncDotNet.S100! -->

## Summary

<!-- What does this PR change and why? -->

## Spec alignment

Check each spec this PR touches and confirm the relevant skill was
consulted (`.github/skills/<spec>/SKILL.md`):

- [ ] S-100 framework (`s100-framework`)
- [ ] S-101 ENC (`s101-enc`)
- [ ] S-102 bathymetry (`s102-bathymetry`)
- [ ] S-104 water level (`s104-water-level`)
- [ ] S-111 surface currents (`s111-surface-currents`)
- [ ] S-124 navigational warnings (`s124-nav-warnings`)
- [ ] S-129 under keel clearance (`s129-ukc`)
- [ ] N/A — change is purely infrastructural (build, CI, docs, tooling)

**Spec section references cited in code/docs:**

<!-- e.g. "S-102 §10.2.3 for BathymetryCoverage attribute names" -->

## Tests

- [ ] Added/updated xunit tests under `tests/`
- [ ] Tests requiring real data files use `SkippableFact`
- [ ] `dotnet test --configuration Release` passes locally

## Documentation

- [ ] Updated the affected project's `src/<project>/README.md`
- [ ] Updated conceptual docs under `docs/` if user-facing behaviour
      changed
- [ ] New public APIs have XML doc comments

## Dependencies

- [ ] No new NuGet dependencies, OR versions added to
      `Directory.Packages.props` (not in the `.csproj`)
- [ ] `gh-advisory-database` security check run for any new dependency

## Breaking changes

<!-- List any binary or behavioural breaking changes, or write "None". -->
