// Features at a point on the tiles serve web page (issue #1581). serve-map.js
// hands this the map; the Razor page owns the pick and the card's content.
//
// A click picks (D21): a drag doesn't (MapLibre fires no click after one), and
// Esc clears it. The marker and the highlight are GeoJSON layers (D24); on a
// desktop the card floats beside the marker and flips to stay on the map (D22).
window.s100Pick = (() => {
  const GAP = 18;
  const MARGIN = 12;
  const BUTTONS = 64; // the map's button column on the right (D5)
  const EMPTY = { type: "FeatureCollection", features: [] };

  let map;
  let dotnet;
  let token;
  let ready = false;
  let point;          // [lng, lat] of the marker, or undefined
  let geometries = []; // highlight geometry per feature index (null where there's none)
  let expanded = -1;
  let hovered = -1;

  const card = () => document.querySelector("[data-pick-card]");

  function whenReady(action) {
    if (!map) return;
    if (ready) action(); else map.once("load", action);
  }

  function addLayers() {
    map.addSource("pick-highlight", { type: "geojson", data: EMPTY });
    map.addSource("pick-marker", { type: "geojson", data: EMPTY });
    const polygon = ["==", ["geometry-type"], "Polygon"];
    const notPoint = ["!=", ["geometry-type"], "Point"];
    map.addLayer({ id: "pick-highlight-fill", type: "fill", source: "pick-highlight", filter: polygon, paint: { "fill-opacity": 0.1 } });
    // A casing under the accent line, so it reads over the chart's blue water.
    map.addLayer({ id: "pick-highlight-casing", type: "line", source: "pick-highlight", filter: notPoint, paint: { "line-width": 5, "line-opacity": 0.85 } });
    map.addLayer({ id: "pick-highlight-line", type: "line", source: "pick-highlight", filter: notPoint, paint: { "line-width": 2 } });
    map.addLayer({ id: "pick-highlight-point", type: "circle", source: "pick-highlight", filter: ["==", ["geometry-type"], "Point"], paint: { "circle-radius": 10, "circle-color": "rgba(0,0,0,0)", "circle-stroke-width": 2.5 } });
    map.addLayer({ id: "pick-marker-ring", type: "circle", source: "pick-marker", paint: { "circle-radius": 15, "circle-opacity": 0.14, "circle-stroke-width": 2.5 } });
    map.addLayer({ id: "pick-marker-dot", type: "circle", source: "pick-marker", paint: { "circle-radius": 4.5, "circle-stroke-width": 1.5 } });
    ready = true;
    applyTheme();
    showMarker();
    showHighlight();
  }

  function applyTheme() {
    if (!ready) return;
    const accent = token("--selected") || "#2563EB";
    const ground = token("--background") || "#FFFFFF";
    map.setPaintProperty("pick-highlight-fill", "fill-color", accent);
    map.setPaintProperty("pick-highlight-casing", "line-color", ground);
    map.setPaintProperty("pick-highlight-line", "line-color", accent);
    map.setPaintProperty("pick-highlight-point", "circle-stroke-color", accent);
    map.setPaintProperty("pick-marker-ring", "circle-color", accent);
    map.setPaintProperty("pick-marker-ring", "circle-stroke-color", accent);
    map.setPaintProperty("pick-marker-dot", "circle-color", accent);
    map.setPaintProperty("pick-marker-dot", "circle-stroke-color", ground);
  }

  function showMarker() {
    if (!ready) return;
    map.getSource("pick-marker").setData(point
      ? { type: "Feature", properties: {}, geometry: { type: "Point", coordinates: point } }
      : EMPTY);
  }

  // The hovered feature, else the expanded one (D24).
  function showHighlight() {
    if (!ready) return;
    const index = hovered >= 0 ? hovered : expanded;
    const feature = point && index >= 0 ? geometries[index] : null;
    map.getSource("pick-highlight").setData(feature ?? EMPTY);
  }

  // Beside the marker, on the side with room, inside the map (D22). With
  // fit, a new pick where neither side has room pans the map just enough.
  function place(fit) {
    const element = card();
    if (!element || !map || !point) return;
    const area = map.getContainer();
    const width = area.clientWidth;
    // Above the time bar when there is one (SW4), which sits over the map's bottom.
    const timeBar = area.parentElement?.querySelector(".time-bar");
    const height = timeBar
      ? Math.min(area.clientHeight, timeBar.getBoundingClientRect().top - area.getBoundingClientRect().top)
      : area.clientHeight;
    element.style.maxHeight = `${Math.max(160, height - 2 * MARGIN)}px`;
    const at = map.project(point);
    const w = element.offsetWidth;
    const h = element.offsetHeight;
    const right = at.x + GAP + w + BUTTONS <= width;
    const left = at.x - GAP - w >= MARGIN;
    const side = right || !left && width - at.x >= at.x ? "right" : "left";
    if (fit === true && !right && !left) {
      const target = side === "right" ? width - BUTTONS - w - GAP : MARGIN + w + GAP;
      map.panBy([at.x - target, 0], { duration: 250 });
      return;
    }
    let x = side === "right" ? at.x + GAP : at.x - GAP - w;
    x = Math.max(MARGIN, Math.min(x, width - w - BUTTONS));
    let y = at.y - Math.min(h / 2, 80);
    y = Math.max(MARGIN, Math.min(y, height - h - MARGIN));
    element.style.left = `${Math.round(x)}px`;
    element.style.top = `${Math.round(y)}px`;
    element.style.setProperty("--pointer-y", `${Math.round(Math.max(14, Math.min(at.y - y, h - 14)))}px`);
    element.dataset.side = side;
    const onMap = at.x >= 0 && at.x <= width && at.y >= 0 && at.y <= height;
    element.dataset.placed = "true";
    element.dataset.pointer = onMap ? "true" : "false";
  }

  function onClick(e) {
    // A click on the map picks; the card and the controls sit outside the canvas.
    const metresPerPixel = 40075016.686 * Math.cos(e.lngLat.lat * Math.PI / 180) / (512 * 2 ** map.getZoom());
    dotnet?.invokeMethodAsync("OnMapClick", e.lngLat.lat, e.lngLat.lng, metresPerPixel);
  }

  function onKey(e) {
    if (e.key === "Escape" && point) dotnet?.invokeMethodAsync("OnPickEscape");
  }

  // A feature row under the pointer or focus is highlighted (D24).
  function onOver(e) {
    const root = e.target.closest?.("[data-pick-root]");
    if (!root) return;
    const row = e.target.closest("[data-pick-index]");
    const index = row ? Number(row.dataset.pickIndex) : -1;
    if (index !== hovered) {
      hovered = index;
      showHighlight();
    }
  }

  function onOut(e) {
    const root = e.target.closest?.("[data-pick-root]");
    if (root && !root.contains(e.relatedTarget) && hovered !== -1) {
      hovered = -1;
      showHighlight();
    }
  }

  return {
    // From serve-map.js once the map exists.
    attach(mapInstance, dotnetRef, tokenOf) {
      map = mapInstance;
      dotnet = dotnetRef;
      token = tokenOf;
      ready = false;
      map.once("load", addLayers);
      map.on("click", onClick);
      map.on("move", () => place());
      map.on("resize", () => place());
      document.addEventListener("keydown", onKey);
      document.addEventListener("pointerover", onOver);
      document.addEventListener("pointerout", onOut);
      document.addEventListener("focusin", onOver);
      document.addEventListener("focusout", onOut);
    },

    applyTheme,

    detach() {
      document.removeEventListener("keydown", onKey);
      document.removeEventListener("pointerover", onOver);
      document.removeEventListener("pointerout", onOut);
      document.removeEventListener("focusin", onOver);
      document.removeEventListener("focusout", onOut);
      map = undefined;
      dotnet = undefined;
      ready = false;
      point = undefined;
      geometries = [];
    },

    // The marker at a point, or none; a new point drops the old highlight.
    setPoint(lat, lng) {
      point = lat === null || lat === undefined ? undefined : [lng, lat];
      geometries = [];
      expanded = -1;
      hovered = -1;
      whenReady(() => { showMarker(); showHighlight(); });
    },

    // The features' geometries (JSON array, by index) and the expanded one.
    setFeatures(json, expandedIndex) {
      geometries = JSON.parse(json);
      expanded = expandedIndex;
      whenReady(showHighlight);
    },

    setExpanded(index) {
      expanded = index;
      whenReady(showHighlight);
    },

    // After the card renders or changes size; fit after a new pick.
    place(fit) {
      // Now, which the render has laid out, and again on the next frame in
      // case the card's size settles later.
      place(fit);
      requestAnimationFrame(() => place());
      const element = card();
      if (element && element.dataset.focused !== "true") {
        element.dataset.focused = "true";
        element.focus({ preventScroll: true });
      }
    },

    // On a phone the sheet covers the map's lower part: bring the marker
    // above it if it's hidden (D22).
    keepInView() {
      whenReady(() => {
        if (!point) return;
        // Where the sheet's top settles (serve.css: half is min(430px, 100% - 120px),
        // full leaves 60 px), not where it is mid-transition.
        const area = map.getContainer().getBoundingClientRect();
        const height = document.querySelector(".sheet")?.dataset.height;
        const top = height === "full" ? 60 : area.height - Math.min(430, area.height - 120);
        const at = map.project(point);
        if (at.y > 56 && at.y < top - 40 && at.x > 16 && at.x < area.width - 16) return;
        const visibleMiddle = Math.max(80, top / 2);
        map.easeTo({ center: point, offset: [0, visibleMiddle - area.height / 2], duration: 300 });
      });
    },

    // What's shown, for checks: the marker and the highlighted feature's geometry type.
    state() {
      const index = hovered >= 0 ? hovered : expanded;
      return { point, expanded, hovered, highlight: point && index >= 0 ? geometries[index]?.geometry?.type ?? null : null };
    },

    // A point from the hash: the radius at the map's zoom.
    metresPerPixel(lat) {
      return map ? 40075016.686 * Math.cos(lat * Math.PI / 180) / (512 * 2 ** map.getZoom()) : 10;
    },
  };
})();
