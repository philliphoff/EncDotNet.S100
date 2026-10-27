"""Builds the compact marine zone layer bundled with EncDotNet.S100.Datasets.Cap
(src/EncDotNet.S100.Datasets.Cap/Resources/MarineZones.geojson) from a directory
of NWS marine zone features, one GeoJSON feature per file with the shapefile's
ID, NAME and WFO attributes (what fetch_marine_zones.py writes).

Each ring is rounded to four decimals and simplified with Douglas-Peucker at
the tolerance given (0.005 degrees for the bundled layer); holes smaller than
the tolerance are dropped. The output is one feature per line, sorted by code.

Usage: python3 -I build_marine_zones.py <zone-dir> <out.geojson> 0.005
"""
import json, os, sys, collections

src, dst, tol = sys.argv[1], sys.argv[2], float(sys.argv[3])
DIGITS = 4


def dp(points, eps):
    if len(points) < 3:
        return points
    keep = [False] * len(points)
    keep[0] = keep[-1] = True
    stack = [(0, len(points) - 1)]
    while stack:
        a, b = stack.pop()
        ax, ay = points[a]; bx, by = points[b]
        dx, dy = bx - ax, by - ay
        norm = (dx * dx + dy * dy) ** 0.5
        best, idx = -1.0, -1
        for i in range(a + 1, b):
            px, py = points[i]
            if norm == 0:
                d = ((px - ax) ** 2 + (py - ay) ** 2) ** 0.5
            else:
                d = abs(dy * px - dx * py + bx * ay - by * ax) / norm
            if d > best:
                best, idx = d, i
        if best > eps:
            keep[idx] = True
            stack.append((a, idx)); stack.append((idx, b))
    return [p for p, k in zip(points, keep) if k]


def area(ring):
    s = 0.0
    for (x1, y1), (x2, y2) in zip(ring, ring[1:]):
        s += x1 * y2 - x2 * y1
    return abs(s) / 2


def simplify_ring(ring):
    ring = [(round(x, DIGITS), round(y, DIGITS)) for x, y in ring]
    dedup = [ring[0]]
    for p in ring[1:]:
        if p != dedup[-1]:
            dedup.append(p)
    if dedup[0] != dedup[-1]:
        dedup.append(dedup[0])
    # Split the closed ring at its farthest point so DP keeps its shape.
    out = dp(dedup, tol)
    if len(out) < 4:
        return None
    return [[x, y] for x, y in out]


features = []
stats = collections.Counter()
for name in sorted(os.listdir(src)):
    with open(os.path.join(src, name)) as f:
        feat = json.load(f)
    props = feat["properties"]
    geom = feat["geometry"]
    polys = geom["coordinates"] if geom["type"] == "MultiPolygon" else [geom["coordinates"]]
    out_polys = []
    for poly in polys:
        rings = [simplify_ring(r) for r in poly]
        if rings[0] is None:
            # Keep tiny zones as their unsimplified outline.
            rings[0] = [[round(x, DIGITS), round(y, DIGITS)] for x, y in poly[0]]
            if area(rings[0]) == 0:
                stats["dropped_part"] += 1
                continue
        holes = [r for r in rings[1:] if r is not None and area(r) > tol * tol * 4]
        out_polys.append([rings[0]] + holes)
    if not out_polys:
        stats["dropped_zone"] += 1
        continue
    geometry = {"type": "Polygon", "coordinates": out_polys[0]} if len(out_polys) == 1 else {"type": "MultiPolygon", "coordinates": out_polys}
    features.append({"type": "Feature", "id": props["ID"], "properties": {"name": props.get("NAME", ""), "wfo": props.get("WFO", "")}, "geometry": geometry})
    stats["zones"] += 1
    stats["vertices"] += sum(len(r) for p in out_polys for r in p)

collection = {
    "type": "FeatureCollection",
    "name": "NWS marine zones",
    "source": "NWS coastal (mz18mr25) and offshore (oz18mr25) marine zone shapefiles, https://www.weather.gov/gis/MarineZones",
    "license": "Public domain (US Government work)",
    "simplification": f"Douglas-Peucker, {tol} degrees; coordinates rounded to {DIGITS} decimals",
    "features": features,
}
with open(dst, "w") as f:
    head = {k: v for k, v in collection.items() if k != "features"}
    f.write(json.dumps(head, separators=(",", ":"))[:-1] + ',"features":[\n')
    f.write(",\n".join(json.dumps(x, separators=(",", ":")) for x in features))
    f.write("\n]}\n")
print(dict(stats), os.path.getsize(dst))
