# Copilot Instructions for EncDotNet.S100

## Project overview

EncDotNet.S100 is a set of .NET libraries and a cross-platform desktop viewer for reading, portraying, and rendering [IHO S-100](https://iho.int/en/s-100-edition-5-2-0) nautical chart data. The solution targets .NET 10 with `<Nullable>enable</Nullable>` and `<ImplicitUsings>enable</ImplicitUsings>` throughout.

## Relevant specifications

| Spec | Role in this codebase |
|---|---|
| **S-100 Ed 5.2.1** (see `docs/specs/S-100 Ed 5.2.1_FINAL.pdf`) | Overarching framework; Parts 1–10 define exchange sets, HDF5 encoding, feature catalogues, portrayal catalogues, and the Lua portrayal engine |
| **S-101** | Electronic Navigational Charts — ISO 8211 encoded vector datasets; portrayal via S-100 Part 9A Lua pipeline |
| **S-102** | Bathymetric Surfaces — HDF5 encoded depth/uncertainty grids |
| **S-104** | Water Level Information — HDF5 encoded water-level time-step grids |
| **S-111** | Surface Currents — HDF5 encoded current speed/direction grids |
| **S-122** | Marine Protected Areas — GML encoded (S-100 Part 10b), XSLT portrayal |
| **S-124** | Navigational Warnings — GML encoded (S-100 Part 10b), XSLT portrayal |
| **S-125** | Marine Aids to Navigation — GML encoded (S-100 Part 10b), XSLT portrayal |
| **S-127** | Marine Resources and Services — GML encoded (S-100 Part 10b), XSLT portrayal |
| **S-128** | Catalogue of Nautical Products — GML encoded (S-100 Part 10b), XSLT portrayal |
| **S-129** | Under Keel Clearance Management — GML encoded (S-100 Part 10b) |
| **S-131** | Marine Harbour Infrastructure — GML encoded (S-100 Part 10b), Lua portrayal (Part 9A) |
| **S-201** | Aids to Navigation Information (IALA, authority-to-authority exchange) — GML encoded (S-100 Part 10b), XSLT portrayal |
| **S-411** | Sea Ice Information — GML encoded (S-100 Part 10b), XSLT portrayal |
| **S-421** | Route Plans (IEC 63173-2) — GML encoded (S-100 Part 10b), XSLT portrayal |
| **ISO 8211** | Record format used by S-101 datasets; read via `EncDotNet.Iso8211` NuGet package |
| **ISO 19110** | Feature Catalogue schema; parsed by `EncDotNet.S100.Features` |
| **HDF5** | Binary container used by S-102, S-104, S-111; accessed via the `IHdf5File`/`IHdf5Group` abstraction backed by PureHDF |

## Repository layout

```
src/
  EncDotNet.S100.Core/               # Core abstractions (IAssetSource, IHdf5File, ILuaEngine, pipelines, shared types)
  EncDotNet.S100.Features/           # Feature Catalogue XML parser (ISO 19110 / S-100 Part 5)
  EncDotNet.S100.ExchangeSets/       # Exchange Set CATALOG.XML parser
  EncDotNet.S100.Portrayals/         # Portrayal Catalogue XML parser (S-100 Part 9)
  EncDotNet.S100.Specifications/     # Bundles official FCs and PCs as embedded resources
  EncDotNet.S100.Hdf5.PureHdf/       # IHdf5File implementation using PureHDF (no native deps)
  EncDotNet.S100.Scripting.MoonSharp/ # ILuaEngine implementation using MoonSharp (Lua 5.2)
  EncDotNet.S100.Datasets.S101/      # S-101 ENC reader + Lua portrayal pipeline
  EncDotNet.S100.Datasets.S102/      # S-102 bathymetry reader + coverage pipeline
  EncDotNet.S100.Datasets.S104/      # S-104 water level reader + coverage pipeline
  EncDotNet.S100.Datasets.S111/      # S-111 surface currents reader + coverage pipeline
  EncDotNet.S100.Datasets.S122/      # S-122 marine protected areas reader + GML/XSLT portrayal
  EncDotNet.S100.Datasets.S124/      # S-124 navigational warnings reader + GML/XSLT portrayal
  EncDotNet.S100.Datasets.S125/      # S-125 marine aids to navigation reader + GML/XSLT portrayal
  EncDotNet.S100.Datasets.S127/      # S-127 marine resources & services reader + GML/XSLT portrayal
  EncDotNet.S100.Datasets.S128/      # S-128 catalogue of nautical products reader + GML/XSLT portrayal
  EncDotNet.S100.Datasets.S129/      # S-129 under keel clearance reader
  EncDotNet.S100.Datasets.S131/      # S-131 marine harbour infrastructure reader + GML/Lua portrayal
  EncDotNet.S100.Datasets.S201/      # S-201 aids to navigation information (IALA) reader + GML/XSLT portrayal
  EncDotNet.S100.Datasets.S411/      # S-411 sea ice reader + GML/XSLT portrayal
  EncDotNet.S100.Datasets.S421/      # S-421 route plans reader + GML/XSLT portrayal
  EncDotNet.S100.Renderers.Skia/     # SkiaSharp coverage + vector rasteriser (no map projection)
  EncDotNet.S100.Renderers.Mapsui/   # Mapsui layer renderer with CRS projection (ProjNet/EPSG:3857)
  EncDotNet.S100.Viewer/             # Avalonia cross-platform desktop viewer (macOS, Windows, Linux)
tests/
  EncDotNet.S100.Datasets.S104.Tests/
  EncDotNet.S100.Datasets.S111.Tests/
  EncDotNet.S100.Datasets.S124.Tests/
  EncDotNet.S100.Datasets.S125.Tests/
  EncDotNet.S100.Datasets.S131.Tests/
  EncDotNet.S100.Datasets.S201.Tests/
  EncDotNet.S100.Datasets.S421.Tests/
  EncDotNet.S100.ExchangeSets.Tests/
  EncDotNet.S100.Pipelines.Tests/    # Pipeline integration tests (S-101, S-102, coverage, vector, Skia)
tools/                               # CLI tools (RenderS102, TestS101Lua)
docs/                                # DocFX documentation source; specs PDF lives here
```

## Technology stack

| Concern | Library / tool |
|---|---|
| Language / platform | C# 13, .NET 10 |
| UI framework | [Avalonia](https://avaloniaui.net/) 11 + Mapsui.Avalonia |
| Map rendering | [Mapsui](https://mapsui.com/) 5 |
| 2-D rasterisation | [SkiaSharp](https://github.com/mono/SkiaSharp) 3, Svg.Skia |
| HDF5 | [PureHDF](https://github.com/Apollo3zehn/PureHDF) (fully managed, cross-platform) |
| Lua scripting | [MoonSharp](https://github.com/moonsharp-devs/moonsharp) 2 (Lua 5.2, sandboxed) |
| ISO 8211 | `EncDotNet.Iso8211` NuGet package |
| CRS projection | [ProjNet](https://github.com/NetTopologySuite/ProjNet4GeoAPI) |
| CLI tools | Spectre.Console.Cli |
| Testing | xunit 2, Xunit.SkippableFact, coverlet |
| Package management | Central Package Management via `Directory.Packages.props` |
| Documentation | DocFX (`docfx.json`) |
| CI/CD | GitHub Actions (`.github/workflows/ci.yml`, `release.yml`, `docs.yml`) |

## Architecture patterns

- **Abstraction-first**: concrete I/O implementations (`PureHdfFile`, `MoonSharpLuaEngine`, `FileSystemAssetSource`, `ZipAssetSource`) are hidden behind interfaces defined in `EncDotNet.S100.Core` and injected by callers.
- **Two pipeline types**:
  - *Coverage pipeline* — `ICoverageSource` → `CoveragePipeline` → `ICoverageRenderer<T>` (used by S-102, S-104, S-111).
  - *Vector pipeline* — `IVectorSource` + `IVectorPortrayalCatalogue` → `VectorPipeline` → `DrawingInstruction` list (used by S-101, S-124, S-125, S-127, S-131, S-201, S-421).
  - S-131 is a **GML+Lua hybrid**: GML-encoded data with Lua portrayal (Part 9A). The bridge is `S131LuaDataProvider` which adapts GML features to the same Lua Host API that S-101 uses.
- **Bundled specifications**: `EncDotNet.S100.Specifications` embeds official FCs/PCs and exposes them via `Specification.OpenFeatureCatalogueAsync()` / `Specification.CreatePortrayalCatalogueSource()`.

## Requirements for changes

### Tests
- Every new public API or bug fix must be accompanied by an xunit test in the appropriate test project under `tests/`.
- Use `Xunit.SkippableFact` (via `Skip.If(...)`) for tests that require optional external data files (e.g. real HDF5 or S-101 datasets) so CI does not fail when those files are absent.
- Run tests with: `dotnet test --configuration Release`

### Documentation
- Each library has a `README.md` in its `src/<project>/` directory. Update it when adding types, removing APIs, or changing behaviour.
- Conceptual guides live under `docs/` in DocFX Markdown. Add or update pages there for user-facing features.

### Naming and style
- Follow existing C# conventions: `PascalCase` for types/methods/properties, `camelCase` for locals and parameters, `_camelCase` for private fields.
- Nullable reference types are enabled everywhere — avoid `!` suppression; prefer null-checks or `ArgumentNullException.ThrowIfNull`.
- All public APIs must carry XML doc comments (`<summary>`, `<param>`, `<returns>`).

### Package dependencies
- All NuGet versions are managed centrally in `Directory.Packages.props`. Do not add `Version` attributes to individual `.csproj` files.
- Run `gh-advisory-database` security checks before introducing any new dependency.

### Build
- Build with: `dotnet build`
- CI runs on .NET 10 (`ubuntu-latest`). Avoid platform-specific APIs unless gated to the appropriate RID.

### Specifications content
- Spec assets (FCs, PCs, Lua rules, symbols) belong under `src/EncDotNet.S100.Specifications/content/<SXXX>/` following the layout described in that project's README.
- Never commit real ENC data files to the repository; use small synthetic test fixtures or skip tests that require live data.

## Spec expertise routing

This repository includes per-spec **skills** under `.github/skills/<spec>/SKILL.md` and matching **instructions files** under `.github/instructions/`. Engage them as follows:

- **Before** designing or implementing any non-trivial feature that touches a spec's semantics (encoding, attribute names, feature catalogue rules, portrayal pipelines, etc.), load the matching skill(s):

  | Signal in task or files touched | Load skill |
  |---|---|
  | S-100 framework, exchange sets, portrayal engine, Part 1–10 semantics | `s100-framework` |
  | S-101, ENC, ISO 8211, vector features, Lua portrayal | `s101-enc` |
  | S-102, bathymetry, depth, uncertainty grids | `s102-bathymetry` |
  | S-104, water level, tide grids | `s104-water-level` |
  | S-111, surface currents, current speed/direction | `s111-surface-currents` |
  | S-124, navigational warnings, GML, XSLT portrayal | `s124-nav-warnings` |
  | S-125, AtoN, marine aids to navigation, lights, buoys, beacons | `s125-aton` |
  | S-122, marine protected areas, restricted areas, VTS, GML, XSLT portrayal | `s122-marine-protected-areas` |
  | S-127, marine resources and services, pilot boarding, routeing measures, vessel traffic services | `s127-marine-services` |
  | S-128, catalogue of nautical products, electronic products, physical products, S-100 services | `s128-catalogue` |
  | S-129, under keel clearance, UKC | `s129-ukc` |
  | S-131, marine harbour infrastructure, berths, bollards, mooring buoys | `s131-marine-harbour` |
  | S-201, IALA aids to navigation information, AtoN-authority exchange (NOT ECDIS — use s125-aton for ECDIS) | `s201-aton-information` |
  | S-411, sea ice, ice information, icebergs, ice edges | `s411-sea-ice` |
  | S-421, route plans, voyage plans, waypoints, route legs | `s421-route-plans` |

- For **cross-spec** features (e.g. a change to `CoveragePipeline` affecting S-102/S-104/S-111), load all affected spec skills and reconcile their guidance before writing code.
- When delegating exploration via `search_subagent`, prefer one subagent per affected `src/EncDotNet.S100.Datasets.Sxxx/` project so each runs with its spec context isolated.
- Cite the relevant spec section number(s) in PR descriptions and in XML doc comments for spec-derived constants, enums, attribute names, and group paths.

## Viewer UI

When editing `src/EncDotNet.S100.Viewer/**`, follow the rules in
`.github/instructions/viewer.instructions.md`. In particular:

- Every UI-visible string lives in `Resources/Strings.resx` with a
  matching property in `Resources/Strings.cs`. XAML references via
  `{x:Static loc:Strings.Key}`; code references via `Strings.Key`
  (use `string.Format` for parameterized status text). No hardcoded
  user-facing strings in views, view-models, or code-behind.
- Every button must have a meaningful `ToolTip.Tip`.
- All `GridSplitter`s use `Classes="PaneSplitter"` with thickness 4
  and a 500ms hover delay before the accent shows. Adjacent panels
  must not draw their own border on the splitter-facing edge.
