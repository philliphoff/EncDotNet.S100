# A Blazor UI for `s100 tiles serve`: feasibility spike

**Status:** spike. Two prototypes, neither shipped:

- **Server-side Razor components**: `tools/EncDotNet.S100.Cli/Web`, served by
  `tiles serve` at `/ui`. The existing page at `/` is unchanged.
- **Blazor WebAssembly**: `spikes/EncDotNet.S100.WebApp`, a standalone app. It isn't
  in the solution or in CI.

**Question:** `s100 tiles serve` shows a small MapLibre page, one embedded HTML
string in `TileServer.cs`. Could that page become a real web app written in C# that
uses our own libraries?

**Short answer:** yes. **Start with server-side Razor components hosted in the CLI.**
The browser downloads about 60 KB. The components call the tile source and our
libraries in-process, at native speed, with no new API. Switch to WebAssembly only
for a feature that has to run in the browser.

---

## 1. Server-side Razor components (recommended)

The serve host is already ASP.NET Core (Kestrel), so the components go into the CLI:

- **`ServeApp.razor`** (`@page "/ui"`) is the page's HTML document.
- **`ServePage.razor`** is an interactive-server component inside it.
- **`ServeWeb.cs`** registers them and adds the routes to `TileServer`.

`ServePage` gets the served `ITileSource` from dependency injection. It shows the
datasets, a palette picker and a time slider. Clicking the map runs
`ITileSource.FeatureQuery` (`DatasetFeatureQuery`) **in-process** and lists the
features at that point. MapLibre draws the map through about 40 lines of JS; the
component holds all the state.

Checked in the in-app browser with three IC-ENC GB S-101 cells:

- The datasets list fills, and clicking a dataset zooms the map to it.
- The night palette works.
- Clicking near a lateral buoy listed 11 features, the buoy first, in **129 ms**.
- No console errors.
- The page also works from a `dotnet publish` build.
- The CLI tests pass.

### What the browser downloads

| Resource | Transfer |
|---|---|
| `/ui` (rendered on the server) | 3.7 KB |
| `_framework/blazor.web.js` (gzip) | 55 KB |
| Circuit setup (`negotiate`, `initializers`) | 0.8 KB |
| **Total** | **about 60 KB**, against about 2.5 MB for WebAssembly |

MapLibre and the OSM basemap come from the internet, as with the current page.

### Changes the CLI needed

1. **Web SDK.** The CLI project changes from `Microsoft.NET.Sdk` to
   `Microsoft.NET.Sdk.Web`. `blazor.web.js` comes from the
   `Microsoft.AspNetCore.App.Internal.Assets` package, and that package adds it only
   when `UsingMicrosoftNETSdkWeb` is true. The Razor SDK alone, even with
   `RequiresAspNetWebAssets`, produces an empty asset manifest. The Web SDK also adds
   nine ASP.NET implicit usings, which make the CLI's existing explicit `using`s
   redundant and fail CI's IDE0005 format check. No property turns them off, so the
   project removes them with `<Using Remove="…"/>`. The explicit `FrameworkReference`
   goes too: the Web SDK implies it and warns about a duplicate (NETSDK1086).
   Publishing now also writes a `web.config` (for IIS); turn that off before shipping.
2. **Static assets outside Development.** The serve host runs in the Production
   environment, which only reads the build's static asset manifest when asked to.
   `builder.WebHost.UseStaticWebAssets()` lets `dotnet run` and test builds find
   `blazor.web.js`. A published CLI reads it from `wwwroot/_framework/`, next to the
   binary. Self-contained per-RID archives would need the same `wwwroot`; not
   checked yet.
3. **Explicit manifest path.** `MapStaticAssets()` looks for a manifest named after
   the host's application. Under the test host that's the test assembly, so it threw
   and failed 72 serve tests. The spike passes the CLI's own manifest path and skips
   the mapping when the file is missing.

### Trimming

