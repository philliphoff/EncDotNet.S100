# Asset Caching Audit — EncDotNet.S100

**Living document.** Originally produced as an audit; now also tracks
the implementation work that flows out of it. Implementation status
table is below the executive summary; see the appendix for the
pipeline-segment deep dives that informed each PR.

**Audit branch:** `philliphoff/asset-caching-audit` (notes only; no code).
**Implementation PRs:** see "Implementation status" below — each PR
branches off `main` and is independently mergeable.
**Scope of investigation:** every dataset processor + per-spec catalogue +
shared infrastructure under `src/EncDotNet.S100.Core`,
`src/EncDotNet.S100.Specifications`, `src/EncDotNet.S100.Features`,
`src/EncDotNet.S100.Portrayals`, `src/EncDotNet.S100.Renderers.*`, plus
the viewer's loader.

---

## 1. Summary

The cross-cutting work in PRs #43/#44/#48 succeeded at one thing only:
de-duplicating the **first-level catalogue parse** (the `FeatureCatalogue`
XML and the `PortrayalCatalogueProvider` for `portrayal_catalogue.xml`).
Those are now cached once-per-spec in `FeatureCatalogueManager` and
`PortrayalCatalogueManager`, keyed by `SpecRef`, behind the
`ICatalogueProvider<T>` abstraction.

Almost everything *downstream* of that — XSLT compilation, SVG decoding,
Lua module reads, Lua chunk compilation, line styles, area fills, colour
palettes, pattern tile rasterization — is still **per-dataset-processor**
or worse **per-render**. The wins from #43/#44 are real but small
compared to what's still hot.

The dominant offenders, in order of expected wall-clock impact:

1. **`MapsuiDisplayListRenderer` is reconstructed on every `Render()` call**
   in every processor. Its `_symbolDataUriCache` (post-`SvgProcessor`
   processed SVG strings) and `_patternTileCache` (rasterised PNG bytes)
   are discarded between renders of the *same* dataset, so every palette
   toggle, time-step scrub, mariner-setting change, or display-scale
   change pays the full SVG processing + pattern rasterization cost
   again. This is a single-PR fix and likely the biggest win.

2. **S-101 / S-131 Lua rule files are re-read and re-compiled on every
   `Render()` call.** `S101LuaRuleExecutor.ExecuteRaw` (`S101LuaRuleExecutor.cs:195-233`)
   creates a fresh `MoonSharp.Script`, a fresh `moduleCache` dictionary,
   and calls `LoadRuleSource("main.lua")` for every invocation. Same in
   `S131LuaRuleExecutor`. The MoonSharp interpreter is forced to lex +
   parse + compile main.lua, S100Scripting.lua, PortrayalModel.lua,
   PortrayalAPI.lua, etc. on every render. No compiled-chunk reuse
   because MoonSharp doesn't expose one cleanly — but the *source
   strings* could and should live at the catalogue or provider level.

3. **Per-spec portrayal catalogue wrappers are reborn per dataset.**
   Every dataset processor does `new SxxxPortrayalCatalogue(provider, …)`
   in its constructor (call sites listed below). Those wrappers hold the
   "level 2" caches: `_compiledXslt`, `_symbols`, `_lineStyles`,
   `_areaFills`, `_luaScripts`, `_palettes`. Two open datasets of the
   same product spec each pay their own XSLT compile + SVG read + line
   style read + area fill read costs.

4. **S-111 reloads & reparses the colour profile on every
   `SwitchPalette()`** (`S111PortrayalCatalogue.cs:53-64`, 123-131). Day
   / Dusk / Night re-fetches the same `ColorProfiles/…` XML from the
   asset source and re-parses it. The palette result is not cached.

5. **`EmbeddedAssetSource` case-insensitive fallback rescans manifest
   names** (`EmbeddedAssetSource.cs:55-63`). For S-127 / S-411 sub-template
   resolution this can re-walk the entire manifest list on every miss.
   Worth a memoised lookup table.

6. **There is no asset-source caching decorator.** Every
   `IAssetSource.OpenAsync(relativePath)` re-runs the underlying lookup
   even though the contents are read-only and trivially poolable for
   small assets (Lua sources, XSLT templates, SVG payloads).

A recommended target architecture is outlined in §5.

---

## 2. Inventory: every static, read-only asset and its current caching state

| Asset kind | Origin (parse cost) | Where cache lives | Cache key | Lifetime | Hot per render? |
|------------|---------------------|-------------------|-----------|----------|-----------------|
| `FeatureCatalogue` (XML) | `FeatureCatalogueReader.Read` | `FeatureCatalogueManager._catalogues` | `SpecRef` | manager | ✅ cached once |
| `FeatureCatalogueDecoder` | derived from FC | `FeatureCatalogueManager._decoders` | `SpecRef` | manager | ✅ cached once |
| `PortrayalCatalogue` (root XML metadata) | `PortrayalCatalogueReader.Read` | `PortrayalCatalogueManager._providers` (one `PortrayalCatalogueProvider`) | `SpecRef` | manager | ✅ cached once |
| Compiled XSLT (`XslCompiledTransform`) | `XslCompiledTransform.Load` | `S101PortrayalCatalogue._compiledXslt`, `GmlPortrayalCatalogueBase._compiledXslt` | rule name | per `XxxPortrayalCatalogue` instance (= per dataset processor) | ⚠️ first render only — but a *new* wrapper exists per dataset processor |
| SVG symbol content (string) | `_provider.FetchAssetAsync(item,"Symbols")` + `StreamReader.ReadToEnd()` | `XxxPortrayalCatalogue._symbols` | symbol name | per wrapper (per dataset) | ⚠️ as above |
| `LineStyle` (parsed XML) | `LineStyleReader.Read` | `XxxPortrayalCatalogue._lineStyles` | name | per wrapper | ⚠️ per dataset |
| `AreaFill` (parsed XML) | `AreaFillReader.Read` | `XxxPortrayalCatalogue._areaFills` | name | per wrapper | ⚠️ per dataset |
| `ColorPalette` | `ColorProfileReader.Read` | `XxxPortrayalCatalogue._palettes` (S-101 / GML base) OR none (S-111) | `PaletteType` | per wrapper, except S-111 has no cache | ❌ S-111 cold every `SwitchPalette` |
| S-101/S-131 Lua rule sources | `_provider.FetchRuleAsync` + `ReadToEnd` | `S101PortrayalCatalogue._luaScripts` only for `GetLuaScript()` callers; **not** used by `S101LuaRuleExecutor.ExecuteRaw` | rule name | per wrapper *and* per executor pass | ❌ every render re-reads `main.lua` and every `require()`d module |
| S-101/S-131 Lua **compiled chunks** | MoonSharp `Script.DoString` | not cached | n/a | per render | ❌ every render |
| S-101 `S101LuaRuleExecutor._luaEngine` runtime state | `MoonSharpLuaEngine.CreateContext()` | not pooled | n/a | per render | ❌ every render |
| S-102 BathymetryCoverage.lua source | `S102PortrayalCatalogue._cachedLuaSource` | yes, single field | n/a | per wrapper | ✅ cached after first render |
| Processed SVG ("data URI" string post `SvgProcessor.Process`) | `SvgProcessor.Process(palette)` | `MapsuiDisplayListRenderer._symbolDataUriCache` | symbol name | **per renderer instance, which is per `Render()` call** | ❌ every render |
| Rasterised pattern tile PNG bytes | `SkiaSvgRasterizer.RasterizePatternTile` | `MapsuiDisplayListRenderer._patternTileCache` | fill name | per renderer | ❌ every render |
| Asset bytes from `EmbeddedAssetSource` | `Assembly.GetManifestResourceStream` | none | n/a | n/a | ❌ every fetch |
| Manifest-name list for case-insensitive fallback | `Assembly.GetManifestResourceNames()` | none — recomputed on miss | n/a | n/a | ❌ on each miss |
| HDF5 `IHdf5File` handles | `PureHdfFile.Open(stream)` | not retained; file closed after dataset read | n/a | construction only | ✅ correct (closed eagerly) |
| Per-dataset Mapsui `ILayer` outputs | `renderer.Render(...)` | not cached; renderer rebuilds layer each call | n/a | per render | ❌ acceptable (output is geometry-dependent) |

