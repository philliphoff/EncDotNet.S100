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

  let map;
  let dotnet;
  let options;
  let hashParameters = "";
  let theme = "auto";
  let resizeObserver;
  let darkQuery;
  let mouseOver = false;
  let loaded = false;

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

  // The basemap, muted (Q4), dimmed for dark chrome, inverted and dimmed for night; the outlines' colours.
  function applyTheme() {
    // isStyleLoaded() is false while a source reloads; the layers are there once the map has loaded.
    if (!map || !loaded) return;
    const t = effectiveTheme();
    const paint = t === "night" ? [0.22, 0] : t === "dark" ? [0, 0.6] : [0.1, 1];
    map.setPaintProperty("osm", "raster-saturation", -1);
    map.setPaintProperty("osm", "raster-contrast", t === "light" ? -0.25 : -0.1);
    map.setPaintProperty("osm", "raster-brightness-min", paint[0]);
    map.setPaintProperty("osm", "raster-brightness-max", paint[1]);
    map.setPaintProperty("coverage-casing", "line-color", token("--background") || "#ffffff");
    map.setPaintProperty("coverage-line", "line-color", token("--outline-served") || "#3D8A5A");
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
      highDpi = opts.highDpi && window.devicePixelRatio > 1;
      darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
      darkQuery.addEventListener("change", applyTheme);
      window.addEventListener("hashchange", onHashChange);

      // The outlines show below the zoom the chart draws from (D13): MapLibre
      // rounds a raster zoom, so the chart's first tiles draw half a zoom early.
      const outlinesUntil = opts.minZoom - 0.5 - ZOOM_OFFSET;
      map = new maplibregl.Map({
        container,
        attributionControl: false,
        style: {
          version: 8,
          sources: {
            osm: {
              type: "raster",
              tiles: ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],
              tileSize: 256,
              maxzoom: 19,
              attribution: "© <a href=\"https://www.openstreetmap.org/copyright\" target=\"_blank\" rel=\"noopener\">OpenStreetMap</a> contributors",
            },
            chart: { type: "raster", url: absolute(opts.tileJsonUrl), tileSize: 256 },
            coverage: { type: "geojson", data: JSON.parse(opts.outlines) },
          },
          layers: [
            { id: "osm", type: "raster", source: "osm", paint: { "raster-saturation": -1, "raster-contrast": -0.25, "raster-brightness-min": 0.1 } },
            { id: "chart", type: "raster", source: "chart" },
            { id: "coverage-casing", type: "line", source: "coverage", maxzoom: outlinesUntil, paint: { "line-color": "#ffffff", "line-opacity": 0.6, "line-width": 3 } },
            { id: "coverage-line", type: "line", source: "coverage", maxzoom: outlinesUntil, paint: { "line-color": "#3D8A5A", "line-width": 1 } },
          ],
        },
        ...startView(opts.view),
      });
      map.addControl(new maplibregl.AttributionControl({ compact: false }), "bottom-right");
      map.once("load", () => { loaded = true; applyTheme(); });
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

    // The page's URL, with the view (D36). Over plain HTTP on a LAN there's
    // no Clipboard API, so a selected text area and execCommand.
    async copyLink() {
      const text = location.href;
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
    },

    dispose() {
      resizeObserver?.disconnect();
      darkQuery?.removeEventListener("change", applyTheme);
      window.removeEventListener("hashchange", onHashChange);
      map?.remove();
      map = undefined;
      loaded = false;
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
