// A thin MapLibre wrapper: the Blazor side owns state, this owns the map.
let map;

export function showChart(container, tileJsonUrl) {
  if (!map) {
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
          chart: { type: "raster", url: tileJsonUrl, tileSize: 256 },
        },
        layers: [
          { id: "osm", type: "raster", source: "osm" },
          { id: "chart", type: "raster", source: "chart" },
        ],
      },
      center: [0, 0],
      zoom: 1,
    });
    map.addControl(new maplibregl.NavigationControl());
    return;
  }
  const apply = () => map.getSource("chart").setUrl(tileJsonUrl);
  map.isStyleLoaded() ? apply() : map.once("load", apply);
}

export function fitBounds([w, s, e, n]) {
  if (!map) return;
  const fit = () => map.fitBounds([[w, s], [e, n]], { padding: 20, animate: false });
  map.loaded() ? fit() : map.once("load", fit);
}