---

## 3. Spec × asset kind matrix

`✓` cached at appropriate scope, `△` cached only at the per-dataset
level (so duplicated for two datasets of the same spec), `✗` never cached
(every render or every fetch), `—` not applicable.

| Spec | FC | PC root | XSLT/Lua rules (compiled) | Lua source | SVG content (raw) | SVG processed | LineStyle | AreaFill | Palette | Pattern tile PNG |
|------|----|---------|---------------------------|------------|-------------------|---------------|-----------|----------|---------|------------------|
| S-101 | ✓ | ✓ | △ | ✗ ¹ | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-102 | ✓ | ✓ | — | ✓ (single Lua) | — | — | — | — | static | — |
| S-104 | ✓ | (no PC) | — | — | — | — | — | — | static | — |
| S-111 | ✓ | ✓ | — | — | — | — | — | — | ✗ ³ | — |
| S-122 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-124 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-125 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-127 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-128 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-129 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-131 | ✓ | ✓ | — | ✗ ¹ | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-411 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-421 | ✓ | ✓ | △ | — | △ | ✗ ² | △ | △ | △ | ✗ ² |
| S-57 | (uses S-101 FC) | (S-101 PC) | △ | ✗ ¹ | △ | ✗ ² | △ | △ | △ | ✗ ² |

¹ S-101/S-131: `S101LuaRuleExecutor.ExecuteRaw` /
  `S131LuaRuleExecutor` build a fresh `Dictionary<string,string?>` module
  cache + call `LoadRuleSource("main.lua")` on every render
  (`S101LuaRuleExecutor.cs:200-240`, `:384-390`). The
  `S101PortrayalCatalogue._luaScripts` dictionary exists but is bypassed
  by the executor; only `GetLuaScript()` callers populate it.

² Every render constructs a new `MapsuiDisplayListRenderer`
  (`S101DatasetProcessor.cs:113`, `S131DatasetProcessor.cs:138`,
  `S57DatasetProcessor.cs:116`, `GmlDatasetProcessorBase.cs:135`). The
  renderer's `_symbolDataUriCache` and `_patternTileCache` are
  *instance* fields, so their entries are recomputed every render.

³ S-111 has no `_palettes` cache; `SwitchPalette` calls
  `LoadColorPalette(...)` every time, which re-fetches and re-parses
  the colour profile XML (`S111PortrayalCatalogue.cs:53-64`,
  `:123-131`).

---

## 4. Specific call sites bypassing the cache

| # | File:line | Issue |
|---|-----------|-------|
| 4.1 | `src/EncDotNet.S100.Datasets.Pipelines/S101DatasetProcessor.cs:72` | `_catalogue = new S101PortrayalCatalogue(_provider, _luaEngine);` — fresh wrapper, fresh XSLT/SVG/LineStyle/AreaFill caches per dataset. |
| 4.2 | `src/EncDotNet.S100.Datasets.Pipelines/S101DatasetProcessor.cs:113` | `var vectorRenderer = new MapsuiDisplayListRenderer { … };` — fresh renderer, fresh processed-SVG cache, fresh pattern-tile cache per **Render() call**. |
| 4.3 | `src/EncDotNet.S100.Datasets.Pipelines/GmlDatasetProcessorBase.cs:135` | Same — applies to S-122/124/125/127/128/129/411/421/(S-131 via its own copy). |
| 4.4 | `src/EncDotNet.S100.Datasets.Pipelines/S131DatasetProcessor.cs:98` and `:138` | Fresh wrapper + fresh renderer. |
| 4.5 | `src/EncDotNet.S100.Datasets.Pipelines/S57DatasetProcessor.cs:77` and `:116` | Same. |
| 4.6 | `src/EncDotNet.S100.Datasets.Pipelines/S122DatasetProcessor.cs:51`, `S124:50`, `S125:57`, `S127:55`, `S128:53`, `S129:51`, `S411:62`, `S421:51` | `new SxxxPortrayalCatalogue(catalogueManager.GetProvider("S-xxx"))` in every processor ctor. |
| 4.7 | `src/EncDotNet.S100.Datasets.S101/S101LuaRuleExecutor.cs:200-233` | New `S101LuaDataProvider`, new `MoonSharp.Script` (via `_luaEngine.CreateContext()`), new module cache dictionary per `ExecuteRaw`. |
| 4.8 | `src/EncDotNet.S100.Datasets.S101/S101LuaRuleExecutor.cs:239-267` | `lua.Execute(mainSource)` + four shim scripts + context-parameter init **on every render**. Lua source re-parsing dominates here. |
| 4.9 | `src/EncDotNet.S100.Datasets.S131/S131LuaRuleExecutor.cs:210-243` | Identical pattern to S-101. |
| 4.10 | `src/EncDotNet.S100.Datasets.S111/S111PortrayalCatalogue.cs:53-64`, `:123-131` | `SwitchPalette` ⇒ `LoadColorPalette` ⇒ asset fetch + `ColorProfileReader.Read` every call. |
| 4.11 | `src/EncDotNet.S100.Specifications/EmbeddedAssetSource.cs:55-63` | `_assembly.GetManifestResourceNames().FirstOrDefault(...)` on every miss (used by S-127 XSL sub-template casing fallback). |
| 4.12 | `src/EncDotNet.S100.Datasets.S101/S101PortrayalCatalogue.cs:24-29` | Plain `Dictionary<,>` caches (not `ConcurrentDictionary`); same pattern in `GmlPortrayalCatalogueBase.cs:28-32` and `S131PortrayalCatalogue.cs:33-38`. Today renders are serialised through `Task.Run` per processor, so no observable race, but cross-processor cache *sharing* (proposal in §5) makes this load-bearing. |
| 4.13 | `src/EncDotNet.S100.Core/Pipelines/Vector/PortrayalRule[s]` rebuilt every `Rules` access on cold catalogue | `S101PortrayalCatalogue.cs:136-144` is OK (memoised), but the inferred-feature-types list (`InferFeatureTypes`) computes per-rule on first access only. ✓ |

