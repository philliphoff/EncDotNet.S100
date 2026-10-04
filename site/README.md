# soundcharts.app

The landing page for SoundCharts and the EncDotNet.S100 SDK. It is a static
[Astro](https://astro.build) site. The [Docs workflow](../.github/workflows/docs.yml)
builds it together with the DocFX docs and deploys both to GitHub Pages:

| Path | Content |
|---|---|
| `/` | This site |
| `/docs/`, `/api/`, `/src/`, … | DocFX output, at the same paths as before |

## Working on it

Needs Node 22.12 or later.

```bash
cd site
npm ci
npm run dev
```

The dev server shows **review mode**: every image slot is labelled with its
shot ID from [shot-list.md](shot-list.md). Green means captured, light yellow
means a stand-in, and bright yellow means still to capture. On a deployed build,
add `?review` to the URL to see the same labels.

`npm run build` writes `dist/`. Links into the docs (`/docs/...`) only work once
the output is merged with DocFX's `_site/`, as the workflow does:

```bash
docfx docfx.json && (cd site && npm run build) && cp -R site/dist/. _site/
```

## Layout

| Path | What it holds |
|---|---|
| `src/pages/index.astro` | The page, made up of the section components |
| `src/components/` | One component per section. `Shot.astro` is the image slot. |
| `src/data/content.ts` | Product tiles, standards list, links |
| `src/data/release.ts` | Latest release assets, fetched at build time for the download button |
| `src/assets/shots/` | Final captures, named `<ID>.png`, plus `<ID>.light.png` twins (see the shot list) |
| `shot-list.md` | What to capture for each shot ID, and how |

## Images

A `<Shot id="H1" standin="Example.png" … />` resolves, in order, to:

1. `src/assets/shots/H1.png`, if it exists
2. the named stand-in from the repo's `readme/` or `docs/images/`
3. a drawn placeholder describing the shot

When a capture also has a `H1.light.png` twin (Light chrome), the slot follows the
visitor's system theme: the light image by default, `H1.png` (Dark chrome) under
`prefers-color-scheme: dark`. Shots without a twin show the same image in both.

The build turns images into responsive AVIF with a WebP fallback, and drops the
full-size source copies. Captures come out at 2× (2200 × 1400 for window shots); let the
build handle sizing.

## Capturing shots

[`capture/capture.cs`](capture/capture.cs) drives a Release build of the viewer
over MCP and writes `src/assets/shots/<ID>.png`. It downloads public NOAA data
(ENCs from charts.noaa.gov, S-102/S-111 from NOAA's AWS buckets) into
`capture/.cache/` on first use, and runs the viewer in a throwaway `--data-dir`
with en-US formatting, dark chrome (S-100 Dusk / Night chrome for the Dusk and Night
shots) and no status bar.

```bash
dotnet build -c Release src/EncDotNet.S100.Viewer
dotnet run site/capture/capture.cs -- --only H1,F4
dotnet run site/capture/capture.cs -- --theme light   # the Light-chrome twins
```

`--theme light` captures only the Day-palette window shots (hero H1, the feature
rows and F7-day) again with Light chrome, as `<ID>.light.png` and manifest entries
`<ID>.light`. The Dusk and Night shots use the matching S-100 Dusk / Night chrome in
both themes, map-only product tiles have no chrome, and the D2 clip stays dark.

The viewer needs a real display, so run it outside any sandbox.

Each capture also updates `src/assets/shots/manifest.json`: what every shot shows
(datasets with source, licence, edition or forecast run), the viewer calls that set
it up, and when it was taken. `--manifest-only` refreshes the record without
replacing images. The `D2` clip also
needs `ffmpeg` on PATH; it writes `public/clips/D2.{mp4,webm}`, and `Clip.astro`
plays it with the `D2` shot as the poster.

## Deployment notes

- `SITE_URL` / `SITE_BASE` come from `actions/configure-pages`, so the same
  build works on `philliphoff.github.io/EncDotNet.S100` and, once the custom
  domain is set, on `soundcharts.app`.
- The workflow also runs after a tagged CI run, so the download button follows
  new releases.
- `package-lock.json` resolves against the public npm registry. If your npm is
  set up to use a mirror, rewrite the `resolved` URLs back to
  `https://registry.npmjs.org/` before committing.
