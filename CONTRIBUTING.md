# Contributing to EncDotNet.S100

Thanks for your interest in contributing! EncDotNet.S100 is a managed,
cross-platform implementation of the IHO S-100 Universal Hydrographic Data
Model for .NET. This guide covers everything you need to build, test, and
submit changes.

By participating you agree to abide by our
[Code of Conduct](CODE_OF_CONDUCT.md).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/) or later.
- A git client.
- No native dependencies are required — HDF5 (PureHDF) and Lua (MoonSharp)
  are fully managed, so everything runs on macOS, Windows, and Linux.

## Getting started

```bash
git clone https://github.com/philliphoff/EncDotNet.S100.git
cd EncDotNet.S100
dotnet build
```

## Building

```bash
dotnet build
```

The solution targets **.NET 10** with `<Nullable>enable</Nullable>` and
`<ImplicitUsings>enable</ImplicitUsings>` throughout. CI builds on
`ubuntu-latest`, so avoid platform-specific APIs unless they are gated to the
appropriate runtime identifier.

## Testing

```bash
dotnet test --configuration Release
```

Test conventions:

- Every new public API or bug fix must be accompanied by an xunit test in the
  appropriate project under `tests/`.
- Tests that require optional external data files (real HDF5, S-101 datasets,
  etc.) must use [`Xunit.SkippableFact`](https://github.com/AArnott/Xunit.SkippableFact)
  via `Skip.If(...)` so CI does not fail when those files are absent.
- **Never commit real ENC data files to the repository.** Use small synthetic
  test fixtures, or skip tests that require live data. Synthetic GML fixtures
  live under `tests/datasets/<SXXX>/`.

## Package management

All NuGet versions are managed centrally via **Central Package Management** in
`Directory.Packages.props`:

- Do **not** add `Version` attributes to individual `.csproj` files.
- Add or update versions in `Directory.Packages.props` instead.
- Before introducing any new dependency, run the `gh-advisory-database`
  security check and confirm the package is free of known advisories.

## Spec routing (skills & instructions)

This repository is organized around individual IHO S-100 product
specifications. Per-spec **skills** live under `.github/skills/<spec>/SKILL.md`
and matching **instructions** under `.github/instructions/`. Before designing
or implementing any non-trivial change that touches a spec's semantics
(encoding, attribute names, feature-catalogue rules, portrayal pipelines),
consult the matching skill/instruction file:

| Area | Skill / instruction |
|---|---|
| S-100 framework, exchange sets, portrayal engine | `s100-framework` |
| S-101 ENC, ISO 8211, Lua portrayal | `s101-enc` |
| S-102 bathymetry | `s102-bathymetry` |
| S-104 water level | `s104-water-level` |
| S-111 surface currents | `s111-surface-currents` |
| S-122 marine protected areas | `s122-marine-protected-areas` |
| S-124 navigational warnings | `s124-nav-warnings` |
| S-125 marine aids to navigation | `s125-aton` |
| S-127 marine resources and services | `s127-marine-services` |
| S-128 catalogue of nautical products | `s128-catalogue` |
| S-129 under keel clearance | `s129-ukc` |
| S-131 marine harbour infrastructure | `s131-marine-harbour` |
| S-201 IALA aids to navigation information | `s201-aton-information` |
| S-411 sea ice | `s411-sea-ice` |
| S-421 route plans | `s421-route-plans` |

For cross-spec changes (e.g. a change to `CoveragePipeline` affecting
S-102/S-104/S-111), reconcile the guidance from all affected specs before
writing code. Cite the relevant spec section number(s) in PR descriptions and
in XML doc comments for spec-derived constants, enums, attribute names, and
group paths.

## Coding style

The normative style rules live in the
[C# coding style guide](docs/coding-style.md) — read it before submitting
code. In brief:

- `PascalCase` for types/methods/properties, `camelCase` for locals and
  parameters, `_camelCase` for private fields.
- File-scoped namespaces, 4-space indents, Allman braces.
- Nullable reference types are enabled everywhere — avoid `!` suppression;
  prefer null-checks or `ArgumentNullException.ThrowIfNull`.
- All public APIs must carry XML doc comments (`<summary>`, `<param>`,
  `<returns>`).
- Only comment code that genuinely needs clarification.

For API-shape conventions (collection return types, `class` vs `record`,
quantity types) see [API design conventions](docs/design/api-conventions.md).

## Documentation

- Each library has a `README.md` in its `src/<project>/` directory. Update it
  when adding types, removing APIs, or changing behaviour.
- Conceptual guides live under `docs/` in DocFX Markdown. Add or update pages
  there for user-facing features.
- When editing the viewer (`src/EncDotNet.S100.Viewer/**`), follow the
  localization and UI rules in `.github/instructions/viewer.instructions.md`
  (every user-facing string lives in `Resources/Strings.resx`).

## Release signing

The `publish` job in [`.github/workflows/ci.yml`](.github/workflows/ci.yml)
code-signs the desktop viewer and the standalone `s100` CLI for macOS and
Windows. Both are **gated to non-PR runs** (pushes to `main` and `v*` tags) and
are skipped automatically when the required secrets are absent, so forks and
pull requests build unsigned artifacts without failing.

### macOS (Developer ID + notarization)

Signing/notarization runs when `APPLE_DEVELOPER_CERTIFICATE_P12` is present.
Required repository **secrets**: `APPLE_DEVELOPER_CERTIFICATE_P12`,
`APPLE_DEVELOPER_CERTIFICATE_PASSWORD`, `APPLE_SIGNING_IDENTITY`, `APPLE_ID`,
`APPLE_TEAM_ID`, `APPLE_APP_PASSWORD`.

### Windows (Azure Trusted Signing)

The Windows `.exe`s are Authenticode-signed with
[Azure Trusted Signing](https://azure.microsoft.com/products/trusted-signing)
via the [`azure/trusted-signing-action`](https://github.com/Azure/trusted-signing-action).
Signing runs when `AZURE_CLIENT_ID` is present.

One-time Azure setup:

1. Create a **Trusted Signing Account** and a **Public Trust** certificate
   profile, and complete Microsoft identity validation. ("Private Trust"
   profiles do **not** clear SmartScreen warnings.)
2. Register a single-tenant **App registration** (its service principal is the
   CI identity) and add a **client secret**.
3. On the Trusted Signing Account's **Access control (IAM)**, assign the app
   the **Trusted Signing Certificate Profile Signer** role. Without this the
   signing step fails with `403 Forbidden`.

Required repository **secrets** (from the app registration):

| Secret | Source |
|---|---|
| `AZURE_TENANT_ID` | App registration → Overview → Directory (tenant) ID |
| `AZURE_CLIENT_ID` | App registration → Overview → Application (client) ID |
| `AZURE_CLIENT_SECRET` | App registration → Certificates & secrets → secret **Value** |

Required repository **variables** (non-secret, account-specific, case-sensitive):

| Variable | Source |
|---|---|
| `AZURE_SIGNING_ENDPOINT` | Trusted Signing Account → Overview → Account URI (e.g. `https://wus2.codesigning.azure.net/`) |
| `AZURE_SIGNING_ACCOUNT_NAME` | Trusted Signing Account name |
| `AZURE_SIGNING_PROFILE_NAME` | Certificate profile name |

The workflow signs every `.exe` under the viewer publish folder (covering both
the viewer and the bundled `cli/s100.exe`) before archiving, then a "Verify
Windows signatures" step asserts `Get-AuthenticodeSignature` returns `Valid`.
The identity is OV-level, so SmartScreen reputation accrues over downloads
rather than instantly.

## Branch & pull-request workflow

1. Create a topic branch off `main` (e.g. `fix-s104-trend-flag` or
   `feature/s127-pilot-boarding`).
2. Make focused, surgical changes that fully address one concern.
3. Ensure `dotnet build` and `dotnet test --configuration Release` pass
   locally.
4. Open a pull request against `main`. The
   [pull request template](.github/pull_request_template.md) includes a
   checklist for spec alignment, tests, documentation, dependencies, and
   breaking changes — please fill it out.
5. CI must pass before review. Keep PRs small and well-scoped where possible.

## Reporting bugs & requesting features

Use the issue templates under
[`.github/ISSUE_TEMPLATE/`](.github/ISSUE_TEMPLATE/). For questions and
general discussion, see [SUPPORT.md](SUPPORT.md). To report a security
vulnerability, follow [SECURITY.md](SECURITY.md) — please do **not** open a
public issue for security problems.

## License

By contributing, you agree that your contributions will be licensed under the
[MIT License](LICENSE) that covers this project.