---

## 5. Recommended target architecture

The current pattern is a two-layer cache where layer 1 (catalogue
parse) is per-spec / process-scoped via `ICatalogueProvider`, and layer
2 (per-catalogue assets) is per dataset instance. The audit-driven
target is **three layers**, all scoped per `CatalogueRef`, not per
processor:

```
┌─────────────────────────────────────────────────────────────────────┐
│ ICatalogueProvider<FeatureCatalogue>          ✓ already correct      │
│ ICatalogueProvider<PortrayalCatalogueProvider> ✓ already correct      │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ (NEW)  IPortrayalAssetCache  — keyed by CatalogueRef                 │
│   • compiled XSLT (XslCompiledTransform) by rule name                │
│   • raw SVG source strings by symbol name                            │
│   • compiled Lua sources / module sources by rule name               │
│   • LineStyle / AreaFill parsed objects                              │
│   • ColorPalette by (palette name, palette type)                     │
│   Thread-safe (ConcurrentDictionary); shared across all datasets of  │
│   the same product spec edition.                                     │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ (NEW)  IRendererAssetCache  — keyed by (CatalogueRef, PaletteType)   │
│   • processed SVG (post SvgProcessor.Process, palette-baked)         │
│   • pattern-tile PNGs (post SkiaSvgRasterizer)                       │
│   Shared by all MapsuiDisplayListRenderer instances of the same spec │
│   + palette. Bound to the spec because palette substitution is the   │
│   only palette-coupled work.                                          │
└─────────────────────────────────────────────────────────────────────┘
```

Implementation notes:

- The `XxxPortrayalCatalogue` classes become **thin facades** over the
  shared `IPortrayalAssetCache` they receive in their constructor;
  their internal dictionaries are removed. Dataset processors keep
  using `new S101PortrayalCatalogue(...)` but the wrapper is now
  cheap.