The CLI runs the trim analyzers as a gate (#764), although it isn't published
trimmed.

- `AddRazorComponents()` and `AddInteractiveServerComponents()` are marked
  `[RequiresUnreferencedCode]`: server-side Blazor doesn't support trimming. The
  spike suppresses that at its one call site.
- Razor's generated code for `<Router>` raises IL2110/IL2111. With one page there's
  no `Router`: the page is the root component. A real UI with several pages needs
  the `Router`. Then either the CLI suppresses those two warnings project-wide, or
  the components move into their own Razor class library that doesn't run the trim
  analyzer and sees the serve data through an interface.

### Not done in the spike

- **`--token` path prefix:** the UI is mapped only when there's no token. With a
  token, the routes and `<base href>` need the `/{token}/` prefix, for example by
  mapping the UI in an `app.Map(prefix, …)` branch.
- **Circuit cost:** each open tab keeps a SignalR connection and its component state
  on the server. That's fine on localhost and a LAN. With `--host 0.0.0.0` and many
  viewers, keep the interactive islands small and render the rest as static SSR.
- **Offline:** MapLibre still loads from a CDN.

## 2. Blazor WebAssembly

A standalone app with two pages:

- **Served:** a thin client over the serve HTTP API (`tiles.json`, `/datasets`). It
  worked with no server changes, because the API already sends
  `Access-Control-Allow-Origin: *`.
- **Open locally:** parses an S-101 `.000` in the browser with
  `EncDotNet.S100.Datasets.S101`.

### Our libraries run in the browser unchanged

`Core`, `Features`, `Portrayals`, `Datasets.S101` and `EncDotNet.Iso8211` built for
`browser-wasm` and ran with no code changes. Our assemblies are 14–24 KB each after
trimming and brotli compression.

### First load is about 2.5 MB

`_framework` totals 2.9 MB brotli. That figure counts all three ICU shards, but a
browser downloads only one. The .NET runtime is most of it: `dotnet.native.wasm` is
0.98 MB and `System.Private.CoreLib` 0.52 MB.

### About 20× slower than native

Parsing `101GB00GB302045.000` (1.9 MB, 3,386 features):

| | Parse | Metadata |
|---|---|---|
| Native, net10.0 Release (warm) | 80 ms | 0–7 ms |
| WASM interpreter, cold | 2,516 ms | 67 ms |
| WASM interpreter, warm | 1,708 ms | 49 ms |

The browser runs WebAssembly on one thread, so a parse blocks the UI. AOT
compilation would close most of the gap but needs the `wasm-tools` workload, which
isn't installed here. **AOT was not measured.**

### Build friction

- The WASM runtime packs must match the SDK's runtime version, and they hit the
  local 7-day NuGet proxy lag.
- Razor's generated code raises IL2110/IL2111, as above.

### Not tested

SkiaSharp in WASM (needs `wasm-tools` and adds megabytes), Lua portrayal, HDF5
products, threading and SQLite.

## 3. Comparison

| | Server-side Razor in the CLI | WebAssembly thin client |
|---|---|---|
| Download | About 60 KB | About 2.5 MB once, then cached |
| Uses our libraries | All of them, in-process, at native speed | In the browser, about 20× slower; otherwise through the HTTP API |
| New API needed for a feature | No | Yes, unless the feature runs in the browser |
| Build changes | CLI on the Web SDK; static asset wiring | A separate WASM project; runtime packs; hosting its `wwwroot` from the CLI |
| Works without a serve instance | No | Yes (opening local files, hosting on soundcharts.app) |
| Server cost | A SignalR circuit per open tab | Static files |

## Recommendation

**Go with server-side Razor components in the CLI.** They're simpler and lighter,
and our libraries are right there.

To keep a later switch to WebAssembly cheap:

- Components read the serve data through a small interface (datasets, palettes,
  times, feature query), not `ITileSource` directly.
- Interactive parts stay in a few components, so each can later move to
  `InteractiveWebAssembly` or `InteractiveAuto` on its own.

Switch to WebAssembly when a feature has to run in the browser: opening a user's
local file without uploading it, working offline, or the page hosted apart from a
serve instance.

### To ship it

1. **Packaging:** decide whether the CLI moves to the Web SDK (as in the spike) or
   the components go into a Razor class library (which also isolates the trim
   suppressions). Turn off `web.config`, and check the `wwwroot` in the per-RID
   archives.
2. **Routing:** replace the page at `/` (keep it while `--no-viewer` is off), and
   support the `--token` prefix.
3. **First features beyond today's page:**
   - dataset coverage outlines
   - feature info shown with catalogue names (the S-101 attribute records still
     need decoding)
   - add and remove datasets with `--manage-datasets`
   - switching layers
   - the time slider for S-104/S-111
4. **Offline:** ship MapLibre with the CLI.
