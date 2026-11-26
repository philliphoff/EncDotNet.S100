# EncDotNet.S100

.NET libraries, an `s100` CLI and a cross-platform Avalonia desktop viewer
(SoundCharts) for reading, portraying and rendering
[IHO S-100](https://iho.int/en/s-100-edition-5-2-0) nautical chart data, plus
adjacent marine data (S-57, weather, AIS, tides, routes). Targets .NET 10 with
nullable reference types and implicit usings everywhere.

The project is in preview with no external API consumers: when an API is
messy, redesign it rather than wrapping it in a facade.

## Layout

- `src/EncDotNet.S100.Core` — abstractions (`IAssetSource`, `IHdf5File`,
  `ILuaEngine`), coverage and vector pipelines, shared GML parsing
  (`Core/Gml`), drawing instructions, interoperability.
- `src/EncDotNet.S100.{Features,Portrayals,ExchangeSets,Specifications}` —
  feature catalogues (ISO 19110 / Part 5), portrayal catalogues (Part 9),
  exchange sets and Part 15 protection, and the bundled official FCs/PCs
  under `Specifications/content/<SXXX>/`.
- `src/EncDotNet.S100.Datasets.<Product>` — one project per product reader
  (S-57, S-101, S-102, S-104, S-111, S-122 … S-421, plus Weather, Grib,
  NetCdf, Cap, Aml, Nis, TropicalCyclones). `Datasets.Pipelines` wires readers
  to pipelines.
- `src/EncDotNet.S100.Renderers.*`, `Rendering.{Engine,Scene}` — Skia and
  Mapsui renderers.
- `src/EncDotNet.S100.Viewer` — Avalonia viewer; `src/EncDotNet.S100.Mcp*`
  — MCP server and tools used by the viewer and CLI.
- `src/EncDotNet.S100.{Navigation,Routes.Rtz,Tides*,DynamicSources.*,Nmea}`
  — route checks, RTZ, tide prediction, AIS/NMEA/NDBC live sources.
- `tools/EncDotNet.S100.Cli` — the `s100` CLI; other `tools/` are dev
  utilities. `samples/` — runnable samples. `tests/` — one xunit project per
  library, plus `tests/datasets/` fixtures. `docs/` — DocFX docs, spec PDFs
  (also `docs/specs/`) and design notes (`docs/design/`). `site/` — landing
  page.

## Stack

C# / .NET 10, Avalonia 12, Mapsui 5, SkiaSharp 3 + Svg.Skia, PureHDF
(managed HDF5), Lua-CSharp (default Lua 5.2 engine) and MoonSharp
(selectable), `EncDotNet.Iso8211`, ProjNet, Spectre.Console.Cli,
ModelContextProtocol, xunit v3, DocFX. Package versions are centrally
managed in `Directory.Packages.props`.

## Build, test, format

```bash
dotnet build EncDotNet.S100.slnx
dotnet test EncDotNet.S100.slnx --configuration Release
```

Before finishing a change, normalize and then verify formatting. CI's
**Format check** job runs the last command and fails on drift:

```bash
dotnet format whitespace EncDotNet.S100.slnx
dotnet format style EncDotNet.S100.slnx --diagnostics IDE0005
dotnet format EncDotNet.S100.slnx --diagnostics IDE0005 IDE1006 --severity warn --verify-no-changes
```

CI runs on `ubuntu-latest`; gate platform-specific APIs to the right RID.

## Rules for every change

- **Style:** [`docs/coding-style.md`](docs/coding-style.md) is normative and
  encoded in `.editorconfig`, but `EnforceCodeStyleInBuild` is off, so the
  build will not catch most violations; apply the guide yourself. Follow
  [`docs/design/api-conventions.md`](docs/design/api-conventions.md) for API
  shape (collection return types, `class` vs `record`, quantity types).
- **Nullability:** avoid `!`; prefer null checks or
  `ArgumentNullException.ThrowIfNull`.
- **Docs comments:** every public API has XML docs. Cite the spec section
  (e.g. "S-102 §10.2.3") on spec-derived constants, enums, element/attribute
  names and HDF5 group paths, and in PR descriptions.
- **Tests:** every new public API or bug fix comes with an xunit test. Tests
  that need real external data use `Assert.SkipUnless` / `Assert.SkipWhen`
  so CI passes without it. Never commit real ENC or other licensed
  datasets; use small synthetic fixtures.
- **Docs:** update the project's `src/<project>/README.md` when its types or
  behaviour change, and the conceptual pages under `docs/` for user-facing
  features.
- **Dependencies:** add versions only to `Directory.Packages.props`, never
  to a `.csproj`. Check a new package for known vulnerabilities
  (`dotnet list package --vulnerable`) before adding it.
- **Spec assets:** FCs, PCs, Lua rules and symbols go under
  `src/EncDotNet.S100.Specifications/content/<SXXX>/` and are loaded through
  `Specification.*`. Upstream catalogues are kept byte-identical; adapt them
  with an `Adapter/` wrapper, never an in-place edit.

Folder-specific rules live in `.claude/rules/` (viewer UI, docs, CLI) and
load automatically when you work in those folders.

## Skills: load before touching spec semantics

Load the matching skill(s) **before** designing or implementing a
non-trivial change to a product's encoding, attribute names, catalogue rules
or portrayal. For a cross-spec change, load every affected skill and
reconcile them. To plan a feature against the specs first, run
`/spec-check <feature>`.

| Signal in the task or files touched | Skill |
|---|---|
| S-100 framework, Parts 1–10, exchange-set structure, catalogues, Lua engine, Core pipelines | `s100-framework` |
| Any GML-encoded product (S-122/124/125/127/128/129/131/201/411/412/421) | `s100-gml-products` + the product skill |
| S-101 ENC, S-401 Inland ENC, ISO 8211, Lua portrayal | `s101-enc` |
| S-57 ENC, S-57 ↔ S-101 conversion, NIS → S-57 | `s57-enc` |
| S-102 bathymetry | `s102-bathymetry` |
| S-104 water level | `s104-water-level` |
| S-111 surface currents | `s111-surface-currents` |
| S-122 marine protected areas | `s122-marine-protected-areas` |
| S-124 navigational warnings | `s124-nav-warnings` |
| S-125 marine AtoN (ECDIS) | `s125-aton` |
| S-127 marine services | `s127-marine-services` |
| S-128 catalogue of nautical products | `s128-catalogue` |
| S-129 under keel clearance, UKC fusion | `s129-ukc` |
| S-131 harbour infrastructure | `s131-marine-harbour` |
| S-201 IALA AtoN information (authority exchange, not ECDIS) | `s201-aton-information` |
| S-411 sea ice | `s411-sea-ice` |
| S-421 route plans | `s421-route-plans` |
| S-412, GRIB2, NetCDF, CAP warnings, tropical cyclones, wind/wave | `marine-weather` |
| Part 15 encryption, signatures, permits, trust roots, protected exchange sets | `s100-data-protection` |
| S-98 interoperability, display combinations, S-164 conformance | `s100-interoperability` |
| Route checks, findings, RTZ | `route-check` |
| Draw order, declutter, labels, palettes, CRS pitfalls, render performance | `chart-cartography` |
| Using or changing the `s100` CLI | `s100-cli` |
| Seeing or measuring a change in the running viewer (including renderer, Core and dataset work) | `viewer-evaluation` |
| Writing or reviewing `docs/**` and READMEs | `docs-writing` |

Areas without a skill: read the design note first. AIS/live sources:
`docs/design/ais-source.md`, `dynamic-feature-source.md`. AML:
`docs/design/aml/`. Tides: `docs/design/tide-predictions/`. Rendering
architecture: `docs/design/map-render-architecture.md`,
`rendering-backend-contract.md`. MCP server: `docs/mcp-server.md`.
