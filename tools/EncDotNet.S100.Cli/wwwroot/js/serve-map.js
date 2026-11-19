// The map of the tiles serve web page (issues #1575, #1585). The Razor
// components own the state; this owns the MapLibre map, the furniture the map
// moves (scale bar, readout, compass), and the URL hash's view. URLs are
// relative to the page.
//
// Zooms crossing to and from the components are the tile set's: the XYZ zoom
// of 256-pixel tiles, as TileJSON and GIS clients count. MapLibre counts
// 512-pixel tiles, one zoom less.
window.s100Map = (() => {
  const ZOOM_OFFSET = 1;
  const NMI = 1852;
  const OSM_TILES = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

  let map;
  let dotnet;
  let options;
  let hashParameters = "";
  let theme = "auto";
  let resizeObserver;
  let darkQuery;
  let mouseOver = false;
  let loaded = false;
  let outlinesUntil = 0;

  // The Datasets panel's outlines (SW2, D9, D15): the row under the pointer,
  // the selected dataset, and the matches of a search or filter (null for none).
  const outlines = { hover: null, selected: null, matches: null, flash: null };

  // muted, osm or none (D17, Q4); OpenStreetMap falls back to none when its tiles don't load.
  let basemap = "muted";
  let basemapLoaded = false;
  let basemapFailTimer;

  // On a high-DPI screen, the 512-pixel @2x tiles when they're served.
  let highDpi = false;
  const absolute = url => {
    const resolved = new URL(url, document.baseURI);
    if (highDpi) resolved.searchParams.set("scale", "2");
    return resolved.href;
  };

  const pageZoom = () => map.getZoom() + ZOOM_OFFSET;
  const token = name => getComputedStyle(document.querySelector(".serve") ?? document.documentElement).getPropertyValue(name).trim();
  const effectiveTheme = () => theme === "auto" ? (darkQuery?.matches ? "dark" : "light") : theme;

  // 50°46.5′N 001°13.9′W
  const degreesMinutes = (value, positive, negative, width) => {
    const hemisphere = value < 0 ? negative : positive;
    let v = Math.abs(value);
    let degrees = Math.floor(v);
    let minutes = (v - degrees) * 60;
    if (minutes >= 59.95) { degrees += 1; minutes = 0; }
    return `${String(degrees).padStart(width, "0")}°${minutes.toFixed(1).padStart(4, "0")}′${hemisphere}`;
  };

  function showPosition(lngLat) {
    const position = document.querySelector("[data-readout-position]");
    if (position) {
      const lng = ((lngLat.lng + 540) % 360) - 180;
      position.textContent = `${degreesMinutes(lngLat.lat, "N", "S", 2)} ${degreesMinutes(lng, "E", "W", 3)}`;
    }
  }

  function showZoom() {
    const zoom = document.querySelector("[data-readout-zoom]");
    if (zoom) zoom.textContent = `z${pageZoom().toFixed(1)}`;
  }

  // A bar of a round number of nautical miles, at most 120 px.
  const scaleSteps = [0.01, 0.02, 0.05, 0.1, 0.2, 0.25, 0.5, 1, 2, 5, 10, 20, 25, 50, 100, 200, 500, 1000, 2000, 5000];
  function showScale() {
    const line = document.querySelector("[data-scale-line]");
    const label = document.querySelector("[data-scale-label]");
    if (!line || !label) return;
    const lat = map.getCenter().lat * Math.PI / 180;
    const metresPerPixel = 40075016.686 * Math.cos(lat) / (512 * 2 ** map.getZoom());
    let step = scaleSteps[0];
    for (const s of scaleSteps) if (s * NMI / metresPerPixel <= 120) step = s;
    line.style.width = `${Math.round(step * NMI / metresPerPixel)}px`;
    label.textContent = `${step} nmi`;
  }

  function showBearing() {
    const needle = document.querySelector("[data-compass-needle]");
    if (needle) needle.style.transform = `rotate(${-map.getBearing()}deg)`;
  }

  // The view in MapLibre's hash form, with the tile set's zoom: zoom/lat/lng[/bearing].
  function formatView() {
    const zoom = pageZoom();
    const center = map.getCenter();
    const precision = Math.max(0, Math.ceil((zoom * Math.LN2 + Math.log(256 / 360 / 0.5)) / Math.LN10));
    const round = n => Math.round(n * 100) / 100;
    let view = `${round(zoom)}/${center.lat.toFixed(precision)}/${center.lng.toFixed(precision)}`;
    const bearing = map.getBearing();
    if (Math.abs(bearing) >= 0.01) view += `/${round(bearing)}`;
    return view;
  }

  // replaceState, so Back leaves the page rather than stepping through pans (D36).
  function writeHash() {
    const hash = `#${formatView()}${hashParameters ? "&" + hashParameters : ""}`;
    if (location.hash !== hash) history.replaceState(history.state, "", hash);
  }

  // The basemap: muted (Q4) or in its own colours, dimmed for dark chrome,
  // inverted and dimmed for night whichever is chosen (D18), or hidden; the outlines' colours.
  function basemapPaint() {
    const t = effectiveTheme();
    const muted = basemap !== "osm" || t === "night";
    const brightness = t === "night" ? [0.22, 0] : t === "dark" ? [0, muted ? 0.6 : 0.75] : [muted ? 0.1 : 0, 1];
    return {
      "raster-saturation": muted ? -1 : t === "dark" ? -0.3 : 0,
      "raster-contrast": !muted ? 0 : t === "light" ? -0.25 : -0.1,
      "raster-brightness-min": brightness[0],
      "raster-brightness-max": brightness[1],
    };
  }

  function applyTheme() {
    // isStyleLoaded() is false while a source reloads; the layers are there once the map has loaded.
    if (!map || !loaded) return;
    map.setLayoutProperty("osm", "visibility", basemap === "none" ? "none" : "visible");
    for (const [name, value] of Object.entries(basemapPaint())) map.setPaintProperty("osm", name, value);
    map.setPaintProperty("coverage-casing", "line-color", token("--background") || "#ffffff");
    map.setPaintProperty("coverage-line", "line-color", token("--outline-served") || "#3D8A5A");
    applyDatasetTheme();
    applyTileSetTheme();
    window.s100Pick?.applyTheme();
  }

  // Hovered and selected outlines are 2 px in the accent with a 6% fill (8%
  // selected), on a casing in the page background (D15).
  function addDatasetLayers() {
    const none = ["in", ["get", "id"], ["literal", []]];
    map.addLayer({ id: "coverage-focus-fill", type: "fill", source: "coverage", filter: none, paint: { "fill-color": "#2563EB", "fill-opacity": 0.06 } });
    map.addLayer({ id: "coverage-focus-casing", type: "line", source: "coverage", filter: none, paint: { "line-color": "#ffffff", "line-opacity": 0.6, "line-width": 4 } });
    map.addLayer({ id: "coverage-focus-line", type: "line", source: "coverage", filter: none, paint: { "line-color": "#2563EB", "line-width": 2 } });
    applyDatasetOutlines();
  }

  function applyDatasetTheme() {
    if (!map?.getLayer("coverage-focus-line")) return;
    const accent = token("--selected") || "#2563EB";
    map.setPaintProperty("coverage-focus-fill", "fill-color", accent);
    map.setPaintProperty("coverage-focus-line", "line-color", accent);
    map.setPaintProperty("coverage-focus-casing", "line-color", token("--background") || "#ffffff");
  }

  // Outlined: every dataset (or the matches) zoomed out, and the matches at
  // any zoom while searching; the hovered and selected ones always. While one
  // is selected, the others fade to 50%.
  function applyDatasetOutlines() {
    if (!map?.getLayer("coverage-focus-line")) return;
    const focus = [...new Set([outlines.hover, outlines.selected, outlines.flash].filter(Boolean))];
    const inFocus = ["in", ["get", "id"], ["literal", focus]];
    for (const id of ["coverage-focus-fill", "coverage-focus-casing", "coverage-focus-line"]) map.setFilter(id, inFocus);
    map.setPaintProperty("coverage-focus-fill", "fill-opacity", ["case", ["==", ["get", "id"], outlines.selected ?? ""], 0.08, 0.06]);

    const matches = outlines.matches ? ["in", ["get", "id"], ["literal", outlines.matches]] : null;
    const fade = outlines.selected ? 0.5 : 1;
    for (const id of ["coverage-casing", "coverage-line"]) {
      map.setFilter(id, matches);
      map.setLayerZoomRange(id, 0, outlines.matches ? 24 : outlinesUntil);
    }
    map.setPaintProperty("coverage-casing", "line-opacity", 0.6 * fade);
    map.setPaintProperty("coverage-line", "line-opacity", fade);
  }

  // A row under the pointer (or focused) outlines its dataset, with no round trip to the server.
  function onRowHover(e) {
    const row = e.target?.closest?.("[data-dataset-row]");
    const id = row?.getAttribute("data-dataset-id") ?? null;
    if (id === outlines.hover) return;
    outlines.hover = id;
    applyDatasetOutlines();
  }

  // A built tile set's bounds stand in for coverage (D16): a dashed rectangle
  // with the world outside it shaded. The shade is a polygon with the bounds
  // as its hole.
  function addTileSetBounds(bounds) {
    const [west, south, east0, north] = bounds;
    const east = east0 < west ? east0 + 360 : east0;
    const ring = [[west, south], [east, south], [east, north], [west, north], [west, south]];
    const world = [[-540, -85.06], [540, -85.06], [540, 85.06], [-540, 85.06], [-540, -85.06]];
    map.addSource("tile-set-bounds", {
      type: "geojson",
      data: {
        type: "FeatureCollection",
        features: [
          { type: "Feature", properties: { kind: "outside" }, geometry: { type: "Polygon", coordinates: [world, [...ring].reverse()] } },
          { type: "Feature", properties: { kind: "bounds" }, geometry: { type: "LineString", coordinates: ring } },
        ],
      },
    });
    map.addLayer({ id: "tile-set-outside", type: "fill", source: "tile-set-bounds", filter: ["==", ["get", "kind"], "outside"], paint: { "fill-color": "#09090B", "fill-opacity": 0.12 } });
    map.addLayer({ id: "tile-set-casing", type: "line", source: "tile-set-bounds", filter: ["==", ["get", "kind"], "bounds"], paint: { "line-color": "#ffffff", "line-opacity": 0.6, "line-width": 3 } });
    map.addLayer({ id: "tile-set-line", type: "line", source: "tile-set-bounds", filter: ["==", ["get", "kind"], "bounds"], paint: { "line-color": "#3D8A5A", "line-width": 1.5, "line-dasharray": [4, 3] } });
    applyTileSetTheme();
  }

  function applyTileSetTheme() {
    if (!map?.getLayer("tile-set-line")) return;
    const t = effectiveTheme();
    map.setPaintProperty("tile-set-outside", "fill-opacity", t === "light" ? 0.12 : 0.35);
    map.setPaintProperty("tile-set-outside", "fill-color", t === "light" ? "#09090B" : "#000000");
    map.setPaintProperty("tile-set-casing", "line-color", token("--background") || "#ffffff");
    map.setPaintProperty("tile-set-line", "line-color", token("--outline-served") || "#3D8A5A");
  }

  // A tile that loads proves the basemap; errors with none loaded, offline or
  // blocked, turn it off and tell the page (D17).
  function onBasemapData(e) {
    if (e.sourceId === "osm" && e.tile) {
      basemapLoaded = true;
      clearTimeout(basemapFailTimer);
    }
  }

  function onError(e) {
    if (e.sourceId !== "osm" || basemapLoaded || basemap === "none" || basemapFailTimer) return;
    basemapFailTimer = setTimeout(() => {
      basemapFailTimer = undefined;
      if (basemapLoaded || basemap === "none") return;
      basemap = "none";
      applyTheme();
      dotnet?.invokeMethodAsync("OnBasemapFailed");
    }, 1500);
  }

  function startView(view) {
    return {
      center: [view.longitude, view.latitude],
      zoom: view.zoom - ZOOM_OFFSET,
      bearing: view.bearing ?? 0,
    };
  }

  // Someone edited the hash (replaceState doesn't fire this): the page reads it.
  function onHashChange() {
    dotnet?.invokeMethodAsync("OnHashChanged", location.hash);
  }

  function onMoveEnd() {
    showZoom();
    writeHash();
    dotnet?.invokeMethodAsync("OnViewChanged", pageZoom());
  }

  // Over plain HTTP on a LAN there's no Clipboard API, so a selected text area and execCommand.
  async function copyText(text) {
    try {
      if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return true;
      }
    } catch {
      // Fall back below.
    }
    const area = document.createElement("textarea");
    area.value = text;
    area.setAttribute("readonly", "");
    area.style.position = "fixed";
    area.style.opacity = "0";
    document.body.appendChild(area);
    area.select();
    let copied = false;
    try { copied = document.execCommand("copy"); } catch { copied = false; }
    area.remove();
    return copied;
  }

  return {
    readHash() {
      return location.hash;
    },

    size(container) {
      const element = document.getElementById(container);
      return [element?.clientWidth ?? 0, element?.clientHeight ?? 0];
    },

    show(container, opts, dotnetRef) {
      options = opts;
      dotnet = dotnetRef;
      hashParameters = opts.hashParameters ?? "";
      theme = opts.theme ?? "auto";
      basemap = opts.basemap ?? "muted";
      highDpi = opts.highDpi && window.devicePixelRatio > 1;
      darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
      darkQuery.addEventListener("change", applyTheme);
      window.addEventListener("hashchange", onHashChange);

      // The outlines show below the zoom the chart draws from (D13): MapLibre
      // rounds a raster zoom, so the chart's first tiles draw half a zoom early.
      outlinesUntil = opts.minZoom - 0.5 - ZOOM_OFFSET;
      // The outlines' casing and line before the tokens are read on load (D18's night has no white).
      const startOutline = { night: ["#000000", "#3E6E50"], dark: ["#09090B", "#4FA571"] }[effectiveTheme()] ?? ["#ffffff", "#3D8A5A"];
      map = new maplibregl.Map({
        container,
        attributionControl: false,
        style: {
          version: 8,
          sources: {
            osm: {
              type: "raster",
              tiles: [OSM_TILES],
              tileSize: 256,
              maxzoom: 19,
              attribution: "© <a href=\"https://www.openstreetmap.org/copyright\" target=\"_blank\" rel=\"noopener\">OpenStreetMap</a> contributors",
            },
            chart: { type: "raster", url: absolute(opts.tileJsonUrl), tileSize: 256 },
            coverage: { type: "geojson", data: JSON.parse(opts.outlines) },
          },
          layers: [
            // Painted for the theme from the start, so night never flashes a light basemap.
            { id: "osm", type: "raster", source: "osm", layout: { visibility: basemap === "none" ? "none" : "visible" }, paint: basemapPaint() },
            { id: "chart", type: "raster", source: "chart" },
            { id: "coverage-casing", type: "line", source: "coverage", maxzoom: outlinesUntil, paint: { "line-color": startOutline[0], "line-opacity": 0.6, "line-width": 3 } },
            { id: "coverage-line", type: "line", source: "coverage", maxzoom: outlinesUntil, paint: { "line-color": startOutline[1], "line-width": 1 } },
          ],
        },
        ...startView(opts.view),
      });
      map.addControl(new maplibregl.AttributionControl({ compact: false }), "bottom-right");
      map.once("load", () => {
        loaded = true;
        addDatasetLayers();
        if (opts.tileSetBounds) addTileSetBounds(opts.tileSetBounds);
        applyTheme();
      });
      // A built tile set has nothing to pick: a click says so (D35). A drag isn't a click.
      if (opts.noPick) map.on("click", () => dotnet?.invokeMethodAsync("OnNoPick"));
      else window.s100Pick?.attach(map, dotnetRef, token);
      document.addEventListener("pointerover", onRowHover);
      document.addEventListener("focusin", onRowHover);
      map.on("sourcedata", onBasemapData);
      map.on("error", onError);
      map.on("move", () => { showScale(); showZoom(); });
      map.on("rotate", showBearing);
      map.on("moveend", onMoveEnd);
      map.on("mousemove", e => showPosition(e.lngLat));
      map.on("move", () => { if (!mouseOver) showPosition(map.getCenter()); });
      map.getCanvas().addEventListener("pointerenter", e => { mouseOver = e.pointerType === "mouse"; });
      map.getCanvas().addEventListener("pointerleave", () => { mouseOver = false; showPosition(map.getCenter()); });

      // The panel opening or closing pushes the map (D1).
      resizeObserver = new ResizeObserver(() => map?.resize());
      resizeObserver.observe(document.getElementById(container));

      showScale();
      showZoom();
      showBearing();
      showPosition(map.getCenter());
      writeHash();
    },

    setChart(tileJsonUrl) {
      const apply = () => map.getSource("chart").setUrl(absolute(tileJsonUrl));
      loaded ? apply() : map.once("load", apply);
    },

    setTheme(value) {
      theme = value;
      // After Blazor has applied the page's data-theme, so the tokens are current.
      requestAnimationFrame(applyTheme);
    },

    setDatasetOutlines(selected, matches) {
      outlines.selected = selected ?? null;
      outlines.matches = matches ?? null;
      applyDatasetOutlines();
    },

    setBasemap(value) {
      basemap = value ?? "muted";
      clearTimeout(basemapFailTimer);
      basemapFailTimer = undefined;
      applyTheme();
      // Chosen again after a failure: ask for the tiles again.
      if (basemap !== "none" && !basemapLoaded && loaded) map.getSource("osm").setTiles([OSM_TILES]);
    },

    setHashParameters(value) {
      hashParameters = value ?? "";
      if (map) writeHash();
    },

    zoomBy(delta) {
      map.easeTo({ zoom: map.getZoom() + delta, duration: 250 });
    },

    zoomTo(zoom) {
      map.easeTo({ zoom: zoom - ZOOM_OFFSET, duration: 300 });
    },

    jumpTo(view) {
      map.jumpTo(startView(view));
    },

    flyTo(view) {
      map.flyTo({ ...startView(view), duration: 800 });
    },

    northUp() {
      map.easeTo({ bearing: 0, pitch: 0, duration: 300 });
    },

    // The page's URL, with the view (D36).
    copyLink() {
      return copyText(location.href);
    },

    copyText,

    // What's served changed (SW7, D29): every dataset's outline again.
    setOutlines(text) {
      const apply = () => map.getSource("coverage").setData(JSON.parse(text));
      loaded ? apply() : map.once("load", apply);
    },

    // A dataset just added: its outline shows in the accent, blinking twice, then goes back to the rest (D29).
    flashDataset(id) {
      let blinks = 0;
      const step = () => {
        outlines.flash = blinks % 2 === 0 ? id : null;
        applyDatasetOutlines();
        if (++blinks < 4) setTimeout(step, 450);
        else { outlines.flash = null; applyDatasetOutlines(); }
      };
      loaded ? step() : map.once("load", step);
    },

    dispose() {
      resizeObserver?.disconnect();
      darkQuery?.removeEventListener("change", applyTheme);
      window.removeEventListener("hashchange", onHashChange);
      document.removeEventListener("pointerover", onRowHover);
      document.removeEventListener("focusin", onRowHover);
      clearTimeout(basemapFailTimer);
      basemapFailTimer = undefined;
      window.s100Pick?.detach();
      map?.remove();
      map = undefined;
      loaded = false;
      basemapLoaded = false;
      dotnet = undefined;
    },
  };
})();

