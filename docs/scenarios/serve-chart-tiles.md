# Serve charts to a web map or GIS

This guide serves a folder of charts as map tiles with `s100 tiles serve`, then
adds them to QGIS and identifies a feature on them. Use it to show S-100 and
S-57 data in a web map, a GIS desktop or any other tool that takes XYZ tiles,
WMTS or WMS, without converting the data first.

## Prerequisites

- The `s100` command-line tool. See
  [Getting started](../getting-started.md#command-line-tool). To run it in a
  container instead, see [Run s100 in Docker](../docker.md).
- A folder of datasets: S-57 cells, S-100 exchange sets or loose S-100
  datasets, in any mix. The repository's S-57 test cell,
  [`tests/datasets/S57/US5MA1BO`](https://github.com/philliphoff/EncDotNet.S100/tree/main/tests/datasets/S57/US5MA1BO),
  is a small NOAA chart of Nantucket Harbor.
- [QGIS](https://qgis.org/), for steps 3 and 4. Any other WMTS or WMS client
  works the same way.

## Steps

1. Serve the folder:

   ```bash
   s100 tiles serve path/to/charts
   ```

   `s100` portrays every dataset in the folder and its subfolders, then prints
   the URLs it serves:

   ```text
   Serving 1 dataset(s) (S-57) (rendered on demand, png, zoom 13–15):
     Tiles:    http://127.0.0.1:8200/{z}/{x}/{y}.png
     TileJSON: http://127.0.0.1:8200/tiles.json
     Palettes: http://127.0.0.1:8200/{palette}/{z}/{x}/{y}.png (day, dusk, night)
     WMTS:     http://127.0.0.1:8200/wmts/1.0.0/WMTSCapabilities.xml
     WMS:      http://127.0.0.1:8200/wms
     Preview:  http://127.0.0.1:8200/
   ```

   The zoom levels come from the datasets' compilation scales. Tiles are
   rendered the first time they're asked for.

2. Open `http://127.0.0.1:8200/` in a browser. The preview page shows the
   tiles over OpenStreetMap, with a menu to switch between the day, dusk and
   night palettes.

3. In QGIS, choose **Layer > Add Layer > Add WMS/WMTS Layer**. Add a new
   connection with the WMTS URL,
   `http://127.0.0.1:8200/wmts/1.0.0/WMTSCapabilities.xml`, connect, and add
   the `s100` layer.

4. To see what's at a point, add a second connection with the WMS URL,
   `http://127.0.0.1:8200/wms`, and add its `s100` layer. Choose the
   **Identify Features** tool and click the chart. The result lists the
   features there with their attributes, and coverage values such as S-102
   depths.

## Result

QGIS shows the chart, portrayed with the S-101 portrayal catalogue (S-57 cells
are translated to S-101 first), and the WMS layer answers identify requests
from the datasets. `tiles serve` keeps running until you press Ctrl+C. It
checks the folder every 10 seconds, so a new edition or a new cell you copy in
is served without a restart.

## Use the tiles in a web page

Add the TileJSON as a raster source in MapLibre GL JS:

```js
map.addSource("chart", {
  type: "raster",
  url: "http://127.0.0.1:8200/tiles.json",
  tileSize: 256,
});
map.addLayer({ id: "chart", type: "raster", source: "chart" });
```

In Leaflet, use the tile URL:

```js
L.tileLayer("http://127.0.0.1:8200/{z}/{x}/{y}.png").addTo(map);
```

For the night palette, put `night/` before the path, for example
`http://127.0.0.1:8200/night/tiles.json`.

## Serve some of the datasets

`--only` keeps some product specifications from a folder, and `--bbox` keeps
the datasets that draw in an area. Both also cut the memory the server needs:

```bash
s100 tiles serve path/to/charts --only S57,S102 --bbox -70.35,41.25,-70.27,41.33
```

## Let other computers connect

By default only this computer can connect. To serve your network, listen on
every address. `s100` then adds an access token to every URL it prints:

```bash
s100 tiles serve path/to/charts --host 0.0.0.0
```

Pass `--token <token>` to keep the same URLs across restarts. The server speaks
plain HTTP; put a proxy that adds HTTPS in front of it to serve beyond a
network you trust.

## Show tide and current time steps

For S-104 water levels and S-111 currents, add `?t=` and a UTC time to the tile
or TileJSON URL, such as `tiles.json?t=2026-03-21T07:00Z`. WMTS and WMS clients
get the time steps as a `Time` dimension.

## Serve prebuilt tiles

Rendering on demand needs memory for every portrayed dataset. On a small
computer, render the tiles once with `tiles export` and serve the file:

```bash
s100 tiles export path/to/charts -o charts.pmtiles
s100 tiles serve charts.pmtiles
```

The server then starts at once and uses little memory. `tiles export` also
writes MBTiles, GeoPackage and Cloud-Optimized GeoTIFF files, which QGIS and
GDAL open without a server.

## See also

- [`tiles serve`](../cli.md#tiles-serve), [WMTS](../cli.md#wmts) and
  [WMS](../cli.md#wms): every route and option.
- [`tiles export`](../cli.md#tiles-export): the tile set formats and how many
  tiles each zoom level adds.
- [Run s100 in Docker](../docker.md): serve tiles from a container, with
  Docker Compose, health checks and memory limits.
