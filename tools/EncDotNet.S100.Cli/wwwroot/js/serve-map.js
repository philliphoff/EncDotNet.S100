// The map of the tiles serve web page (issue #1575). The Razor component owns
// the state; this owns the MapLibre map. URLs are relative to the page.
window.s100Map = (() => {
  let map;

  // On a high-DPI screen, the 512-pixel @2x tiles when they're served.
  let highDpi = false;
  const absolute = url => {
    const resolved = new URL(url, document.baseURI);
    if (highDpi) resolved.searchParams.set("scale", "2");
    return resolved.href;
  };

  return {
    show(container, tileJsonUrl, serveHighDpi, bounds, center) {
      highDpi = serveHighDpi && window.devicePixelRatio > 1;
      map = new maplibregl.Map({
        container,
        style: {
          version: 8,
          sources: {
            osm: {
              type: "raster",
              tiles: ["https://tile.openstreetmap.org/{z}/{x}/{y}.png"],
              tileSize: 256,
              attribution: "© OpenStreetMap contributors",
            },
            chart: { type: "raster", url: absolute(tileJsonUrl), tileSize: 256 },
          },
          layers: [
            { id: "osm", type: "raster", source: "osm" },
            { id: "chart", type: "raster", source: "chart" },
          ],
        },
        center: center ? [center[0], center[1]] : [0, 0],
        zoom: center ? center[2] : 1,
      });
      map.addControl(new maplibregl.NavigationControl());
      if (bounds) {
        const [w, s, e, n] = bounds;
        map.fitBounds([[w, s], [e, n]], { padding: 20, animate: false });
      }
    },

    setChart(tileJsonUrl) {
      const apply = () => map.getSource("chart").setUrl(absolute(tileJsonUrl));
      map.isStyleLoaded() ? apply() : map.once("load", apply);
    },

    dispose() {
      map?.remove();
      map = undefined;
    },
  };
})();