// The page's frame: the phone breakpoint and the sheet's handle (D1, D2).
window.s100Shell = (() => {
  const PHONE = "(max-width: 767px)";

  return {
    watchPhone(dotnet) {
      const query = window.matchMedia(PHONE);
      const report = () => dotnet.invokeMethodAsync("OnPhoneChanged", query.matches);
      query.addEventListener("change", report);
      report();
    },

    // A drag on the handle raises or lowers the sheet a height; a tap (or a
    // key) toggles it.
    sheetHandle(handle, dotnet) {
      let startY;
      let dragged = false;
      handle.addEventListener("pointerdown", e => {
        startY = e.clientY;
        handle.setPointerCapture(e.pointerId);
      });
      handle.addEventListener("pointerup", e => {
        if (startY === undefined) return;
        const dy = e.clientY - startY;
        startY = undefined;
        if (Math.abs(dy) > 24) {
          dragged = true;
          dotnet.invokeMethodAsync("Gesture", dy < 0 ? "up" : "down");
        }
      });
      handle.addEventListener("click", () => {
        if (dragged) { dragged = false; return; }
        dotnet.invokeMethodAsync("Gesture", "tap");
      });
    },
  };
})();

// The browser's time zone (D20). The server can't know it, so the page asks
// for its id and, at each instant it shows, the offset and abbreviation.
window.s100Time = (() => {
  const zoneId = () => {
    try { return Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC"; } catch { return "UTC"; }
  };

  let barObserver;

  return {
    // The floating bar's height, so the phone layout can lift the attribution above it.
    observeBar(element) {
      barObserver ??= new ResizeObserver(entries => {
        for (const entry of entries) {
          const body = entry.target.closest(".serve-body");
          if (body && entry.target.dataset.docked !== "true")
            body.style.setProperty("--time-bar-height", `${Math.ceil(entry.target.getBoundingClientRect().height) + 8}px`);
        }
      });
      barObserver.observe(element);
    },

    unobserveBar(element) {
      barObserver?.unobserve(element);
    },

    // instants: ISO 8601 UTC strings. Returns { id, offsets: [minutes], abbreviations: [text] }.
    zone(instants) {
      const id = zoneId();
      const wall = new Intl.DateTimeFormat("en-US", {
        timeZone: id, hourCycle: "h23",
        year: "numeric", month: "numeric", day: "numeric", hour: "numeric", minute: "numeric", second: "numeric",
      });
      // The browser's own language first; English as a fallback, where "BST" or "PDT" beats "GMT+1".
      const names = [navigator.language, "en-US", "en-GB"].flatMap(locale => {
        try { return [new Intl.DateTimeFormat(locale, { timeZone: id, timeZoneName: "short" })]; } catch { return []; }
      });
      const offsets = [];
      const abbreviations = [];
      for (const text of instants) {
        const date = new Date(text);
        const parts = Object.fromEntries(wall.formatToParts(date).map(p => [p.type, p.value]));
        const local = Date.UTC(+parts.year, +parts.month - 1, +parts.day, +parts.hour % 24, +parts.minute, +parts.second);
        offsets.push(Math.round((local - Math.floor(date.getTime() / 1000) * 1000) / 60000));
        let abbreviation = "";
        for (const format of names) {
          const name = format.formatToParts(date).find(p => p.type === "timeZoneName")?.value ?? "";
          if (name && !/\d/.test(name)) { abbreviation = name; break; }
          if (!abbreviation) abbreviation = name;
        }
        abbreviations.push(abbreviation);
      }
      return { id, offsets, abbreviations };
    },
  };
})();