- `MapsuiDisplayListRenderer` accepts an injected `IRendererAssetCache`
  via its property bag (defaulting to a per-instance cache for
  callers that don't supply one — preserves backward compat).
- `S101LuaRuleExecutor` reads main.lua and module sources via the
  asset cache (or, simpler, by binding to `S101PortrayalCatalogue.GetLuaScript`
  which already has the right Dictionary; the *real* fix is to make
  that dictionary live above the executor, not be bypassed by it).
- All caches keyed by `CatalogueRef` (or `SpecRef` for caches that
  don't change with catalogue version), never by object reference or
  by file path string.
- Lifetime tied to the `PortrayalCatalogueManager` and
  `FeatureCatalogueManager` — both already implement `IDisposable`.
  Caches drop with the manager. The viewer keeps one manager per
  application session, so the caches live for the app lifetime; this
  matches the static, read-only nature of the assets.
- Thread-safety: every cache uses `ConcurrentDictionary<TKey, TValue>`
  + `GetOrAdd(key, ValueFactory)` (or `Lazy<T>` wrappers like
  `FeatureCatalogueManager` already uses) to avoid double-compile under
  contention.

This brings the architecture in line with the "raw asset-based provider
wrapped by a caching decorator" pattern. Asset sources stay dumb;
catalogue managers own the caching policy.

---

## 6. Ranked PR plan

Each entry is sized to ship as a single small PR.

### PR-1 — Share `MapsuiDisplayListRenderer` symbol/pattern caches (highest impact, easiest)
**Files:** `MapsuiDisplayListRenderer.cs`, all dataset processors that
new it up.
**Change:** lift `_symbolDataUriCache` and `_patternTileCache` out of
`MapsuiDisplayListRenderer` into a small `MapsuiRenderAssetCache` keyed by
`(CatalogueRef, PaletteType)` and pass it via the renderer's property bag.
Cache is owned by the dataset processor (lives as long as the processor),
so re-renders of the same dataset reuse SVG processing + pattern tile
work.
**Impact:** every palette toggle / time scrub / mariner-setting change
on an already-loaded dataset skips SVG re-processing and PNG
re-rasterization.
**Risk:** low — internal-only change.

### PR-2 — Cache S-101 / S-131 Lua sources at the catalogue level and reuse them
**Files:** `S101LuaRuleExecutor.cs`, `S131LuaRuleExecutor.cs`,
`S101PortrayalCatalogue.cs`, `S131PortrayalCatalogue.cs`.
**Change:** route `LoadRuleSource("main.lua")` and the module loader
through the catalogue's existing `_luaScripts` dictionary (or a new
`GetLuaSource(string)` method on the catalogue that reads + caches the
raw string). The MoonSharp `Script` itself still has to be recreated
per execution (sandboxed state), but **the per-render `ReadToEnd()` of
~10–15 Lua files disappears.**
**Impact:** removes ~10–15 stream opens + decoder allocations per S-101
or S-131 render.
**Risk:** low — just relocates the existing per-executor cache up one
level.

### PR-3 — Move per-catalogue caches up to the spec level via `IPortrayalAssetCache`
**Files:** new `EncDotNet.S100.Portrayals/PortrayalAssetCache.cs`;
update `S101PortrayalCatalogue`, `GmlPortrayalCatalogueBase`,
`S131PortrayalCatalogue`. Update `PortrayalCatalogueManager` to
construct + own one cache per `SpecRef`.
**Change:** `_compiledXslt`, `_symbols`, `_lineStyles`, `_areaFills`,
`_palettes`, `_luaScripts` move into a `PortrayalAssetCache` instance
shared between all dataset processors of the same spec.
**Impact:** two open datasets of the same spec stop paying duplicate
XSLT compile + SVG read + line style / area fill parsing.
**Risk:** medium — touches every per-spec catalogue wrapper, but the
public surface stays the same.

### PR-4 — Fix S-111 palette re-load
**Files:** `S111PortrayalCatalogue.cs`.
**Change:** add a `_palettes` dictionary mirroring the S-101 pattern;
cache by `PaletteType`. Better still: load all three palettes from the
single color-profile file eagerly on first access.
**Impact:** small per-render saving when scrubbing day/dusk/night
toggles; also fixes the subtle gotcha that `ResolveColorScheme` calls
`LoadColorPalette` if `ActivePalette.Colors.Count == 0` even after
`SwitchPalette` succeeded (a defensive belt-and-suspenders re-load).
**Risk:** trivial.

### PR-5 — Memoise `EmbeddedAssetSource` case-insensitive fallback
**Files:** `EmbeddedAssetSource.cs`.
**Change:** lazily build a case-insensitive `Dictionary<string,string>`
from `GetManifestResourceNames()` on first miss, reuse for subsequent
misses. Avoids re-scanning the manifest for every S-127 XSL sub-template
include.
**Impact:** small but measurable for S-127 / S-411.
**Risk:** trivial.

### PR-6 — Make per-catalogue dictionaries `ConcurrentDictionary` and use `GetOrAdd`
**Files:** `S101PortrayalCatalogue.cs`,
`GmlPortrayalCatalogueBase.cs`, `S131PortrayalCatalogue.cs`.
**Change:** swap `Dictionary<,>` → `ConcurrentDictionary<,>` and replace
the `TryGetValue` + `if missing then load + store` pattern with
`GetOrAdd(key, valueFactory)`. Eliminates the double-load race that
would otherwise emerge once PR-3 makes these caches process-scoped.
**Impact:** prevents redundant XSLT compile / SVG read under
concurrent renders.
**Risk:** trivial; only required *if* PR-3 ships first or simultaneously.

### PR-7 — Counters / spans for cache hit-miss rates on the new caches
**Files:** add `s100.xslt.cache.{hit,miss}.count`,
`s100.linestyle.cache.*`, `s100.areafill.cache.*`, `s100.lua.source.cache.*`
counters using the existing telemetry. Match the pattern in
`MapsuiDisplayListRenderer` (`SymbolCacheHit` / `SymbolCacheMiss`).
**Impact:** observability — feeds future baseline diffs.
**Risk:** trivial.

### PR-8 (optional / longer-term) — A bytes-cache wrapper around `IAssetSource`
**Files:** new `CachingAssetSource` decorator in
`EncDotNet.S100.Core` + opt-in wiring in `PortrayalCatalogueProvider`.
**Change:** small LRU cache of `byte[]` keyed by `relativePath` for
read-only sources (`EmbeddedAssetSource`, `FileSystemAssetSource` in
read-only mode). Most callers do `ReadToEnd()` or pass the stream to
an XML reader; both can be served from a `MemoryStream` view of the
cached bytes.
**Impact:** consolidates the "open + read all" pattern that's still
ubiquitous in catalogue code. Largely subsumed by PR-1/2/3 for the
hot paths, but useful for less-frequently-hit assets.
**Risk:** medium — defines a new abstraction; only worth it after
PR-1/2/3 land and the remaining hot fetches are profiled.

---

## 7. Out-of-scope follow-ups noted during the audit

- `DatasetPipelineFactory` constructs a new `FeatureCatalogueManager`
  per factory (`DatasetPipelineFactory.cs:56`). Each viewer session has
  one factory, so this is fine — but worth a comment so future
  refactors don't make it per-dataset.
- `EmbeddedAssetSource.Dispose()` is a no-op, but `PortrayalCatalogueProvider.Dispose()`
  disposes its source (`PortrayalCatalogueProvider.cs:99`). Combined
  with the eviction-on-`SetSource` path in `PortrayalCatalogueManager.cs:117`,
  this works today but means a registered source you pass in becomes
  owned by the manager. Document this or split into "I own" vs
  "I borrow" overloads.
- Today every render walks `_provider.Catalogue.RuleFiles` /
  `Symbols` / `LineStyles` / `AreaFills` with `FirstOrDefault`
  (e.g. `GmlPortrayalCatalogueBase.cs:289-291`, `:315-316`). After PR-3
  the lookup happens at most once per (spec, name) — but pre-PR-3 the
  list walk is per-render. Trivial follow-up: build an
  `IDictionary<string, CatalogItem>` once when the provider is
  constructed.
- `S102PortrayalCatalogue.ResolveColorScheme` (`S102PortrayalCatalogue.cs:69-112`)
  re-runs the BathymetryCoverage.lua script every call to compute the
  colour scheme even though context parameters change rarely. Could
  cache by `MarinerSettings`. Low priority — the script is small.
- Telemetry: there's no `s100.symbol.resolve.miss.count` counter yet;
  the perf baseline summary shows `s100.symbol.cache.{hit,miss}.count`
  at value 1 each — clearly the renderer is hit-cold on each
  iteration, which is consistent with the per-render reconstruction
  finding above. PR-7 should fix attribution.

---

*Audit complete. No code changes performed in this session.*

---

# Implementation status

| PR | Audit ref | Status | Branch / PR |
|----|-----------|--------|-------------|
| PR-0: `CachingAssetSource` + `AssetBytes` + memoised manifest fallback | Appendix §1.6–§1.9 | **Shipped** (PR #59) | `philliphoff/asset-bytes-caching-seam` off `main` |
| PR-A: Race-free `PortrayalCatalogueManager` | Appendix §2.7-A | **Shipped** (PR #60) | `philliphoff/catalogue-identity-segment-2` off `main` |
| PR-B: `FeatureCatalogueManager` as viewer singleton | Appendix §2.7-B | **Shipped** (PR #61) | same branch |
| PR-C: `IAssetSource`-shaped FC manager config | Appendix §2.7-C | **Shipped** (PR #62) | same branch |
| Wire FC `SetSource` into `App.axaml.cs` | PR-C follow-up | Pending (small, ~10 lines) | after PR-B/C merge |
| PR-1 → PR-N | §5 | Pending |  |

### PR-0 outcome (2026-05-13)

- Implemented exactly per Appendix §1.6: `AssetBytes` record struct
  (zero-copy `AsStream()` via internal `ReadOnlyMemoryStream`),
  `AssetSourceExtensions.ReadAllBytesAsync` (MemoryStream fast-path),
  `CachingAssetSource` decorator
  (`ConcurrentDictionary<string, Lazy<Task<AssetBytes>>>` with
  `ExecutionAndPublication`, ordinal keys, `Dispose` forwards).
- Bonus PR-5 subsumed: `EmbeddedAssetSource` manifest fallback
  memoised via `Lazy<Dictionary<string, string>>`.
- Wired into `Specification.CreatePortrayalCatalogueSource` only.
  **FC was scoped out of this PR** — `Specification.OpenFeatureCatalogueAsync`
  is a per-call helper that disposes the source inline, so wrapping
  there would be a no-op. Tracked as a follow-up.
- Tests: new `EncDotNet.S100.Core.Tests` files covering
  `AssetBytes`, `AssetSourceExtensions`, `CachingAssetSource`,
  including a 32-way concurrent first-read collapse test against a
  counting fake source. All 111 Core tests + full
  `dotnet test --configuration Release` green.
- Perf sanity: `s101-portray-warm` 100.3 ms → 86.5 ms
  (within run-to-run noise; no regression — value of the PR is the
  seam, not a hot-path optimisation).

### PR-0 deferred follow-ups (now tracked, not yet scheduled)

1. ~~Reshape `Specification` FC access to expose a long-lived
   `IAssetSource` so `CachingAssetSource` can wrap it.~~
   **Done** — shipped as PR-C (#62) in Segment 2.
2. Add `lock (_archive)` to `ZipAssetSource.OpenAsync` to fix the
   latent thread-safety hole on cold reads.
3. Wrap viewer-loaded exchange-set `ZipAssetSource` instances in
   `CachingAssetSource` at the dataset-loader pathway.
4. Add `s100.asset.cache.hit` / `s100.asset.cache.miss` counters in
   `CachingAssetSource` (and corresponding spans).
5. Promote `AssetBytes` onto `IAssetSource` directly (revisit after
   Segment 3 review).

---

# Appendix — Pipeline-segment deep dive

The user requested a more concrete, type-by-type description of what
is held where today and what the target shape should look like. We
walk this segment-by-segment, bottom-up.

## Segment 1 — Asset I/O (`IAssetSource` and friends)

### 1.1 Type inventory

| Type | Kind | Mutability | Cost to produce | Thread-safe? | Owned by |
|------|------|------------|-----------------|--------------|----------|
| `EncDotNet.S100.Core.IAssetSource` | interface, `IDisposable` | n/a (contract) | n/a | not specified | caller / catalogue provider |
| `EncDotNet.S100.Core.FileSystemAssetSource` | sealed class | immutable after `Create` (just a `_basePath`) | trivial — `Create` calls `Path.GetFullPath` once | yes: each `OpenAsync` is a fresh `File.OpenRead` (independent fd) | callers; `Dispose` is no-op (`FileSystemAssetSource.cs:55-58`) |
| `EncDotNet.S100.Core.ZipAssetSource` | sealed class | holds a `ZipArchive` (`_archive`) + `_basePath` | construction parses central directory; `OpenAsync` does `GetEntry` + `entry.Open()` (DeflateStream, no extraction-to-memory) | **`ZipArchive` is NOT thread-safe** for concurrent reads from different entries (BCL doc); current code does no locking | callers; `Dispose` disposes the archive (`ZipAssetSource.cs:93`) |
| `EncDotNet.S100.Specifications.EmbeddedAssetSource` | sealed class | `_assembly` + `_resourcePrefix` | construction trivial; `OpenAsync` does `assembly.GetManifestResourceStream(name)` which is fast (CLI metadata lookup → unmanaged-memory `UnmanagedMemoryStream` view) | yes: `Assembly.GetManifestResourceStream` is thread-safe | typically singleton-per-spec (managed by `Specification.OpenPortrayalCatalogueSource` and friends) |
| Returned `Stream` | varies — `FileStream`, `DeflateStream` (over zip), `UnmanagedMemoryStream` (embedded) | not seekable in general (DeflateStream isn't), forward-only | each call returns a fresh independent stream | yes (different streams) | caller (`using` everywhere in this codebase) |

### 1.2 Identity & key properties of the contract

- The `relativePath` argument is a **stable string key**: forward-slash
  separated, case-sensitive in spec terms but the embedded source has
  a case-insensitive fallback (S-127 idiosyncrasy,
  `EmbeddedAssetSource.cs:55-63`).
- Contents are **read-only** for the lifetime of the source instance:
  - `FileSystemAssetSource`: trusts the FS won't change. (Acceptable —
    spec content is read-only on disk in practice.)
  - `ZipAssetSource`: archive is opened read-only.
  - `EmbeddedAssetSource`: assembly manifest is immutable.
- Sizes are small (Lua/SVG/XSLT/XML are kilobytes; the largest
  embedded artefacts are a handful of XSL stylesheets in the tens of
  KB). Memoising bytes in memory is cheap.
- The contract returns `Task<Stream>` but all three implementations
  complete synchronously and wrap via `Task.FromResult`. Every
  in-tree caller immediately blocks via `.GetAwaiter().GetResult()`
  (see `GmlPortrayalCatalogueBase.cs:293`, `S131PortrayalCatalogue.cs:242`,
  `S101LuaRuleExecutor.cs:221`, etc.). The asynchrony is vestigial.

### 1.3 How instances live today

- `Specification.OpenPortrayalCatalogueSource(specName)` builds a
  fresh `EmbeddedAssetSource` per call (rooted at e.g.
  `EncDotNet.S100.Specifications.content.S131.pc`). This is cheap.
- `PortrayalCatalogueManager.SetSource(specRef, source)` takes
  ownership of a passed-in `IAssetSource` and disposes it on
  eviction / dispose (`PortrayalCatalogueManager.cs:117` and `:172`).
- `PortrayalCatalogueProvider` wraps the source and disposes it
  (`PortrayalCatalogueProvider.cs:99`).
- Concretely, when the viewer is up there is **one
  `EmbeddedAssetSource` per `(SpecRef, "FC" | "PC")`** kept alive for
  the app session, held inside the relevant `*CatalogueManager`. That
  matches the read-only static nature of the underlying assembly
  resources.

### 1.4 What this segment actually costs today

The hot path per render (S-101 example, single dataset open):

1. `Specification.OpenPortrayalCatalogueSource("S-101")` → 1 ×
   `EmbeddedAssetSource.Create` (trivial). One-time at app startup
   per spec.
2. `PortrayalCatalogueProvider.OpenAsync(source)` →
   `source.OpenAsync("portrayal_catalogue.xml")` →
   `Assembly.GetManifestResourceStream(...)`. One-time per spec.
3. *Per render:* `S101LuaRuleExecutor.ExecuteRaw` calls
   `_provider.FetchRuleAsync("main.lua")`, then for each
   `require()`'d module: `FetchRuleAsync(<module>)`. Each call goes
   through `_source.OpenAsync(...)` which:
   - opens a fresh `UnmanagedMemoryStream` (embedded) — sub-microsecond
   - the caller wraps it in a `StreamReader`, calls `ReadToEnd()`,
     allocates the string, then disposes everything.
   That stream open is **not** the bottleneck — the `ReadToEnd()` +
   string allocation is small but happens 10–15× per render for S-101.
4. *Per render of vector products with SVG / XSLT:* same pattern via
   `FetchAssetAsync(catalogItem, "Symbols")` etc. Already cached by
   the per-catalogue `_symbols` / `_compiledXslt` dictionaries (PR-3
   subject), so the embedded asset source is hit only on cold misses.

So the asset source itself is **not** the dominant cost. What it lacks
is a place to memoise the *decoded bytes / strings* so the layers above
don't have to re-implement that caching themselves.

### 1.5 Gaps / sharp edges in the current contract

1. **No "open bytes" / "open string" convenience.** Every caller does
   `using var stream = await source.OpenAsync(...); using var reader =
   new StreamReader(stream); var s = reader.ReadToEnd();`. This is
   boilerplate, allocates two disposables per call, and prevents a
   trivial wrap-with-caching decorator from short-circuiting at the
   bytes level. Adding `Task<ReadOnlyMemory<byte>>
   ReadAllBytesAsync(string)` (default-implemented in terms of
   `OpenAsync` + `ToArray`) on the interface unlocks a clean caching
   decorator without touching call sites.
2. **`ZipAssetSource` is not thread-safe.** `ZipArchive` requires
   external synchronisation for concurrent reads of different
   entries. Today the code doesn't lock. Two parallel S-101 renders
   sharing a ZIP-backed exchange set could race. (Not actively
   exploited — the viewer serialises renders per processor — but a
   latent bug.) Worth a `lock (_archive)` around `_archive.GetEntry`
   + the wrap of the returned stream into a `MemoryStream` copy if
   we want callers to read concurrently.
3. **No memoisation of `EmbeddedAssetSource`'s manifest-name lookup
   table.** Documented above (4.11). A `Lazy<Dictionary<string,
   string>>` keyed by lower-cased name resolves this trivially.
4. **`OpenAsync` returns `Task<Stream>` but is always sync in
   practice.** Acceptable, but worth noting for the recommended
   contract.
5. **No identity primitive for assets.** `relativePath` is treated as
   the cache key whenever caching is bolted on (`SymbolEntry` keyed
   by symbol name, XSLT keyed by rule name, etc.). That's fine for
   read-only sources, but downstream layers re-derive the relative
   path from a `RuleFile` or `CatalogItem` in multiple places. A
   small `AssetKey` (`SpecRef` + relative path) value type would
   make caches well-typed and impossible to mis-key.

### 1.6 Recommended target shape (Segment 1)

**Two changes, both backward-compatible.**

#### A. Add a thin "bytes / string" surface above `IAssetSource`

```csharp
namespace EncDotNet.S100.Core;

/// <summary>
/// Read-once view of a small asset (Lua source, XSLT, SVG, palette XML).
/// Backed by a <see cref="ReadOnlyMemory{Byte}"/> so callers can wrap it
/// in a <see cref="MemoryStream"/> or decode it as text without copies.
/// </summary>
public readonly record struct AssetBytes(
    ReadOnlyMemory<byte> Bytes,
    string RelativePath)
{
    public Stream AsStream() => new ReadOnlyMemoryStream(Bytes);  // small helper
    public string AsString(System.Text.Encoding? encoding = null) =>
        (encoding ?? System.Text.Encoding.UTF8).GetString(Bytes.Span);
}

public static class AssetSourceExtensions
{
    /// <summary>
    /// Reads the asset fully into memory. Default implementation
    /// allocates one byte array per call; the caching decorator
    /// returns a shared buffer.
    /// </summary>
    public static async Task<AssetBytes> ReadAllBytesAsync(
        this IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = await source.OpenAsync(relativePath, cancellationToken);
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var seg))
            return new AssetBytes(seg.AsMemory(), relativePath);
        using var copy = new MemoryStream(capacity: 4096);
        await stream.CopyToAsync(copy, cancellationToken);
        return new AssetBytes(copy.ToArray(), relativePath);
    }
}
```

This adds zero allocations on the cache-hit path (the decorator just
returns a stored `AssetBytes`), keeps `IAssetSource` minimal, and
doesn't break any existing caller.

#### B. Provide a `CachingAssetSource` decorator

```csharp
namespace EncDotNet.S100.Core;

/// <summary>
/// Wraps another <see cref="IAssetSource"/> and memoises the bytes of
/// each asset on first read. Intended for read-only sources whose
/// contents are immutable for the lifetime of the source
/// (<see cref="EmbeddedAssetSource"/>, packaged exchange sets read in
/// <c>ZipArchiveMode.Read</c>, on-disk spec content). The cache is
/// thread-safe and bounded by the number of distinct relative paths
/// requested.
/// </summary>
public sealed class CachingAssetSource : IAssetSource
{
    private readonly IAssetSource _inner;
    private readonly ConcurrentDictionary<string, Lazy<Task<AssetBytes>>> _cache =
        new(StringComparer.Ordinal);

    public CachingAssetSource(IAssetSource inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public Task<Stream> OpenAsync(string relativePath, CancellationToken ct = default)
        => GetAsync(relativePath, ct).ContinueWith(t => t.Result.AsStream(), ct);

    public Task<AssetBytes> GetAsync(string relativePath, CancellationToken ct = default)
        => _cache.GetOrAdd(relativePath,
                p => new Lazy<Task<AssetBytes>>(
                    () => _inner.ReadAllBytesAsync(p, ct),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    public void Dispose() => _inner.Dispose();
}
```

- Key is `string` (the relative path) — that's how the upper layers
  already key. No premature `AssetKey` struct.
- `Lazy<Task<AssetBytes>>` collapses concurrent first-read storms to
  a single underlying open.
- Bounded by call-site cardinality (~10s of paths per spec); no
  eviction needed.
- For `ZipAssetSource` this *also* fixes the thread-safety hole: the
  archive is touched at most once per path, then results come from
  the memo.

#### C. Memoise `EmbeddedAssetSource` case-insensitive manifest lookup

Trivial follow-up (PR-5 above). Lazy `Dictionary<string, string>`
mapping ordinal-ignore-case name → manifest resource name, built on
first miss.

### 1.7 Where the caching decorator gets plugged in

There are three call sites that own the asset-source lifecycle, all
already pass through `PortrayalCatalogueManager` / `FeatureCatalogueManager`
/ `Specification`:

| Site | Today | After |
|------|-------|-------|
| `Specification.OpenPortrayalCatalogueSource` | `return EmbeddedAssetSource.Create(asm, prefix);` | `return new CachingAssetSource(EmbeddedAssetSource.Create(asm, prefix));` |
| `Specification.OpenFeatureCatalogueSource` | same | same |
| `PortrayalCatalogueManager.SetSource(specRef, source)` (callers passing in an exchange-set source) | uses as-given | optionally wrap if not already a `CachingAssetSource` |

Wrapping in `Specification.*` is the smallest surface that gets us
the cache "for free" everywhere catalogues come from bundled embedded
resources, which is the dominant case.

For exchange-set assets opened by viewer-loaded datasets, the wrap
happens in the data-loader pathway when a `ZipAssetSource` is built;
the viewer owns that policy.

### 1.8 What changes for upper layers

**Nothing has to change immediately.** The decorator is invisible
through `IAssetSource.OpenAsync`. Once it's in place, the
`*PortrayalCatalogue` and `*LuaRuleExecutor` types can be migrated
*incrementally* to either:

- Continue using `OpenAsync` + `StreamReader` (no behavioural
  difference, but bytes are reused under the hood), **or**
- Migrate to `((CachingAssetSource)source).GetAsync(path)` for sites
  that want zero-allocation `AssetBytes`.

Most of segment 3 (per-spec wrappers) does its own further caching
of the *parsed* result anyway, so the decorator's value is mainly:

1. Eliminating redundant raw-stream opens on cold-miss paths
   (`S101LuaRuleExecutor.LoadRuleSource` is the standout — see
   Segment 4).
2. Fixing the latent `ZipArchive` thread-safety hole for free.
3. Giving us a single, well-defined seam where bytes-level caching
   lives so we stop sprinkling it ad-hoc in every wrapper.

### 1.9 Segment 1 PR mapping

- **Adds one new PR** (call it PR-0 since it sequences before PR-1/2/3):
  ship `AssetBytes` + `AssetSourceExtensions.ReadAllBytesAsync` +
  `CachingAssetSource`, wire it into `Specification.Open*Source`. No
  behaviour change — just a memoisation seam.
- **Subsumes PR-5** (manifest fallback memoisation) — it stays a
  follow-up but becomes mostly moot once cache hits dominate.
- **Unblocks PR-2** (Lua source caching) — the Lua executor can
  `_provider.Source.ReadAllBytesAsync("Rules/main.lua")` and the
  decorator handles repeat reads. (Though we'll see in Segment 4
  that caching at the wrapper level is still nicer for clarity.)

### 1.10 Open questions for Segment 1

1. **Do we expose `AssetBytes` from `IAssetSource` directly, or keep
   it as an extension method?** Extension is less disruptive; direct
   interface method allows implementations (especially
   `EmbeddedAssetSource`) to skip the copy and return a
   `ReadOnlyMemory<byte>` view of the assembly resource directly.
   Recommendation: **extension first, promote to interface method
   later if profiling shows the copy matters.**
2. **Eviction policy.** Bundled content is small and immutable;
   unbounded growth is bounded by manifest size (~hundreds of
   entries). For exchange-set ZIP sources this could grow with usage
   — but a viewer session typically opens a small number of datasets
   and closes the source on unload, which disposes the decorator
   too. Recommendation: **no eviction; size is naturally bounded.**
3. **`CancellationToken` semantics on cache hits.** Hits return
   immediately and ignore the token; misses honour it. Standard
   pattern. No change.

---

## Segment 2 — Catalogue identity & parse

This segment is the layer the audit found "mostly already correct" —
the layer that exists today because of PRs #43/#44/#48. The
deep-dive's job here is to verify that the design holds up under
load and to flag the two specific defects that came out of reading
the code carefully.

### 2.1 Type inventory

| Type | Kind | Mutability | Cost to produce | Thread-safe? | Identity / cache key |
|------|------|------------|-----------------|--------------|----------------------|
| `EncDotNet.S100.Core.SpecRef` | `readonly record struct` (Name + Edition) | immutable | trivial; `SpecName.Normalize` only | yes (value type) | what callers know at dataset-open time (from `productSpecification` HDF5 attr, GML namespace, ISO 8211 DSSI, or exchange-set CATALOG.XML) |
| `EncDotNet.S100.Core.CatalogueRef` | `readonly record struct` (Name + Version) | immutable | trivial | yes | what the catalogue *itself* declares (read-side; for diagnostics & spec-match policies) |
| `EncDotNet.S100.Core.SpecVersion` | `readonly record struct` (semver triple) | immutable | trivial | yes | — |
| `EncDotNet.S100.Core.ICatalogueProvider<T>` | interface | n/a | n/a | contract: must be thread-safe | input `SpecRef` → output `T?` |
| `EncDotNet.S100.Features.FeatureCatalogue` | sealed class (parsed XML tree) | immutable after `FeatureCatalogueReader.Read` | XML parse + decode-tree build (5–15 ms for a real FC) | yes (immutable) | self-describing via `CatalogueRef` property |
| `EncDotNet.S100.Features.FeatureCatalogueDecoder` | class built from `FeatureCatalogue` | immutable in practice | trivial (just builds lookup dicts over the FC) | yes (read-only dictionaries) | derived from a `FeatureCatalogue` |
| `EncDotNet.S100.Portrayals.PortrayalCatalogue` | sealed class (parsed XML metadata) | immutable after `PortrayalCatalogueReader.Read` | XML parse (1–5 ms) | yes | self-describing via `CatalogueRef` |
| `EncDotNet.S100.Portrayals.PortrayalCatalogueProvider` | sealed class, `IDisposable`; holds `IAssetSource` + `PortrayalCatalogue` | the catalogue is immutable; the source is not directly mutated but is *owned and disposed* | one `source.OpenAsync("portrayal_catalogue.xml")` + parse | yes for *reads* on the catalogue; `_source.OpenAsync` thread-safety depends on the source (see Segment 1) | the value held by `PortrayalCatalogueManager` |
| `EncDotNet.S100.Features.FeatureCatalogueManager` | sealed class; `ICatalogueProvider<FeatureCatalogue>` | dict-mutating; values are immutable | parse cost amortised | **yes (ConcurrentDictionary + Lazy)** | `SpecRef` |
| `EncDotNet.S100.Portrayals.PortrayalCatalogueManager` | sealed class, `IDisposable`; `ICatalogueProvider<PortrayalCatalogueProvider>` | dict-mutating | parse + provider-construct cost amortised | **partial — see §2.4** | `SpecRef` |

### 2.2 Identity & key choices

- **The two managers cache by `SpecRef`, not `CatalogueRef`.** This
  matches the lookup pattern: at dataset-open time the caller knows
  the *spec edition* the dataset declares, not the catalogue version
  that will be parsed. `CatalogueRef` is exposed for the *read* side
  (`AvailableCatalogues`, plus the catalogue's self-description) so
  callers can run match policies like "accept any catalogue whose
  CatalogueRef name == SpecRef name and version >= 2.0.0".
- **Consequence:** at most one catalogue per `(SpecName, SpecVersion)`
  slot. Two FCs targeting the same product spec edition cannot
  coexist in the same manager. This is fine — there is no real use
  case in the codebase that requires it, and the design comment on
  `CatalogueRef` already calls out the orthogonality.
- **`default(SpecVersion)` is the "edition unspecified" slot.** Both
  string-based overloads route to it. Callers that don't yet know
  the dataset's edition share that single slot per product name.
  Acceptable; the viewer is mostly in this mode today.

### 2.3 How instances live today

| Type | Lifetime | Constructed where |
|------|----------|-------------------|
| `PortrayalCatalogueManager` | **viewer singleton** | `App.axaml.cs:113` — `services.AddSingleton<PortrayalCatalogueManager>()` |
| `FeatureCatalogueManager` | **per `DatasetPipelineFactory`** | `DatasetPipelineFactory.cs:56` |
| `DatasetPipelineFactory` | **per `DatasetLoaderService` load** | `Services/DatasetLoaderService.cs:134` — a new factory is built when the loader configures itself for a session |
| `PortrayalCatalogueProvider` | **per `SpecRef` slot inside PC manager** | `PortrayalCatalogueManager.GetProvider`, `:124` and `:162` |

### 2.4 Defects identified in this layer

#### Defect A — `PortrayalCatalogueManager` has a double-build / leaked-provider race

```csharp
// PortrayalCatalogueManager.cs:139-165
if (_providers.TryGetValue(spec, out var cached)) return cached;
// ... path lookup ...
var source = FileSystemAssetSource.Create(path);
var provider = PortrayalCatalogueProvider.OpenAsync(source).GetAwaiter().GetResult();
_providers[spec] = provider;
return provider;
```

Two threads missing the cache concurrently will each parse a fresh
`PortrayalCatalogueProvider`. The last write wins; the loser
silently leaks its provider — **including the wrapped `IAssetSource`,
which is never `Dispose`d**. With PR-0 in play, that source is a
`CachingAssetSource` so the leak now includes the bytes cache it
accumulated. Compare to `FeatureCatalogueManager.GetCatalogue` (§88)
which already uses `Lazy<T?>` and is race-free.

#### Defect B — `FeatureCatalogueManager` cache is wiped on every dataset load

`DatasetPipelineFactory` constructs its own `FeatureCatalogueManager`
in its constructor (line 56). `DatasetLoaderService` rebuilds the
factory each time it reconfigures (line 134). Result: the FC parse
cache is per-load, not per-app-session. Parse cost is small (single
digit ms per FC) but it's a clear inconsistency with PC manager
treatment and undoes most of PR #44's benefit in the viewer's hot
path.

### 2.5 Other smaller issues

1. **PC manager exposes a path-based config (`SetPath`) but FC
   manager doesn't.** Asymmetric API surface. Not a defect — FCs are
   single files and PCs are trees — but means PR-0 wrapping
   `CachingAssetSource` around bundled content works for PC but not
   FC. (Same as PR-0's deferred follow-up.)
2. **`FeatureCatalogueManager` resolver is `Func<string, Stream?>` /
   `Func<SpecRef, Stream?>`.** Stream resolvers cannot be wrapped by
   `CachingAssetSource`. To plug FC into PR-0 cleanly the manager
   needs an `IAssetSource` (or `Func<SpecRef, IAssetSource?>`)
   constructor overload.
3. **`Sync-over-async` on `PortrayalCatalogueProvider.OpenAsync`** in
   `PortrayalCatalogueManager.GetProvider` (lines 124, 162). Today
   all asset sources are sync-completing, so this is benign — but if
   `CachingAssetSource` ever gets a true-async miss path (unlikely
   for spec-bundled content), this should be revisited.
4. **`AvailableCatalogues` on `FeatureCatalogueManager` deliberately
   skips unforced `Lazy` entries (line 160).** Correct and matches
   the doc — just worth noting that PC manager's equivalent
   (lines 217-231) materialises the providers, which is fine because
   they're always force-loaded by the time anyone iterates.
5. **No eviction API.** Neither manager has a "drop spec X" call.
   PC manager evicts implicitly on `SetPath` / `SetSource` (and
   disposes). FC manager never evicts. Today's viewer never
   reconfigures specs at runtime so this isn't pressing.

### 2.6 What's working well

- **`SpecRef` parse coverage.** Handles all three real-world forms
  (`S-101/1.2.0`, `S-101@1.2.0`, `INT.IHO.S-101.1.2.0`). Verified by
  `SpecRefTests` and the parsers in `S102DatasetReader`,
  `S111DatasetReader`, exchange-set readers.
- **`SpecRef` and `CatalogueRef` correctly distinct.** Documented in
  both types; tested by the existing test suite.
- **`ICatalogueProvider<T>` is the right abstraction.** Two
  implementations exist, both satisfy the contract, both are used
  polymorphically (e.g. `DatasetPipelineFactory` could be refactored
  to take `ICatalogueProvider<FeatureCatalogue>` and
  `ICatalogueProvider<PortrayalCatalogueProvider>` directly — that
  refactor would let the FC manager be a viewer singleton without
  more boilerplate).
- **Catalogues self-describe via `CatalogueRef`.** So once a callable
  spec-match policy lands, no extra plumbing is needed.

### 2.7 Recommended changes for Segment 2

Three concrete PRs, all small. None depend on PR-0 being merged.

#### PR-A: Fix `PortrayalCatalogueManager` double-build race

Replace `ConcurrentDictionary<SpecRef, PortrayalCatalogueProvider>`
with `ConcurrentDictionary<SpecRef, Lazy<PortrayalCatalogueProvider>>`.
Use `LazyThreadSafetyMode.ExecutionAndPublication`. On `SetPath` /
`SetSource` eviction, force-materialise the old `Lazy` only if
`IsValueCreated`, dispose, and replace. Mirror the
`FeatureCatalogueManager` shape exactly.

Test: thread-storm test (parallel `GetProvider` for the same spec)
asserting `PortrayalCatalogueProvider.OpenAsync` is called exactly
once and the asset source is opened exactly once.

#### PR-B: Hoist `FeatureCatalogueManager` to a singleton

Two-step:

1. Add `services.AddSingleton<FeatureCatalogueManager>()` (with the
   appropriate FC resolver factory delegate).
2. Inject it into `DatasetPipelineFactory`'s constructor instead of
   the manager constructing its own. The factory's
   `featureCatalogueResolver` parameter goes away.

Test: assert that two dataset loads of the same spec only hit the
resolver once.

#### PR-C: Add `IAssetSource`-shaped FC manager configuration

Add a parallel surface to FC manager that mirrors PC manager:

```csharp
// In FeatureCatalogueManager
public void SetSource(SpecRef spec, IAssetSource source);
public void SetSource(string productSpec, IAssetSource source);
// New private parse path that opens "FeatureCatalogue.xml" on the source.
```

Existing `Func`-based constructors stay; the new surface is
additive. Now `CachingAssetSource` wrapping inside
`Specification.OpenFeatureCatalogueSource` (deferred PR-0
follow-up) becomes effective.

Optional: deprecate the `Func<string, Stream?>` constructor in
favour of the `IAssetSource` path once internal callers migrate.

### 2.8 Out of scope for Segment 2

- Switching managers from `SpecRef`-keyed to `CatalogueRef`-keyed.
  No use case justifies it today.
- Adding spec-match policy enforcement at the manager boundary
  (the audit's §6 "spec-match policy" item — defer).
- Hot-reload / file-watcher integration. Spec content is bundled
  resources; on-disk PC paths change rarely.

### 2.9 Segment 2 PR mapping into the audit's ranked plan

The original audit §5 had eight PRs. Segment 2 adds (or splits out)
three smaller PRs that don't fit cleanly into the existing rank:

| New PR | Title | Audit rank context | Impact |
|--------|-------|--------------------|--------|
| PR-A | Race-free PortrayalCatalogueManager | Correctness — should go before any further caching work; concurrent renders today are rare but the PR-0 cache makes orphaned-source leaks worse | High (correctness), Low (effort) |
| PR-B | FeatureCatalogueManager as singleton | Subsumes part of audit PR-1; reorder | Medium (perf), Low (effort) |
| PR-C | IAssetSource FC manager surface | Subsumes the PR-0 deferred FC follow-up | Medium (consistency), Low (effort) |

Recommendation: **ship PR-A first** (pure bug fix, no API change),
then PR-B and PR-C in either order.

### 2.10 Open questions for Segment 2

1. **Should `DatasetPipelineFactory` take `ICatalogueProvider<…>`
   instead of the concrete managers?** Pro: cleanly decouples
   factory lifetime from manager lifetime, makes test doubles easy.
   Con: existing call sites (viewer DI) pass the concrete managers
   directly. Recommendation: **add ICatalogueProvider overloads in
   PR-B, keep concrete overloads for back-compat.**
2. **Should `PortrayalCatalogueManager.SetSource` `lock`-on-replace
   to fence concurrent `GetProvider` callers against eviction?**
   Worth doing in PR-A while we're already touching the dict.
   Lazy + replace under a small spinlock or `lock(_sync)` is the
   normal pattern.
3. **Should FC resolver overloads stay forever, or be deprecated?**
   Recommendation: **stay** — they're useful for tests and tooling
   that hand-craft streams. The `IAssetSource` overload (PR-C) is
   purely additive.

---

*Next segment to drill into (when requested): Segment 3 — Per-spec
portrayal wrappers (the largest payoff segment — `IVectorPortrayalCatalogue`
implementations, per-dict-per-instance vs per-spec caching).*

